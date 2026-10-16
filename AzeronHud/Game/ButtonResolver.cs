using System;
using System.Collections.Generic;
using System.Text;
using AzeronHud.Azeron;
using AzeronHud.Layout;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace AzeronHud.Game;

/// <summary>
/// Turns Azeron profile inputs into drawable buttons by asking the game what
/// each key actually does.
///
/// Split into two cadences: the structural pass (icon, name, which slot) only
/// changes on a job swap, hotbar edit or profile reload, while cooldown and
/// usability move every frame. Both are skipped entirely when the overlay is
/// hidden.
/// </summary>
internal sealed unsafe class ButtonResolver
{
    private readonly KeybindIndex keybinds;
    private readonly Dictionary<int, ResolvedButton> buttons = new();

    /// <summary>
    /// Every cell backed by a hotbar slot, buttons and stick directions alike.
    /// Flat rather than keyed by button id because a stick contributes up to
    /// eight of them.
    /// </summary>
    private readonly List<(ResolvedButton Cell, HotbarRef Ref)> hotbarBacked = new();

    internal ButtonResolver(KeybindIndex keybinds) => this.keybinds = keybinds;

    internal IReadOnlyDictionary<int, ResolvedButton> Buttons => buttons;

    /// <summary>
    /// Full rebuild: decode every Azeron bind, match it against the keybind
    /// table and look up the hotbar slot behind it.
    /// </summary>
    internal void Rebuild(AzeronProfile? profile, DeviceLayout? layout)
    {
        buttons.Clear();
        hotbarBacked.Clear();

        if (profile == null || layout == null)
            return;

        // The layout's button list is the authority on what the keypad has.
        // The profile's own pinOne field is not: it marks absent slots 255 but
        // still lists buttons the configurator hides, like the profile-switch
        // key, which would otherwise show up as phantom cells.
        var byId = new Dictionary<int, AzeronInput>();
        foreach (var input in profile.Inputs)
            byId[input.Id] = input;

        foreach (var lb in layout.Buttons)
        {
            if (!byId.TryGetValue(lb.Id, out var input))
                continue;

            var resolved = Resolve(input);
            if (lb.Kind == "stick")
                ResolveStick(resolved, input);

            buttons[lb.Id] = resolved;
        }

        RefreshVolatile();
    }

    private ResolvedButton Resolve(AzeronInput input)
    {
        var type = input.TypeAt(PressKind.Single);
        var label = string.IsNullOrWhiteSpace(input.Label) ? null : input.Label!.Trim();

        var b = new ResolvedButton
        {
            AzeronId = input.Id,
            Type = type,
            AzeronLabel = label,
            HasLongPress = !input.TypeAt(PressKind.Long).IsEmpty(),
            HasDoublePress = !input.TypeAt(PressKind.Double).IsEmpty(),
        };

        if (type.IsEmpty())
        {
            // Unassigned slots keep stale keyValues from a previous bind, so the
            // type is the only trustworthy signal that nothing is set.
            b.Tier = ResolutionTier.Empty;
            b.Title = label ?? string.Empty;
            return b;
        }

        if (!type.IsKeyboard())
        {
            b.Tier = ResolutionTier.Unbindable;
            b.Title = label ?? type.Describe();
            b.UnbindableReason =
                $"This button sends {type.Describe().ToLowerInvariant()} input, not a keystroke, "
                + "so FFXIV can't report what it does.";
            return b;
        }

        var (keys, mods) = input.ValuesAt(PressKind.Single);
        var chord = KeyCodeMap.Decode(keys, mods);
        b.Chord = chord.ToString();
        b.Vk = chord.Vk;

        if (chord.IsEmpty)
        {
            // A modifier with no base key: the keypad holds Shift, say, but
            // FFXIV has no binding for a bare modifier.
            b.Tier = chord.Modifiers == Mods.None ? ResolutionTier.Empty : ResolutionTier.Unbindable;
            b.Title = label ?? b.Chord;
            if (b.Tier == ResolutionTier.Unbindable)
                b.UnbindableReason = "A modifier on its own can't be bound to anything in FFXIV.";
            return b;
        }

        ResolveChord(b, chord, label);
        return b;
    }

    /// <summary>
    /// The shared tail of the resolution chain: keystroke -> FFXIV keybind ->
    /// hotbar slot. Used by both buttons and thumbstick directions.
    /// </summary>
    private void ResolveChord(ResolvedButton b, KeyChord chord, string? label)
    {
        if (KeyCodeMap.IsUnbindable(chord.Vk))
        {
            b.Tier = ResolutionTier.Unbindable;
            b.Title = label ?? b.Chord;
            b.UnbindableReason = "FFXIV can't bind the Windows key.";
            return;
        }

        if (!keybinds.TryResolve(chord, out var id))
        {
            b.Tier = ResolutionTier.RawKey;
            b.Title = label ?? b.Chord;
            return;
        }

        b.InputIdName = id.ToString();

        if (keybinds.TryHotbar(id, out var href))
        {
            // Watch the slot either way. An empty slot can be filled later --
            // by a job change, or by dragging an action onto the bar -- and if
            // we stopped watching it we would never notice.
            hotbarBacked.Add((b, href));

            b.Tier = ResolutionTier.HotbarSlot;
            if (FillFromHotbar(b, href))
                return;

            // Bound to a slot that is empty right now.
        }

        b.Tier = ResolutionTier.Keybind;
        b.Title = keybinds.NameOf(id);
    }

    /// <summary>
    /// Work out what the thumbstick does.
    ///
    /// Two quite different cases. If it emulates keys, each direction is an
    /// ordinary bind and resolves through the same chain as a button. If it is
    /// an XInput stick the game sees a gamepad axis, which isn't in the keybind
    /// table at all -- FFXIV wires the left stick to movement and the right to
    /// the camera in its own pad settings, so the honest thing is to say which
    /// stick it is rather than invent a binding.
    /// </summary>
    private void ResolveStick(ResolvedButton b, AzeronInput input)
    {
        b.IsStick = true;

        var settings = input.AnalogSettings;
        var type = input.TypeAt(PressKind.Single);
        var isRight = settings?.IsRightAnalog == true;
        var side = isRight ? "Right" : "Left";
        b.IsRightStick = isRight;

        if (type.IsXInputStick())
        {
            b.Tier = ResolutionTier.Unbindable;
            b.Title = b.AzeronLabel ?? $"{side} stick";
            b.UnbindableReason =
                $"Set to XInput, so FFXIV sees it as a gamepad's {side.ToLowerInvariant()} stick. "
                + "What it does is set in the game's own Gamepad settings, not its keybinds.";
        }

        if (!type.EmulatesKeys() || settings == null)
            return;

        var cardinals = settings.Cardinals;
        var diagonals = settings.Diagonals;
        if (cardinals == null && diagonals == null)
            return;

        var directions = new List<ResolvedButton>();

        void Add(StickDirection dir, List<string>? keys)
        {
            if (keys == null)
                return;

            // Directions have no separate modifier array; Decode already folds
            // any modifier it finds among the keys into the chord.
            var chord = KeyCodeMap.Decode(keys, Array.Empty<string>());
            if (chord.IsEmpty)
                return;

            var d = new ResolvedButton
            {
                AzeronId = input.Id,
                Direction = dir,
                Type = type,
                Chord = chord.ToString(),
                Vk = chord.Vk,
            };

            ResolveChord(d, chord, null);
            directions.Add(d);
        }

        Add(StickDirection.Up, cardinals?.Up);
        Add(StickDirection.Right, cardinals?.Right);
        Add(StickDirection.Down, cardinals?.Down);
        Add(StickDirection.Left, cardinals?.Left);
        Add(StickDirection.UpRight, diagonals?.UpRight);
        Add(StickDirection.DownRight, diagonals?.DownRight);
        Add(StickDirection.DownLeft, diagonals?.DownLeft);
        Add(StickDirection.UpLeft, diagonals?.UpLeft);

        if (directions.Count == 0)
            return;

        b.Directions = directions;
        b.Tier = ResolutionTier.Keybind;
        b.Title = b.AzeronLabel ?? $"{side} stick";
        b.UnbindableReason = null;
    }

    /// <summary>Read a hotbar slot's icon and name. False when the slot is empty.</summary>
    private static bool FillFromHotbar(ResolvedButton b, HotbarRef href)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null)
            return false;

        var slot = module->GetSlotById((uint)href.Bar, (uint)href.Slot);
        if (slot == null || slot->IsEmpty)
            return false;

        var type = slot->ApparentSlotType;
        var action = slot->ApparentActionId;

        // Let the game resolve these -- it already handles combos, upgrades,
        // items, macros, emotes and gear sets uniformly.
        var icon = slot->GetIconIdForSlot(type, action);
        b.IconId = icon > 0 ? (uint)icon : 0u;

        var name = slot->GetDisplayNameForSlot(type, action);
        if (name.HasValue)
        {
            var text = name.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                b.Title = text;
        }

        return true;
    }

    /// <summary>
    /// Per-frame refresh of the things that actually move. Only touches buttons
    /// that resolved to a hotbar slot, so this is a handful of calls.
    /// </summary>
    internal void RefreshVolatile()
    {
        if (hotbarBacked.Count == 0)
            return;

        var module = RaptureHotbarModule.Instance();
        if (module == null)
            return;

        foreach (var (cell, href) in hotbarBacked)
        {
            var slot = module->GetSlotById((uint)href.Bar, (uint)href.Slot);
            if (slot == null || slot->IsEmpty)
            {
                cell.Usable = false;
                cell.CooldownPct = 0;
                cell.CooldownSeconds = 0;
                continue;
            }

            var type = slot->ApparentSlotType;
            var action = slot->ApparentActionId;

            cell.Usable = slot->IsSlotUsable(type, action);
            cell.Highlighted = slot->IsActionHighlighted(type, action);
            cell.InRange = slot->IsSlotActionTargetInRange();

            var seconds = 0;
            var pct = slot->GetSlotActionCooldownPercentage(&seconds);
            cell.CooldownPct = Math.Clamp(pct / 100f, 0f, 1f);
            cell.CooldownSeconds = seconds;
            cell.Charges = slot->GetApparentIconRecastCharges();

            // CostText is a fixed byte buffer on the slot. Decoding it every
            // frame for every cell would be pure garbage; the numeric CostValue
            // moves with it, so use that as the change detector.
            if (cell.LastCostValue != slot->CostValue)
            {
                cell.LastCostValue = slot->CostValue;
                cell.CostText = ReadCostText(slot->CostText);
            }
        }
    }

    /// <summary>Null-terminated UTF-8 out of the slot's fixed buffer.</summary>
    private static string? ReadCostText(Span<byte> raw)
    {
        var end = raw.IndexOf((byte)0);
        if (end == 0)
            return null;

        var text = Encoding.UTF8.GetString(end < 0 ? raw : raw[..end]).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Cheap check for whether the structural pass needs redoing: the icon or
    /// name behind any slot changed (job swap, hotbar page, action upgrade).
    /// </summary>
    internal bool StructureLooksStale()
    {
        if (hotbarBacked.Count == 0)
            return false;

        var module = RaptureHotbarModule.Instance();
        if (module == null)
            return false;

        foreach (var (cell, href) in hotbarBacked)
        {
            var slot = module->GetSlotById((uint)href.Bar, (uint)href.Slot);
            if (slot == null)
                continue;

            // Compare unconditionally, including against zero: a slot emptying
            // matters as much as one being filled, and an earlier version that
            // skipped icon == 0 could never see a bar being cleared.
            var icon = slot->IsEmpty
                ? 0u
                : (uint)slot->GetIconIdForSlot(slot->ApparentSlotType, slot->ApparentActionId);

            if (icon != cell.IconId)
                return true;
        }

        return false;
    }
}
