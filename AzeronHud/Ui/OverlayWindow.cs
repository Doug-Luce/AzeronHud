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
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.AlwaysAutoResize;

        if (cfg.Locked)
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;

        AllowClickthrough = cfg.Locked;
        BgAlpha = cfg.BackgroundAlpha * 0.5f;
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
        var cell = cfg.CellSize * ImGuiHelpers.GlobalScale;
        var gap = cell * cfg.GapRatio;
        var origin = ImGui.GetCursorScreenPos();
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

        // AlwaysAutoResize needs something to measure.
        ImGui.Dummy(dev.GridSize(cell, gap));
    }

    private void DrawButton(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        var p1 = pos + size;
        var rounding = cfg.Rounding * ImGuiHelpers.GlobalScale;
        var pressed = cfg.HighlightPressed && IsHeld(b);

        draw.AddRectFilled(pos, p1, Palette.Background(b.Tier, cfg.BackgroundAlpha), rounding);

        if (cfg.ShowIcons && b.IconId != 0)
            DrawIcon(draw, b, pos, size);

        if (cfg.ShowCooldown && b.CooldownPct > 0f)
            DrawCooldown(draw, b, pos, size, cell);

        if (cfg.DimUnusable && !b.Usable && b.Tier == ResolutionTier.HotbarSlot)
            draw.AddRectFilled(pos, p1, Palette.UnusableVeil, rounding);

        draw.AddRect(
            pos, p1,
            pressed ? Palette.Pressed : Palette.Border(b.Tier, cfg.BackgroundAlpha),
            rounding, ImDrawFlags.None, pressed ? 2.5f : 1.5f);

        DrawText(draw, b, pos, size, cell);

        if (cfg.ShowSecondaryPips && (b.HasLongPress || b.HasDoublePress))
            DrawPips(draw, b, pos, size, cell);

        // Hit-testing goes through ImGui so the hover region is, by
        // construction, the rect we just drew.
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton($"##azbtn{b.AzeronId}", size);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            DrawTooltip(b);
    }

    private static void DrawIcon(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size)
    {
        var shared = Plugin.Textures.GetFromGameIcon(new GameIconLookup(b.IconId));
        if (!shared.TryGetWrap(out var wrap, out _))
            return;

        // Square icon, centred in the upper part of the cell so the label has room.
        var side = MathF.Min(size.X, size.Y) * 0.56f;
        var topLeft = new Vector2(pos.X + ((size.X - side) / 2f), pos.Y + (size.Y * 0.06f));
        var tint = b.Usable
            ? 0xFFFFFFFFu
            : ImGui.ColorConvertFloat4ToU32(Palette.IconTintUnusable);

        draw.AddImage(wrap.Handle, topLeft, topLeft + new Vector2(side, side),
            Vector2.Zero, Vector2.One, tint);
    }

    /// <summary>Dark sweep from 12 o'clock, the way a hotbar reads at a glance.</summary>
    private void DrawCooldown(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        const int Segments = 28;

        var centre = pos + (size / 2f);
        var radius = size.Length();          // overshoot so the wedge covers the corners
        var sweep = b.CooldownPct * MathF.PI * 2f;

        draw.PathLineTo(centre);
        for (var i = 0; i <= Segments; i++)
        {
            var a = (-MathF.PI / 2f) + (sweep * i / Segments);
            draw.PathLineTo(centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
        }

        draw.PathFillConvex(Palette.CooldownVeil);

        if (!cfg.ShowCooldownNumerals || b.CooldownSeconds <= 0)
            return;

        var text = b.CooldownSeconds.ToString();
        var scale = cell / ImGui.GetFontSize() * 0.34f;
        var extent = ImGui.CalcTextSize(text) * scale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
            centre - (extent / 2f), Palette.Title, text);
    }

    private void DrawText(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        var pad = cell * 0.06f;

        if (cfg.ShowChord && b.Chord.Length > 0)
        {
            var scale = cell / ImGui.GetFontSize() * 0.17f;
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                pos + new Vector2(pad, pad), Palette.Chord, b.Chord);
        }

        if (b.Charges > 1)
        {
            var text = b.Charges.ToString();
            var scale = cell / ImGui.GetFontSize() * 0.2f;
            var extent = ImGui.CalcTextSize(text) * scale;
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                new Vector2(pos.X + size.X - extent.X - pad, pos.Y + pad),
                Palette.Charges, text);
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
            b.Tier is ResolutionTier.Empty or ResolutionTier.RawKey ? Palette.TitleDim : Palette.Title,
            b.Title);
        draw.PopClipRect();
    }

    /// <summary>One dot for a long-press bind, two for a double-press.</summary>
    private static void DrawPips(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        var r = cell * 0.035f;
        var y = pos.Y + size.Y - (r * 2.2f);
        var x = pos.X + size.X - (r * 2.2f);

        if (b.HasLongPress)
        {
            draw.AddCircleFilled(new Vector2(x, y), r, Palette.Pip);
            x -= r * 2.6f;
        }

        if (b.HasDoublePress)
        {
            draw.AddCircleFilled(new Vector2(x, y), r, Palette.Pip);
            draw.AddCircleFilled(new Vector2(x - (r * 2.6f), y), r, Palette.Pip);
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
