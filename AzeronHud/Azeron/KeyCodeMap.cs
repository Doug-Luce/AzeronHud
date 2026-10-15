using System;
using System.Collections.Generic;
using System.Text;

namespace AzeronHud.Azeron;

/// <summary>Modifier mask, matching FFXIV's KeyModifierFlag bit layout.</summary>
[Flags]
internal enum Mods : byte
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>A decoded Azeron bind: one key plus its modifiers.</summary>
internal readonly record struct KeyChord(byte Vk, Mods Modifiers)
{
    internal static readonly KeyChord None = new(0, Mods.None);
    internal bool IsEmpty => Vk == 0;

    public override string ToString()
    {
        if (IsEmpty)
            return Modifiers == Mods.None ? string.Empty : KeyCodeMap.ModifierPrefix(Modifiers).TrimEnd('+');

        return KeyCodeMap.ModifierPrefix(Modifiers) + KeyCodeMap.KeyName(Vk);
    }
}

/// <summary>
/// Turns the Azeron profile's key encoding into Windows virtual-key codes.
///
/// The profile is inconsistent about this: a slot holds either a W3C
/// <c>KeyboardEvent.code</c> string ("KeyW", "Digit3", "ShiftLeft") or a decimal
/// virtual-key code already in string form ("80", "187"). Older profiles and
/// some input modes use the numeric form, so both have to be accepted. "0" means
/// the slot is empty.
///
/// We deliberately produce raw VK bytes rather than named enum members:
/// FFXIV's SeVirtualKey is Windows VK codes below 128, so a plain cast at the
/// call site is both correct and immune to the enum being renamed upstream.
/// </summary>
internal static class KeyCodeMap
{
    private static readonly Dictionary<string, byte> CodeToVk = BuildCodeTable();
    private static readonly Dictionary<byte, string> VkToName = BuildNameTable();

    /// <summary>Decode one slot's key + modifier arrays into a chord.</summary>
    internal static KeyChord Decode(IReadOnlyList<string> keys, IReadOnlyList<string> mods)
    {
        var modifiers = Mods.None;
        byte vk = 0;

        // The Windows key has no equivalent in FFXIV's modifier mask. Keep it as
        // the chord's key so the button reads "Win" rather than going blank, and
        // let the resolver explain why it can't be bound.
        byte windowsKey = 0;

        foreach (var m in mods)
        {
            switch (Resolve(m))
            {
                case 0xA0 or 0xA1 or 0x10: modifiers |= Mods.Shift; break;
                case 0xA2 or 0xA3 or 0x11: modifiers |= Mods.Ctrl; break;
                case 0xA4 or 0xA5 or 0x12: modifiers |= Mods.Alt; break;
                case 0x5B or 0x5C: windowsKey = 0x5B; break;
            }
        }

        // Only the first key slot is a real binding; the rest are combo extras
        // the keypad sends together, which FFXIV has no concept of.
        foreach (var k in keys)
        {
            var resolved = Resolve(k);
            if (resolved == 0)
                continue;

            // A modifier sitting in the key array (rather than the modifier
            // array) is still a modifier, not the bound key.
            switch (resolved)
            {
                case 0xA0 or 0xA1 or 0x10: modifiers |= Mods.Shift; continue;
                case 0xA2 or 0xA3 or 0x11: modifiers |= Mods.Ctrl; continue;
                case 0xA4 or 0xA5 or 0x12: modifiers |= Mods.Alt; continue;
            }

            vk = resolved;
            break;
        }

        if (vk == 0)
            vk = windowsKey;

        return new KeyChord(vk, modifiers);
    }

    /// <summary>One slot entry -> VK byte, accepting either encoding. 0 = empty.</summary>
    internal static byte Resolve(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "0")
            return 0;

        if (byte.TryParse(value, out var numeric))
            return numeric;

        // Values above 255 can't be virtual keys; they're from another encoding
        // (media/HID usages) and we have nothing to match them against.
        if (int.TryParse(value, out _))
            return 0;

        return CodeToVk.GetValueOrDefault(value);
    }

    /// <summary>Keys FFXIV has no way to bind, whatever the user assigned them to.</summary>
    internal static bool IsUnbindable(byte vk) => vk is 0x5B or 0x5C;

    internal static string ModifierPrefix(Mods m)
    {
        if (m == Mods.None)
            return string.Empty;

        var sb = new StringBuilder();
        if (m.HasFlag(Mods.Ctrl)) sb.Append("Ctrl+");
        if (m.HasFlag(Mods.Alt)) sb.Append("Alt+");
        if (m.HasFlag(Mods.Shift)) sb.Append("Shift+");
        return sb.ToString();
    }

    internal static string KeyName(byte vk)
    {
        if (VkToName.TryGetValue(vk, out var name))
            return name;
        if (vk is >= 0x30 and <= 0x39)
            return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        return $"0x{vk:X2}";
    }

    private static Dictionary<string, byte> BuildCodeTable()
    {
        var t = new Dictionary<string, byte>(StringComparer.Ordinal);

        for (var c = 'A'; c <= 'Z'; c++)
            t[$"Key{c}"] = (byte)c;                       // KeyA = 0x41
        for (var d = 0; d <= 9; d++)
            t[$"Digit{d}"] = (byte)(0x30 + d);            // Digit0 = 0x30
        for (var d = 0; d <= 9; d++)
            t[$"Numpad{d}"] = (byte)(0x60 + d);
        for (var f = 1; f <= 24; f++)
            t[$"F{f}"] = (byte)(0x6F + f);                // F1 = 0x70

        t["Escape"] = 0x1B;
        t["Tab"] = 0x09;
        t["CapsLock"] = 0x14;
        t["Space"] = 0x20;
        t["Enter"] = 0x0D;
        t["NumpadEnter"] = 0x0D;
        t["Backspace"] = 0x08;
        t["Delete"] = 0x2E;
        t["Insert"] = 0x2D;
        t["Home"] = 0x24;
        t["End"] = 0x23;
        t["PageUp"] = 0x21;
        t["PageDown"] = 0x22;
        t["ArrowLeft"] = 0x25;
        t["ArrowUp"] = 0x26;
        t["ArrowRight"] = 0x27;
        t["ArrowDown"] = 0x28;
        t["PrintScreen"] = 0x2C;
        t["ScrollLock"] = 0x91;
        t["Pause"] = 0x13;
        t["NumLock"] = 0x90;
        t["ContextMenu"] = 0x5D;

        t["NumpadMultiply"] = 0x6A;
        t["NumpadAdd"] = 0x6B;
        t["NumpadSubtract"] = 0x6D;
        t["NumpadDecimal"] = 0x6E;
        t["NumpadDivide"] = 0x6F;

        // OEM punctuation, US layout.
        t["Semicolon"] = 0xBA;
        t["Equal"] = 0xBB;
        t["Comma"] = 0xBC;
        t["Minus"] = 0xBD;
        t["Period"] = 0xBE;
        t["Slash"] = 0xBF;
        t["Backquote"] = 0xC0;
        t["BracketLeft"] = 0xDB;
        t["Backslash"] = 0xDC;
        t["BracketRight"] = 0xDD;
        t["Quote"] = 0xDE;
        t["IntlBackslash"] = 0xE2;

        t["ShiftLeft"] = 0xA0;
        t["ShiftRight"] = 0xA1;
        t["ControlLeft"] = 0xA2;
        t["ControlRight"] = 0xA3;
        t["AltLeft"] = 0xA4;
        t["AltRight"] = 0xA5;
        t["MetaLeft"] = 0x5B;
        t["MetaRight"] = 0x5C;

        return t;
    }

    private static Dictionary<byte, string> BuildNameTable()
    {
        var t = new Dictionary<byte, string>
        {
            [0x1B] = "Esc", [0x09] = "Tab", [0x14] = "Caps", [0x20] = "Space",
            [0x0D] = "Enter", [0x08] = "Bksp", [0x2E] = "Del", [0x2D] = "Ins",
            [0x24] = "Home", [0x23] = "End", [0x21] = "PgUp", [0x22] = "PgDn",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "PrtSc", [0x91] = "ScrLk", [0x13] = "Pause", [0x90] = "NumLk",
            [0x5D] = "Menu", [0x5B] = "Win", [0x5C] = "Win",
            [0x6A] = "Num*", [0x6B] = "Num+", [0x6D] = "Num-", [0x6E] = "Num.", [0x6F] = "Num/",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".",
            [0xBF] = "/", [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]",
            [0xDE] = "'", [0xE2] = "\\",
            [0x10] = "Shift", [0xA0] = "Shift", [0xA1] = "RShift",
            [0x11] = "Ctrl", [0xA2] = "Ctrl", [0xA3] = "RCtrl",
            [0x12] = "Alt", [0xA4] = "Alt", [0xA5] = "RAlt",
        };

        for (var d = 0; d <= 9; d++)
            t[(byte)(0x60 + d)] = $"Num{d}";
        for (var f = 1; f <= 24; f++)
            t[(byte)(0x6F + f)] = $"F{f}";

        return t;
    }
}
