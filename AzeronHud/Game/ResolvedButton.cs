using AzeronHud.Azeron;

namespace AzeronHud.Game;

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

    /// <summary>Set when the button also has a long-press or double-press bind.</summary>
    internal bool HasLongPress;
    internal bool HasDoublePress;

    /// <summary>Why this button can't resolve, for the tooltip.</summary>
    internal string? UnbindableReason;

    /// <summary>The FFXIV keybind id this matched, for the dump command.</summary>
    internal string? InputIdName;

    internal InputType Type;
}
