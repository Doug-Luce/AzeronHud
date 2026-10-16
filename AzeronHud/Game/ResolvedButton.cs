using System;
using System.Collections.Generic;
using AzeronHud.Azeron;

namespace AzeronHud.Game;

/// <summary>Whether an action shares the global cooldown.</summary>
internal enum ActionKind
{
    /// <summary>Not an action, or not one we can classify.</summary>
    Unknown,

    /// <summary>A weaponskill or spell: it runs on the GCD.</summary>
    Gcd,

    /// <summary>An ability: it has its own recast and is used between GCDs.</summary>
    OffGcd,
}

/// <summary>How much we managed to learn about a button, best first.</summary>
internal enum ResolutionTier
{
    /// <summary>Nothing assigned in the Azeron profile.</summary>
    Empty,

    /// <summary>Assigned, but to something FFXIV can't see (mouse, XInput, macro, wheel...).</summary>
    Unbindable,

    /// <summary>A keystroke FFXIV has no binding for. Shows the Azeron label or the raw key.</summary>
    RawKey,

    /// <summary>Bound to a named FFXIV action that isn't a hotbar slot.</summary>
    Keybind,

    /// <summary>Bound to a hotbar slot: real icon, name, cooldown.</summary>
    HotbarSlot,
}

/// <summary>Everything the overlay needs to draw one button.</summary>
internal sealed class ResolvedButton
{
    internal int AzeronId;
    internal ResolutionTier Tier;

    /// <summary>Main line: action name, keybind name, Azeron label, or the chord.</summary>
    internal string Title = string.Empty;

    /// <summary>Small line: the key this button sends.</summary>
    internal string Chord = string.Empty;

    /// <summary>Virtual-key code this button sends, 0 for none. Used for the held-key highlight.</summary>
    internal byte Vk;

    /// <summary>The user's own name for the button in the Azeron app, if they set one.</summary>
    internal string? AzeronLabel;

    /// <summary>Game icon id, 0 for none.</summary>
    internal uint IconId;

    internal bool Usable = true;
    internal float CooldownPct;
    internal int CooldownSeconds;
    internal uint Charges;

    /// <summary>The game is highlighting this action -- a proc is up.</summary>
    internal bool Highlighted;

    /// <summary>False when the target is out of range, which is distinct from unusable.</summary>
    internal bool InRange = true;

    /// <summary>MP or item count the game shows on the slot, if any.</summary>
    internal string? CostText;

    /// <summary>Change detector so the cost string is only decoded when it moves.</summary>
    internal uint LastCostValue = uint.MaxValue;

    /// <summary>Longest remaining time seen during the cooldown now running.</summary>
    internal int PeakCooldownSeconds;

    /// <summary>Flash until this moment, set when a long cooldown finishes.</summary>
    internal DateTime ReadyFlashUntil = DateTime.MinValue;

    /// <summary>Set when the button also has a long-press or double-press bind.</summary>
    internal bool HasLongPress;
    internal bool HasDoublePress;

    /// <summary>Why this button can't resolve, for the tooltip.</summary>
    internal string? UnbindableReason;

    /// <summary>The FFXIV keybind id this matched, for the dump command.</summary>
    internal string? InputIdName;

    internal InputType Type;

    /// <summary>Whether this is on the GCD, so the pad can be read as rhythm.</summary>
    internal ActionKind Kind;

    /// <summary>Set on a thumbstick cell, so the overlay draws it as a joystick.</summary>
    internal bool IsStick;

    /// <summary>Which physical stick this stands in for, when it's an XInput one.</summary>
    internal bool IsRightStick;

    /// <summary>Which compass point this is, when it's one of a stick's directions.</summary>
    internal StickDirection Direction = StickDirection.None;

    /// <summary>
    /// A thumbstick's resolved directions. Each one goes through the same
    /// chain as a button, so a stick direction bound to a hotbar slot gets the
    /// real icon, name and cooldown too.
    /// </summary>
    internal List<ResolvedButton>? Directions;
}
