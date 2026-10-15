using System;
using System.Collections.Generic;
using AzeronHud.Azeron;
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
    private readonly Dictionary<int, HotbarRef> hotbarByButton = new();

    internal ButtonResolver(KeybindIndex keybinds) => this.keybinds = keybinds;

    internal IReadOnlyDictionary<int, ResolvedButton> Buttons => buttons;

    /// <summary>
    /// Full rebuild: decode every Azeron bind, match it against the keybind
    /// table and look up the hotbar slot behind it.
    /// </summary>
    internal void Rebuild(AzeronProfile? profile)
    {
        buttons.Clear();
        hotbarByButton.Clear();

        if (profile == null)
            return;

        foreach (var input in profile.Inputs)
        {
            if (!input.IsPresent)
                continue;

            buttons[input.Id] = Resolve(input);
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

        if (KeyCodeMap.IsUnbindable(chord.Vk))
        {
            b.Tier = ResolutionTier.Unbindable;
            b.Title = label ?? b.Chord;
            b.UnbindableReason = "FFXIV can't bind the Windows key.";
            return b;
        }

        if (!keybinds.TryResolve(chord, out var id))
        {
            b.Tier = ResolutionTier.RawKey;
            b.Title = label ?? b.Chord;
            return b;
        }

        b.InputIdName = id.ToString();

        if (keybinds.TryHotbar(id, out var href))
        {
            hotbarByButton[input.Id] = href;
            b.Tier = ResolutionTier.HotbarSlot;
            if (!FillFromHotbar(b, href))
            {
                // Bound to a slot, but the slot is empty on this job.
                hotbarByButton.Remove(input.Id);
                b.Tier = ResolutionTier.Keybind;
                b.Title = keybinds.NameOf(id);
            }

            return b;
        }

        b.Tier = ResolutionTier.Keybind;
        b.Title = keybinds.NameOf(id);
        return b;
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
        if (hotbarByButton.Count == 0)
            return;

        var module = RaptureHotbarModule.Instance();
        if (module == null)
            return;

        foreach (var (buttonId, href) in hotbarByButton)
        {
            if (!buttons.TryGetValue(buttonId, out var b))
                continue;

            var slot = module->GetSlotById((uint)href.Bar, (uint)href.Slot);
            if (slot == null || slot->IsEmpty)
            {
                b.Usable = false;
                b.CooldownPct = 0;
                b.CooldownSeconds = 0;
                continue;
            }

            var type = slot->ApparentSlotType;
            var action = slot->ApparentActionId;

            b.Usable = slot->IsSlotUsable(type, action);

            var seconds = 0;
            var pct = slot->GetSlotActionCooldownPercentage(&seconds);
            b.CooldownPct = Math.Clamp(pct / 100f, 0f, 1f);
            b.CooldownSeconds = seconds;
            b.Charges = slot->GetApparentIconRecastCharges();
        }
    }

    /// <summary>
    /// Cheap check for whether the structural pass needs redoing: the icon or
    /// name behind any slot changed (job swap, hotbar page, action upgrade).
    /// </summary>
    internal bool StructureLooksStale()
    {
        if (hotbarByButton.Count == 0)
            return false;

        var module = RaptureHotbarModule.Instance();
        if (module == null)
            return false;

        foreach (var (buttonId, href) in hotbarByButton)
        {
            if (!buttons.TryGetValue(buttonId, out var b))
                continue;

            var slot = module->GetSlotById((uint)href.Bar, (uint)href.Slot);
            if (slot == null)
                continue;

            var icon = slot->GetIconIdForSlot(slot->ApparentSlotType, slot->ApparentActionId);
            if (icon > 0 && (uint)icon != b.IconId)
                return true;
        }

        return false;
    }
}
