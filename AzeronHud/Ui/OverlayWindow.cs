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

        // Both of these add Dalamud's own titlebar button for pin/clickthrough.
        // On a borderless HUD that is an interactive widget floating over the
        // keypad with nothing to anchor it, and it takes focus when clicked.
        // The plugin exposes locking itself, so turn the built-in off.
        AllowPinning = false;
        AllowClickthrough = false;

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

        if (!cfg.ShowWindowBackground)
            Flags |= ImGuiWindowFlags.NoBackground;

        if (cfg.Locked)
        {
            // Locked means inert, not merely immovable. Without NoMouseInputs
            // the window still swallows every click and drag that lands on it,
            // which over a HUD this size means the game stops responding
            // wherever it happens to sit.
            Flags |= ImGuiWindowFlags.NoMove
                     | ImGuiWindowFlags.NoResize
                     | ImGuiWindowFlags.NoMouseInputs
                     | ImGuiWindowFlags.NoNav;
        }

        // Even unlocked it has no business pulling itself in front of other
        // windows or taking keyboard navigation away from the game.
        Flags |= ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoBringToFrontOnFocus;

        // Small enough to tuck away, but not so small the grid collapses.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(220, 160),
            MaximumSize = new Vector2(4000, 4000),
        };

        IsClickthrough = cfg.Locked;
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
        var col = cfg.Colors;
        var p1 = pos + size;
        var rounding = cfg.Rounding * ImGuiHelpers.GlobalScale;
        var pressed = cfg.HighlightPressed && IsHeld(b);

        // Everything inside a cell stays inside it -- icons, cooldown and long
        // action names all overflow otherwise.
        draw.PushClipRect(pos, p1, true);

        if (cfg.ShowButtonPanels)
            draw.AddRectFilled(pos, p1, Palette.Panel(col, b.Tier, cfg.Opacity), rounding);

        if (b.IsStick)
        {
            DrawStick(draw, b, pos, size, cell);
        }
        else if (cfg.ShowIcons && b.IconId != 0)
        {
            DrawIcon(draw, b, pos, size, cfg.Opacity, col.IconUnusable);
        }

        if (cfg.ShowCooldown && b.CooldownPct > 0f)
            DrawCooldown(draw, b, pos, size, cell);

        if (b.Tier == ResolutionTier.HotbarSlot)
        {
            if (cfg.DimUnusable && !b.Usable)
                draw.AddRectFilled(pos, p1, Palette.Of(col.Unusable, cfg.Opacity), rounding);

            // Out of range is its own state: the action is fine, you just can't
            // reach. Grey would read as "unusable", so this gets its own color.
            if (cfg.ShowRangeTint && !b.InRange)
                draw.AddRectFilled(pos, p1, Palette.Of(col.OutOfRange, cfg.Opacity), rounding);
        }

        var procing = cfg.ShowProcGlow && b.Highlighted && b.Tier == ResolutionTier.HotbarSlot;
        if (procing)
        {
            // Inner wash plus a heavier rim, both inside the cell's clip rect.
            draw.AddRectFilled(pos, p1, Palette.Of(col.ProcGlow, cfg.Opacity), rounding);
            draw.AddRect(pos, p1, Palette.Of(col.Proc, cfg.Opacity),
                rounding, ImDrawFlags.None, 3.5f);
        }

        draw.AddRect(
            pos, p1,
            pressed
                ? Palette.Of(col.KeyHeld, cfg.Opacity)
                : Palette.Border(col, b.Tier, cfg.Opacity),
            rounding, ImDrawFlags.None, pressed ? 2.5f : 1.5f);

        if (!b.IsStick)
            DrawText(draw, b, pos, size, cell);

        if (cfg.ShowSecondaryPips && (b.HasLongPress || b.HasDoublePress))
            DrawPips(draw, b, pos, size, cell, cfg.Opacity, col.Pip);

        draw.PopClipRect();

        // Hover-test against the exact rect we just drew -- same coordinates,
        // so there's no second scale path for them to drift apart on.
        //
        // Deliberately not an InvisibleButton: an item covering every cell eats
        // the drag that moves and resizes the window, which on a borderless HUD
        // leaves nothing to grab.
        if (!cfg.Locked
            && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)
            && ImGui.IsMouseHoveringRect(pos, p1))
            DrawTooltip(b);
    }

    /// <summary>
    /// The thumbstick, as a compass rose: a ring with each live direction
    /// placed at its own bearing, showing the action it triggers.
    ///
    /// The cell is a 2x2 span and taller than it is wide, so the ring is an
    /// ellipse -- a circle would waste the vertical room or overflow the sides.
    /// </summary>
    private void DrawStick(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        var col = cfg.Colors;
        var centre = pos + (size / 2f);
        var slot = MathF.Min(size.X, size.Y) * 0.28f;
        var hasDirections = b.Directions is { Count: > 0 };

        // Directions sit on an ellipse because the cell is a 2x2 span and taller
        // than it is wide; a circle would either waste the height or overflow.
        var ring = new Vector2(size.X * 0.33f, size.Y * 0.33f);

        if (hasDirections)
        {
            foreach (var d in b.Directions!)
            {
                var angle = (float)d.Direction * MathF.PI / 4f;   // clockwise from north
                var at = centre + new Vector2(MathF.Sin(angle) * ring.X, -MathF.Cos(angle) * ring.Y);
                DrawStickDirection(draw, d, at, slot, cell);
            }
        }

        // The stick itself, drawn the way the Azeron configurator draws it: a
        // filled disc with a smaller knob sitting on it. With no directions
        // around it there is room to make it the whole cell.
        var baseRadius = hasDirections
            ? MathF.Max(8f, MathF.Min(ring.X, ring.Y) - (slot * 0.62f))
            : MathF.Min(size.X, size.Y) * 0.30f;

        // Travel is whatever is left of the disc once the knob is accounted for,
        // so the knob is contained by construction rather than by a magic factor.
        var knobRadius = baseRadius * 0.42f;
        var offset = StickOffset(b) * (baseRadius - knobRadius);
        var active = offset.LengthSquared() > 0.0001f;

        draw.AddCircleFilled(centre, baseRadius, Palette.Of(col.StickBase, cfg.Opacity), 32);
        draw.AddCircle(centre, baseRadius, Palette.Of(col.StickRim, cfg.Opacity), 32, 1.5f);
        draw.AddCircleFilled(centre + offset, knobRadius,
            Palette.Of(active ? col.StickKnobActive : col.StickKnob, cfg.Opacity), 24);

        if (b.Title.Length == 0)
            return;

        // Under the disc, or along the bottom of the cell when the ring is busy.
        var scale = cell / ImGui.GetFontSize() * 0.17f;
        var fontSize = ImGui.GetFontSize() * scale;
        var extent = ImGui.CalcTextSize(b.Title) * scale;
        var titleY = hasDirections
            ? pos.Y + size.Y - fontSize - (cell * 0.06f)
            : centre.Y + baseRadius + (cell * 0.08f);

        draw.PushClipRect(pos, pos + size, true);
        draw.AddText(ImGui.GetFont(), fontSize,
            new Vector2(centre.X - (extent.X / 2f), titleY),
            Palette.Of(hasDirections ? col.TextTitle : col.TextTitleDim, cfg.Opacity),
            b.Title);
        draw.PopClipRect();
    }

    /// <summary>FFXIV reports gamepad axes raw, roughly -99..99, not as a unit vector.</summary>
    private const float GamepadAxisRange = 99f;

    /// <summary>Enough to swallow a resting stick's drift without feeling laggy.</summary>
    private const float StickDeadzone = 0.06f;

    /// <summary>
    /// Where to put the knob, as a vector of at most unit length. An XInput
    /// stick reports a real axis, so use it; a key-emulating stick has no axis
    /// to read, but the direction keys it sends are observable, so reconstruct
    /// the deflection from whichever are held.
    ///
    /// The result is always clamped. The raw axis has no documented range, and
    /// treating it as -1..1 threw the knob clean off the disc.
    /// </summary>
    private static Vector2 StickOffset(ResolvedButton b)
    {
        Vector2 v;

        if (b.Type.IsXInputStick())
        {
            var axis = b.IsRightStick ? Plugin.Gamepad.RightStick : Plugin.Gamepad.LeftStick;
            v = new Vector2(axis.X, -axis.Y) / GamepadAxisRange;
        }
        else if (b.Directions != null)
        {
            v = Vector2.Zero;
            foreach (var d in b.Directions)
            {
                if (!IsHeld(d))
                    continue;

                var angle = (float)d.Direction * MathF.PI / 4f;
                v += new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
            }
        }
        else
        {
            return Vector2.Zero;
        }

        var length = v.Length();
        if (length <= StickDeadzone || float.IsNaN(length))
            return Vector2.Zero;

        return length > 1f ? v / length : v;
    }

    private void DrawStickDirection(ImDrawListPtr draw, ResolvedButton d, Vector2 centre, float slot, float cell)
    {
        var col = cfg.Colors;
        var half = new Vector2(slot / 2f);
        var p0 = centre - half;
        var p1 = centre + half;
        var rounding = slot * 0.18f;

        if (cfg.ShowButtonPanels)
            draw.AddRectFilled(p0, p1, Palette.Panel(col, d.Tier, cfg.Opacity), rounding);

        if (cfg.ShowIcons && d.IconId != 0)
        {
            DrawIcon(draw, d, p0, new Vector2(slot), cfg.Opacity, col.IconUnusable, inset: 0.08f, fill: 0.84f);
        }
        else
        {
            // No icon to show, so the key itself is the most useful label.
            var text = d.Chord.Length > 0 ? d.Chord : d.Title;
            if (text.Length > 0)
            {
                var scale = cell / ImGui.GetFontSize() * 0.15f;
                var extent = ImGui.CalcTextSize(text) * scale;
                draw.PushClipRect(p0, p1, true);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                    centre - (extent / 2f), Palette.Of(col.TextTitle, cfg.Opacity), text);
                draw.PopClipRect();
            }
        }

        if (cfg.ShowCooldown && d.CooldownPct > 0f)
        {
            draw.AddRectFilled(p0, new Vector2(p1.X, p0.Y + (slot * d.CooldownPct)),
                Palette.Of(col.Cooldown, cfg.Opacity));
        }

        if (d.Tier == ResolutionTier.HotbarSlot)
        {
            if (cfg.DimUnusable && !d.Usable)
                draw.AddRectFilled(p0, p1, Palette.Of(col.Unusable, cfg.Opacity), rounding);
            if (cfg.ShowRangeTint && !d.InRange)
                draw.AddRectFilled(p0, p1, Palette.Of(col.OutOfRange, cfg.Opacity), rounding);
        }

        if (cfg.ShowProcGlow && d.Highlighted && d.Tier == ResolutionTier.HotbarSlot)
        {
            draw.AddRectFilled(p0, p1, Palette.Of(col.ProcGlow, cfg.Opacity), rounding);
            draw.AddRect(p0, p1, Palette.Of(col.Proc, cfg.Opacity), rounding, ImDrawFlags.None, 2.5f);
        }

        draw.AddRect(p0, p1, Palette.Border(col, d.Tier, cfg.Opacity), rounding, ImDrawFlags.None, 1f);
    }

    private static void DrawIcon(
        ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float opacity,
        Vector4 unusableTint, float inset = 0.06f, float fill = 0.56f)
    {
        var shared = Plugin.Textures.GetFromGameIcon(new GameIconLookup(b.IconId));
        if (!shared.TryGetWrap(out var wrap, out _))
            return;

        // Square icon, centred in the upper part of the cell so the label has room.
        // A stick direction has no label strip, so it fills more of its slot.
        var side = MathF.Min(size.X, size.Y) * fill;
        var topLeft = new Vector2(pos.X + ((size.X - side) / 2f), pos.Y + (size.Y * inset));
        var tint = Palette.Of(b.Usable ? Vector4.One : unusableTint, opacity);

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
        var col = cfg.Colors;
        draw.AddRectFilled(
            pos,
            new Vector2(pos.X + size.X, pos.Y + (size.Y * b.CooldownPct)),
            Palette.Of(col.Cooldown, cfg.Opacity));

        if (!cfg.ShowCooldownNumerals || b.CooldownSeconds <= 0)
            return;

        var centre = pos + (size / 2f);

        var text = b.CooldownSeconds.ToString();
        var scale = cell / ImGui.GetFontSize() * 0.34f;
        var extent = ImGui.CalcTextSize(text) * scale;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
            centre - (extent / 2f), Palette.Of(col.TextTitle, cfg.Opacity), text);
    }

    private void DrawText(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        var col = cfg.Colors;
        var pad = cell * 0.06f;

        if (cfg.ShowChord && b.Chord.Length > 0)
        {
            var scale = cell / ImGui.GetFontSize() * 0.17f;
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                pos + new Vector2(pad, pad), Palette.Of(col.TextKey, cfg.Opacity), b.Chord);
        }

        if (b.Charges > 1)
        {
            var text = b.Charges.ToString();
            var scale = cell / ImGui.GetFontSize() * 0.2f;
            var extent = ImGui.CalcTextSize(text) * scale;
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                new Vector2(pos.X + size.X - extent.X - pad, pos.Y + pad),
                Palette.Of(col.TextCharges, cfg.Opacity), text);
        }

        if (cfg.ShowCost && b.CostText is { Length: > 0 })
        {
            var scale = cell / ImGui.GetFontSize() * 0.16f;
            var extent = ImGui.CalcTextSize(b.CostText) * scale;
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                new Vector2(pos.X + pad, pos.Y + size.Y - extent.Y - pad),
                Palette.Of(col.TextCost, cfg.Opacity), b.CostText);
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
            Palette.Of(
                b.Tier is ResolutionTier.Empty or ResolutionTier.RawKey ? col.TextTitleDim : col.TextTitle,
                cfg.Opacity),
            b.Title);
        draw.PopClipRect();
    }

    /// <summary>One dot for a long-press bind, two for a double-press.</summary>
    private static void DrawPips(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell, float opacity, Vector4 pip)
    {
        var r = cell * 0.035f;
        var y = pos.Y + size.Y - (r * 2.2f);
        var x = pos.X + size.X - (r * 2.2f);

        if (b.HasLongPress)
        {
            draw.AddCircleFilled(new Vector2(x, y), r, Palette.Of(pip, opacity));
            x -= r * 2.6f;
        }

        if (b.HasDoublePress)
        {
            draw.AddCircleFilled(new Vector2(x, y), r, Palette.Of(pip, opacity));
            draw.AddCircleFilled(new Vector2(x - (r * 2.6f), y), r, Palette.Of(pip, opacity));
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
            if (b.Highlighted)
                ImGui.TextUnformatted("Ready -- the game is highlighting this");
            if (!b.Usable)
                ImGui.TextDisabled("Not usable right now");
            if (!b.InRange)
                ImGui.TextDisabled("Target out of range");
            if (b.CostText is { Length: > 0 })
                ImGui.TextDisabled($"Cost: {b.CostText}");
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

        if (b.Directions is not { Count: > 0 })
            return;

        ImGui.Separator();
        foreach (var d in b.Directions)
        {
            var name = d.Title.Length > 0 ? d.Title : d.Chord;
            ImGui.TextUnformatted($"{DirectionName(d.Direction),-10} {name}");
            if (d.Chord.Length > 0 && d.Title != d.Chord)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({d.Chord})");
            }
        }
    }

    private static string DirectionName(StickDirection d) => d switch
    {
        StickDirection.Up => "Up",
        StickDirection.UpRight => "Up-right",
        StickDirection.Right => "Right",
        StickDirection.DownRight => "Down-right",
        StickDirection.Down => "Down",
        StickDirection.DownLeft => "Down-left",
        StickDirection.Left => "Left",
        StickDirection.UpLeft => "Up-left",
        _ => string.Empty,
    };

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
