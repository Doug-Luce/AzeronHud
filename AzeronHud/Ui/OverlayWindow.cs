using System;
using System.Numerics;
using AzeronHud.Azeron;
using AzeronHud.Game;
using AzeronHud.Layout;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AzeronHud.Ui;

/// <summary>
/// The keypad itself: the configurator's grid, drawn over the game, with each
/// cell labelled by what the game says that key does.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private readonly Configuration cfg;
    private readonly Func<DeviceLayout?> layout;
    private readonly Func<ButtonResolver> resolver;
    private readonly Func<string?> statusLine;

    internal OverlayWindow(
        Configuration cfg,
        Func<DeviceLayout?> layout,
        Func<ButtonResolver> resolver,
        Func<string?> statusLine)
        : base("Azeron HUD##AzeronHudOverlay")
    {
        this.cfg = cfg;
        this.layout = layout;
        this.resolver = resolver;
        this.statusLine = statusLine;

        // Esc belongs to the game while this is up, and it must stay on the
        // main viewport -- this install runs with IsDisableViewport set.
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        ForceMainWindow = true;

        // First open only -- after that ImGui remembers whatever the user
        // dragged it to. Roughly the Cyborg II grid at the Azeron app's own
        // medium button size.
        Size = new Vector2(650, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override bool DrawConditions()
    {
        if (!Plugin.ClientState.IsLoggedIn)
            return false;

        if (cfg.HideInCutscenes &&
            (Plugin.Condition[ConditionFlag.WatchingCutscene]
             || Plugin.Condition[ConditionFlag.WatchingCutscene78]
             || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]))
            return false;

        return true;
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoFocusOnAppearing;

        // Scale-to-window means the user drags the frame and the keypad follows.
        // The fixed mode is the opposite: the keypad's cell size decides, and
        // the window shrink-wraps it.
        if (!cfg.ScaleToWindow)
            Flags |= ImGuiWindowFlags.AlwaysAutoResize;

        if (cfg.Locked)
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;

        // Small enough to tuck away, but not so small the grid collapses.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(220, 160),
            MaximumSize = new Vector2(4000, 4000),
        };

        AllowClickthrough = cfg.Locked;
        BgAlpha = cfg.Opacity * 0.5f;
    }

    public override void Draw()
    {
        var dev = layout();
        if (dev == null)
        {
            ImGui.TextUnformatted(statusLine() ?? "No Azeron layout loaded.");
            return;
        }

        var buttons = resolver().Buttons;
        if (buttons.Count == 0)
        {
            ImGui.TextUnformatted(statusLine() ?? "No Azeron profile loaded.");
            return;
        }

        // One scalar drives every rect this frame. Scale is applied here and
        // nowhere else -- mixing it with SetWindowFontScale is what makes hover
        // rects drift away from what's drawn.
        var avail = ImGui.GetContentRegionAvail();
        var cell = cfg.ScaleToWindow
            ? dev.CellSizeToFit(avail, cfg.GapRatio)
            : cfg.CellSize * ImGuiHelpers.GlobalScale;

        var gap = cell * cfg.GapRatio;
        var grid = dev.GridSize(cell, gap);

        // Fitting preserves the grid's proportions, so one axis has slack.
        // Centre in it rather than letting the keypad sit in a corner.
        var origin = ImGui.GetCursorScreenPos() + new Vector2(
            MathF.Max(0f, (avail.X - grid.X) / 2f),
            MathF.Max(0f, (avail.Y - grid.Y) / 2f));

        var draw = ImGui.GetWindowDrawList();

        foreach (var lb in dev.Buttons)
        {
            if (!buttons.TryGetValue(lb.Id, out var b))
                continue;
            if (b.Tier == ResolutionTier.Empty && !cfg.ShowEmptyButtons)
                continue;

            var (offset, size) = dev.Rect(lb, cell, gap);
            DrawButton(draw, b, origin + offset, size, cell);
        }

        // Only the shrink-wrapping mode needs something to measure. Reserving
        // the grid while scaling to the window would fight the user's resize.
        if (!cfg.ScaleToWindow)
        {
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(grid);
        }
    }

    private void DrawButton(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        var p1 = pos + size;
        var rounding = cfg.Rounding * ImGuiHelpers.GlobalScale;
        var pressed = cfg.HighlightPressed && IsHeld(b);

        // Everything inside a cell stays inside it -- icons, cooldown and long
        // action names all overflow otherwise.
        draw.PushClipRect(pos, p1, true);

        draw.AddRectFilled(pos, p1, Palette.Background(b.Tier, cfg.Opacity), rounding);

        if (cfg.ShowIcons && b.IconId != 0)
            DrawIcon(draw, b, pos, size, cfg.Opacity);

        if (cfg.ShowCooldown && b.CooldownPct > 0f)
            DrawCooldown(draw, b, pos, size, cell);

        if (cfg.DimUnusable && !b.Usable && b.Tier == ResolutionTier.HotbarSlot)
            draw.AddRectFilled(pos, p1, Palette.Fade(Palette.UnusableVeil, cfg.Opacity), rounding);

        draw.AddRect(
            pos, p1,
            pressed
                ? Palette.Fade(Palette.Pressed, cfg.Opacity)
                : Palette.Border(b.Tier, cfg.Opacity),
            rounding, ImDrawFlags.None, pressed ? 2.5f : 1.5f);

        DrawText(draw, b, pos, size, cell);

        if (cfg.ShowSecondaryPips && (b.HasLongPress || b.HasDoublePress))
            DrawPips(draw, b, pos, size, cell, cfg.Opacity);

        draw.PopClipRect();

        // Hover-test against the exact rect we just drew -- same coordinates,
        // so there's no second scale path for them to drift apart on.
        //
        // Deliberately not an InvisibleButton: an item covering every cell eats
        // the drag that moves and resizes the window, which on a borderless HUD
        // leaves nothing to grab.
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)
            && ImGui.IsMouseHoveringRect(pos, p1))
            DrawTooltip(b);
    }

    private static void DrawIcon(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float opacity)
    {
        var shared = Plugin.Textures.GetFromGameIcon(new GameIconLookup(b.IconId));
        if (!shared.TryGetWrap(out var wrap, out _))
            return;

        // Square icon, centred in the upper part of the cell so the label has room.
        var side = MathF.Min(size.X, size.Y) * 0.56f;
        var topLeft = new Vector2(pos.X + ((size.X - side) / 2f), pos.Y + (size.Y * 0.06f));
        var tint = Palette.Fade(
            b.Usable ? 0xFFFFFFFFu : ImGui.ColorConvertFloat4ToU32(Palette.IconTintUnusable),
            opacity);

        draw.AddImage(wrap.Handle, topLeft, topLeft + new Vector2(side, side),
            Vector2.Zero, Vector2.One, tint);
    }

    /// <summary>
    /// Dark veil that drains off the top as the recast elapses.
    ///
    /// Not the hotbar's radial sweep: a pie past 180 degrees isn't convex, and
    /// the only fills in this ImGui binding are the convex ones, so a wedge
    /// would render as garbage. At keypad-cell size a vertical drain reads
    /// better than a tiny pie anyway.
    /// </summary>
    private void DrawCooldown(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        draw.AddRectFilled(
            pos,
            new Vector2(pos.X + size.X, pos.Y + (size.Y * b.CooldownPct)),
            Palette.Fade(Palette.CooldownVeil, cfg.Opacity));

        if (!cfg.ShowCooldownNumerals || b.CooldownSeconds <= 0)
            return;

        var centre = pos + (size / 2f);

        var text = b.CooldownSeconds.ToString();
        var scale = cell / ImGui.GetFontSize() * 0.34f;
        var extent = ImGui.CalcTextSize(text) * scale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
            centre - (extent / 2f), Palette.Fade(Palette.Title, cfg.Opacity), text);
    }

    private void DrawText(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        var pad = cell * 0.06f;

        if (cfg.ShowChord && b.Chord.Length > 0)
        {
            var scale = cell / ImGui.GetFontSize() * 0.17f;
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                pos + new Vector2(pad, pad), Palette.Fade(Palette.Chord, cfg.Opacity), b.Chord);
        }

        if (b.Charges > 1)
        {
            var text = b.Charges.ToString();
            var scale = cell / ImGui.GetFontSize() * 0.2f;
            var extent = ImGui.CalcTextSize(text) * scale;
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                new Vector2(pos.X + size.X - extent.X - pad, pos.Y + pad),
                Palette.Fade(Palette.Charges, cfg.Opacity), text);
        }

        if (b.Title.Length == 0)
            return;

        // The title gets the bottom strip, clipped rather than ellipsised --
        // action names are long and the cell is narrow.
        var titleScale = cell / ImGui.GetFontSize() * 0.19f;
        var titleSize = ImGui.GetFontSize() * titleScale;
        var measured = ImGui.CalcTextSize(b.Title) * titleScale;
        var titlePos = new Vector2(
            pos.X + MathF.Max(pad, (size.X - measured.X) / 2f),
            pos.Y + size.Y - titleSize - pad);

        draw.PushClipRect(pos + new Vector2(pad * 0.5f, 0), pos + size - new Vector2(pad * 0.5f, 0), true);
        draw.AddText(ImGui.GetFont(), titleSize, titlePos,
            Palette.Fade(
                b.Tier is ResolutionTier.Empty or ResolutionTier.RawKey ? Palette.TitleDim : Palette.Title,
                cfg.Opacity),
            b.Title);
        draw.PopClipRect();
    }

    /// <summary>One dot for a long-press bind, two for a double-press.</summary>
    private static void DrawPips(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell, float opacity)
    {
        var r = cell * 0.035f;
        var y = pos.Y + size.Y - (r * 2.2f);
        var x = pos.X + size.X - (r * 2.2f);

        if (b.HasLongPress)
        {
            draw.AddCircleFilled(new Vector2(x, y), r, Palette.Fade(Palette.Pip, opacity));
            x -= r * 2.6f;
        }

        if (b.HasDoublePress)
        {
            draw.AddCircleFilled(new Vector2(x, y), r, Palette.Fade(Palette.Pip, opacity));
            draw.AddCircleFilled(new Vector2(x - (r * 2.6f), y), r, Palette.Fade(Palette.Pip, opacity));
        }
    }

    private static void DrawTooltip(ResolvedButton b)
    {
        using var tip = ImRaii.Tooltip();

        ImGui.TextUnformatted(b.Title.Length > 0 ? b.Title : $"Button {b.AzeronId}");
        ImGui.Separator();

        ImGui.TextDisabled($"Azeron button {b.AzeronId}  ({b.Type.Describe()})");

        if (b.Chord.Length > 0)
            ImGui.TextUnformatted($"Sends: {b.Chord}");

        if (b.InputIdName != null)
            ImGui.TextDisabled($"FFXIV keybind: {b.InputIdName}");

        if (b.AzeronLabel != null && b.AzeronLabel != b.Title)
            ImGui.TextUnformatted($"Your label: {b.AzeronLabel}");

        if (b.Tier == ResolutionTier.HotbarSlot)
        {
            if (!b.Usable)
                ImGui.TextDisabled("Not usable right now");
            if (b.CooldownSeconds > 0)
                ImGui.TextDisabled($"Cooldown: {b.CooldownSeconds}s");
            if (b.Charges > 1)
                ImGui.TextDisabled($"Charges: {b.Charges}");
        }

        if (b.HasLongPress || b.HasDoublePress)
        {
            var kinds = (b.HasLongPress, b.HasDoublePress) switch
            {
                (true, true) => "long-press and double-press",
                (true, false) => "long-press",
                _ => "double-press",
            };
            ImGui.TextDisabled($"Also has a {kinds} bind.");
        }

        if (b.UnbindableReason != null)
            ImGui.TextDisabled(b.UnbindableReason);
    }

    /// <summary>
    /// Whether this button's key is down right now. IKeyState reads the game's
    /// own key array, so it only reports keys the game itself saw -- which is
    /// exactly what we want to highlight.
    /// </summary>
    private static bool IsHeld(ResolvedButton b)
    {
        if (b.Vk == 0)
            return false;

        var key = (VirtualKey)b.Vk;
        return Plugin.KeyState.IsVirtualKeyValid(key) && Plugin.KeyState[key];
    }
}
