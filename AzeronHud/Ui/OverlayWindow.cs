using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// This frame's effective opacity: the configured value, knocked down while
    /// out of combat if the user asked for that. Computed once in PreDraw so
    /// the window background and the contents can't disagree.
    /// </summary>
    private float opacity = 1f;

    /// <summary>Scratch for the wrapped action name. Reused; drawing is single-threaded.</summary>
    private readonly List<string> titleLines = new();

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

    /// <summary>
    /// Whether a button earns a place on the HUD. "Only hotbar actions" keeps
    /// a thumbstick if any of its directions casts something, since the stick
    /// as a whole is then still worth showing.
    /// </summary>
    private bool IsVisible(ResolvedButton b)
    {
        if (cfg.OnlyHotbarActions)
        {
            if (b.Tier == ResolutionTier.HotbarSlot)
                return true;

            return b.Directions?.Exists(d => d.Tier == ResolutionTier.HotbarSlot) == true;
        }

        return b.Tier != ResolutionTier.Empty || cfg.ShowEmptyButtons;
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

        opacity = cfg.Opacity;
        if (cfg.DimOutOfCombat && !Plugin.Condition[ConditionFlag.InCombat])
            opacity *= cfg.OutOfCombatOpacity;

        IsClickthrough = cfg.Locked;
        BgAlpha = opacity * 0.5f;
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

        var visible = new List<LayoutButton>();
        int minCol = int.MaxValue, minRow = int.MaxValue, maxCol = 0, maxRow = 0;

        foreach (var lb in dev.Buttons)
        {
            if (!buttons.TryGetValue(lb.Id, out var b) || !IsVisible(b))
                continue;

            visible.Add(lb);
            minCol = Math.Min(minCol, lb.Col);
            minRow = Math.Min(minRow, lb.Row);
            maxCol = Math.Max(maxCol, lb.Col + lb.ColSpan - 1);
            maxRow = Math.Max(maxRow, lb.Row + lb.RowSpan - 1);
        }

        if (visible.Count == 0)
        {
            ImGui.TextUnformatted(cfg.OnlyHotbarActions
                ? "No button on this profile is bound to a hotbar action."
                : "Nothing to show.");
            return;
        }

        // Hiding buttons can empty whole rows and columns at the edges. Trim to
        // what's left rather than keeping a border of nothing -- interior gaps
        // stay, because the point of the HUD is where the buttons are on the
        // keypad, and closing those up would break the mapping to your hand.
        var columns = maxCol - minCol + 1;
        var rows = maxRow - minRow + 1;

        // One scalar drives every rect this frame. Scale is applied here and
        // nowhere else -- mixing it with SetWindowFontScale is what makes hover
        // rects drift away from what's drawn.
        var avail = ImGui.GetContentRegionAvail();
        var cell = cfg.ScaleToWindow
            ? dev.CellSizeToFit(avail, cfg.GapRatio, columns, rows)
            : cfg.CellSize * ImGuiHelpers.GlobalScale;

        var gap = cell * cfg.GapRatio;
        var grid = dev.GridSize(cell, gap, columns, rows);

        // Fitting preserves the grid's proportions, so one axis has slack.
        // Centre in it rather than letting the keypad sit in a corner.
        var origin = ImGui.GetCursorScreenPos() + new Vector2(
            MathF.Max(0f, (avail.X - grid.X) / 2f),
            MathF.Max(0f, (avail.Y - grid.Y) / 2f));

        var draw = ImGui.GetWindowDrawList();

        foreach (var lb in visible)
        {
            var (offset, size) = dev.Rect(lb, cell, gap, minCol, minRow);
            DrawButton(draw, buttons[lb.Id], origin + offset, size, cell);
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
            draw.AddRectFilled(pos, p1, Palette.Panel(col, b.Tier, opacity), rounding);

        // Wrap first: the icon centres in whatever space the name leaves, so it
        // has to know how many lines the name actually took.
        WrapTitle(b, size.X, cell);
        var strip = TitleStrip(cell);

        if (b.IsStick)
        {
            DrawStick(draw, b, pos, size, cell);
        }
        else if (cfg.ShowIcons && b.IconId != 0)
        {
            DrawIcon(draw, b, pos, size, opacity, col.IconUnusable, cfg.IconScale, strip);
        }

        if (cfg.ShowCooldown && b.CooldownPct > 0f)
            DrawCooldown(draw, b, pos, size, cell);

        if (b.Tier == ResolutionTier.HotbarSlot)
        {
            if (cfg.DimUnusable && !b.Usable)
                draw.AddRectFilled(pos, p1, Palette.Of(col.Unusable, opacity), rounding);

            // Out of range is its own state: the action is fine, you just can't
            // reach. Grey would read as "unusable", so this gets its own color.
            if (cfg.ShowRangeTint && !b.InRange)
                draw.AddRectFilled(pos, p1, Palette.Of(col.OutOfRange, opacity), rounding);
        }

        // A finished cooldown fades out over its flash; a proc stays lit while
        // the game says so. Different events, so they look different.
        var flash = cfg.ShowReadyFlash ? ReadyFlashStrength(b) : 0f;
        if (flash > 0f)
        {
            draw.AddRect(pos, p1, Palette.Of(col.ReadyFlash with { W = col.ReadyFlash.W * flash }, opacity),
                rounding, ImDrawFlags.None, 3.5f);
        }

        var procing = cfg.ShowProcGlow && b.Highlighted && b.Tier == ResolutionTier.HotbarSlot;
        if (procing)
        {
            // Inner wash plus a heavier rim, both inside the cell's clip rect.
            draw.AddRectFilled(pos, p1, Palette.Of(col.ProcGlow, opacity), rounding);
            draw.AddRect(pos, p1, Palette.Of(col.Proc, opacity),
                rounding, ImDrawFlags.None, 3.5f);
        }

        draw.AddRect(
            pos, p1,
            pressed
                ? Palette.Of(col.KeyHeld, opacity)
                : Palette.Border(col, b.Tier, opacity),
            rounding, ImDrawFlags.None, pressed ? 2.5f : 1.5f);

        if (!b.IsStick)
            DrawText(draw, b, pos, size, cell);

        if (cfg.ShowSecondaryPips && (b.HasLongPress || b.HasDoublePress))
            DrawPips(draw, b, pos, size, cell, opacity, col.Pip);

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

        draw.AddCircleFilled(centre, baseRadius, Palette.Of(col.StickBase, opacity), 32);
        draw.AddCircle(centre, baseRadius, Palette.Of(col.StickRim, opacity), 32, 1.5f);
        draw.AddCircleFilled(centre + offset, knobRadius,
            Palette.Of(active ? col.StickKnobActive : col.StickKnob, opacity), 24);

        if (b.Title.Length == 0)
            return;

        // Under the disc, or along the bottom of the cell when the ring is busy.
        var fontSize = LabelSize(cell, 0.17f);
        var extent = ImGui.CalcTextSize(b.Title) * (fontSize / ImGui.GetFontSize());
        var titleY = hasDirections
            ? pos.Y + size.Y - fontSize - (cell * 0.06f)
            : centre.Y + baseRadius + (cell * 0.08f);

        draw.PushClipRect(pos, pos + size, true);
        Label(draw, fontSize, new Vector2(centre.X - (extent.X / 2f), titleY),
            Palette.Of(hasDirections ? col.TextTitle : col.TextTitleDim, opacity), b.Title);
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
            draw.AddRectFilled(p0, p1, Palette.Panel(col, d.Tier, opacity), rounding);

        if (cfg.ShowIcons && d.IconId != 0)
        {
            // A direction slot has no label strip, so the same setting can fill
            // more of it before it looks cramped.
            DrawIcon(draw, d, p0, new Vector2(slot), opacity, col.IconUnusable,
                MathF.Min(0.96f, cfg.IconScale * 1.3f));
        }
        else
        {
            // Prefer what the direction does over which key it sends, but only
            // when the name actually fits -- a direction slot is small, and a
            // clipped "Move Forwa" is worse than a legible "W". Short names
            // like "Jump" or "Sprint" win; long ones fall back to the key and
            // live in the tooltip instead.
            var fontSize = LabelSize(cell, 0.15f);
            var ratio = fontSize / ImGui.GetFontSize();
            var text = d.Chord;

            if (d.Title.Length > 0 && d.Title != d.Chord
                && ImGui.CalcTextSize(d.Title).X * ratio <= slot * 0.92f)
                text = d.Title;

            if (text.Length == 0)
                text = d.Title;

            if (text.Length > 0)
            {
                var extent = ImGui.CalcTextSize(text) * ratio;
                draw.PushClipRect(p0, p1, true);
                Label(draw, fontSize, centre - (extent / 2f), Palette.Of(col.TextTitle, opacity), text);
                draw.PopClipRect();
            }
        }

        if (cfg.ShowCooldown && d.CooldownPct > 0f)
        {
            draw.AddRectFilled(p0, new Vector2(p1.X, p0.Y + (slot * d.CooldownPct)),
                Palette.Of(col.Cooldown, opacity));
        }

        if (d.Tier == ResolutionTier.HotbarSlot)
        {
            if (cfg.DimUnusable && !d.Usable)
                draw.AddRectFilled(p0, p1, Palette.Of(col.Unusable, opacity), rounding);
            if (cfg.ShowRangeTint && !d.InRange)
                draw.AddRectFilled(p0, p1, Palette.Of(col.OutOfRange, opacity), rounding);
        }

        if (cfg.ShowProcGlow && d.Highlighted && d.Tier == ResolutionTier.HotbarSlot)
        {
            draw.AddRectFilled(p0, p1, Palette.Of(col.ProcGlow, opacity), rounding);
            draw.AddRect(p0, p1, Palette.Of(col.Proc, opacity), rounding, ImDrawFlags.None, 2.5f);
        }

        draw.AddRect(p0, p1, Palette.Border(col, d.Tier, opacity), rounding, ImDrawFlags.None, 1f);
    }

    /// <summary>
    /// Draw a label, optionally over a dark outline.
    ///
    /// The HUD sits on anything from a white wall to a dark cave, and with the
    /// panels turned off there is nothing behind the text at all. Stamping the
    /// glyphs in the outline color at eight offsets first costs a few draw
    /// calls per label and makes them readable over any of it.
    /// </summary>
    private void Label(ImDrawListPtr draw, float fontSize, Vector2 pos, uint color, string text)
    {
        if (cfg.ShowTextOutline)
        {
            var o = MathF.Max(1f, fontSize * 0.09f);
            var outline = Palette.Of(cfg.Colors.TextOutline, opacity);
            var font = ImGui.GetFont();

            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                draw.AddText(font, fontSize, pos + new Vector2(dx * o, dy * o), outline, text);
            }
        }

        draw.AddText(ImGui.GetFont(), fontSize, pos, color, text);
    }

    /// <summary>How bright the cooldown-finished flash is right now, 0 when over.</summary>
    private static float ReadyFlashStrength(ResolvedButton b)
    {
        var remaining = b.ReadyFlashUntil - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return 0f;

        return MathF.Min(1f, (float)(remaining.TotalMilliseconds / 650d));
    }

    /// <summary>A label's size, as a fraction of the cell, after the user's scale.</summary>
    private float LabelSize(float cell, float fraction) =>
        cell * fraction * cfg.TextScale;

    /// <summary>
    /// Draw a slot's icon, centred in whatever space it has.
    ///
    /// <paramref name="reserveBottom"/> is the label strip at the foot of the
    /// cell: the icon centres in the space above it rather than in the cell as
    /// a whole, which keeps it optically centred instead of sitting low and
    /// colliding with the text.
    /// </summary>
    private static void DrawIcon(
        ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float opacity,
        Vector4 unusableTint, float fill, float reserveBottom = 0f)
    {
        var shared = Plugin.Textures.GetFromGameIcon(new GameIconLookup(b.IconId));
        if (!shared.TryGetWrap(out var wrap, out _))
            return;

        var area = new Vector2(size.X, MathF.Max(1f, size.Y - reserveBottom));
        var side = MathF.Min(area.X, area.Y) * fill;
        var topLeft = pos + ((area - new Vector2(side)) / 2f);
        var tint = Palette.Of(b.Usable ? Vector4.One : unusableTint, opacity);

        draw.AddImage(wrap.Handle, topLeft, topLeft + new Vector2(side, side),
            Vector2.Zero, Vector2.One, tint);
    }

    /// <summary>
    /// Height of the label strip at the foot of a cell, for however many lines
    /// the name wrapped onto. Mirrors what <see cref="DrawText"/> lays out, so
    /// the icon can centre above it.
    /// </summary>
    private float TitleStrip(float cell) =>
        titleLines.Count == 0
            ? 0f
            : (titleLines.Count * LineHeight(cell)) + (cell * 0.06f);

    private float LineHeight(float cell) => LabelSize(cell, 0.19f) * 1.05f;

    /// <summary>
    /// Break the action name into at most <see cref="Configuration.NameLines"/>
    /// lines that fit the cell. Greedy word wrap -- action names are two or
    /// three short words, so there is nothing to gain from anything cleverer.
    /// A single word too wide to fit is left long and clipped at the edge.
    /// </summary>
    private void WrapTitle(ResolvedButton b, float width, float cell)
    {
        titleLines.Clear();

        if (!cfg.ShowActionName || b.Title.Length == 0)
            return;

        var maxLines = Math.Clamp(cfg.NameLines, 1, 3);
        var ratio = LabelSize(cell, 0.19f) / ImGui.GetFontSize();
        var limit = width - (cell * 0.1f);

        if (maxLines == 1 || ImGui.CalcTextSize(b.Title).X * ratio <= limit)
        {
            titleLines.Add(b.Title);
            return;
        }

        var words = b.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = string.Empty;

        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (ImGui.CalcTextSize(candidate).X * ratio <= limit || line.Length == 0)
            {
                line = candidate;
                continue;
            }

            titleLines.Add(line);
            line = word;

            // Out of room: put everything remaining on the last line and let
            // it clip, rather than silently dropping words.
            if (titleLines.Count == maxLines - 1)
                break;
        }

        var consumed = titleLines.Sum(l => l.Split(' ').Length);
        if (titleLines.Count == maxLines - 1 && consumed < words.Length)
            line = string.Join(' ', words.Skip(consumed));

        if (line.Length > 0)
            titleLines.Add(line);
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
            Palette.Of(col.Cooldown, opacity));

        if (!cfg.ShowCooldownNumerals || b.CooldownSeconds <= 0)
            return;

        var centre = pos + (size / 2f);

        var text = b.CooldownSeconds.ToString();
        var fontSize = LabelSize(cell, 0.34f);
        var extent = ImGui.CalcTextSize(text) * (fontSize / ImGui.GetFontSize());
        Label(draw, fontSize, centre - (extent / 2f), Palette.Of(col.TextTitle, opacity), text);
    }

    private void DrawText(ImDrawListPtr draw, ResolvedButton b, Vector2 pos, Vector2 size, float cell)
    {
        var col = cfg.Colors;
        var pad = cell * 0.06f;

        if (cfg.ShowChord && b.Chord.Length > 0)
        {
            Label(draw, LabelSize(cell, 0.17f), pos + new Vector2(pad, pad),
                Palette.Of(col.TextKey, opacity), b.Chord);
        }

        if (b.Charges > 1)
        {
            var text = b.Charges.ToString();
            var fontSize = LabelSize(cell, 0.2f);
            var extent = ImGui.CalcTextSize(text) * (fontSize / ImGui.GetFontSize());
            Label(draw, fontSize, new Vector2(pos.X + size.X - extent.X - pad, pos.Y + pad),
                Palette.Of(col.TextCharges, opacity), text);
        }

        if (cfg.ShowCost && b.CostText is { Length: > 0 })
        {
            var fontSize = LabelSize(cell, 0.16f);
            var extent = ImGui.CalcTextSize(b.CostText) * (fontSize / ImGui.GetFontSize());
            Label(draw, fontSize, new Vector2(pos.X + pad, pos.Y + size.Y - extent.Y - pad),
                Palette.Of(col.TextCost, opacity), b.CostText);
        }

        if (titleLines.Count == 0)
            return;

        // The name gets the bottom strip, stacked upwards so it always sits
        // flush with the foot of the cell however many lines it took.
        var titleSize = LabelSize(cell, 0.19f);
        var lineHeight = LineHeight(cell);
        var ratio = titleSize / ImGui.GetFontSize();
        var color = Palette.Of(
            b.Tier is ResolutionTier.Empty or ResolutionTier.RawKey ? col.TextTitleDim : col.TextTitle,
            opacity);

        draw.PushClipRect(pos + new Vector2(pad * 0.5f, 0), pos + size - new Vector2(pad * 0.5f, 0), true);

        var top = pos.Y + size.Y - pad - (titleLines.Count * lineHeight);
        for (var i = 0; i < titleLines.Count; i++)
        {
            var measured = ImGui.CalcTextSize(titleLines[i]).X * ratio;
            Label(draw, titleSize,
                new Vector2(pos.X + MathF.Max(pad, (size.X - measured) / 2f), top + (i * lineHeight)),
                color, titleLines[i]);
        }

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
