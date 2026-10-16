using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AzeronHud.Azeron;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AzeronHud.Game;

/// <summary>A hotbar slot a keybind points at. Bar and slot are already zero-based.</summary>
internal readonly record struct HotbarRef(int Bar, int Slot);

/// <summary>
/// Reverse index of FFXIV's keybind table: key chord -> what the game does.
///
/// Built from the game outward, because that side is the smaller and
/// authoritative set -- roughly 600 input ids, each with a primary and an
/// alternate binding -- and then every Azeron button is a dictionary lookup.
/// </summary>
internal sealed unsafe class KeybindIndex
{
    private static readonly Regex HotbarId = new(@"^HOTBAR_(\d+)_([0-9AB])$", RegexOptions.Compiled);

    private readonly Dictionary<KeyChord, List<InputId>> chords = new();
    private readonly Dictionary<InputId, string> friendlyNames = new();
    private readonly Dictionary<InputId, HotbarRef> hotbarRefs = new();

    internal int BoundCount { get; private set; }
    internal int NamedCount => friendlyNames.Count;

    /// <summary>
    /// Cheap hash of the whole keybind table, for spotting a rebind made in
    /// game. Rebuilding on a timer would be wasteful and rebuilding never
    /// leaves the HUD quietly wrong, so poll this instead.
    /// </summary>
    internal ulong Signature()
    {
        var input = UIInputData.Instance();
        if (input == null)
            return 0;

        // FNV-1a over the key settings. The span is a few hundred entries of
        // two bytes each -- trivial next to anything else in a frame.
        var hash = 14695981039346656037UL;
        var span = input->GetKeybindSpan();

        unchecked
        {
            hash = (hash ^ (ulong)span.Length) * 1099511628211UL;
            foreach (ref readonly var keybind in span)
            {
                foreach (ref readonly var setting in keybind.KeySettings)
                {
                    hash = (hash ^ (byte)setting.Key) * 1099511628211UL;
                    hash = (hash ^ (byte)setting.KeyModifier) * 1099511628211UL;
                }
            }
        }

        return hash;
    }

    /// <summary>Re-read the whole keybind table. Cheap enough to run on demand, not per frame.</summary>
    internal void Rebuild()
    {
        chords.Clear();
        hotbarRefs.Clear();
        BoundCount = 0;

        var input = UIInputData.Instance();
        if (input == null)
            return;

        foreach (var id in Enum.GetValues<InputId>())
        {
            var keybind = input->GetKeybind(id);
            if (keybind == null)
                continue;

            foreach (var setting in keybind->KeySettings)
            {
                if (setting.Key == SeVirtualKey.NO_KEY)
                    continue;

                var chord = new KeyChord((byte)setting.Key, ToMods(setting.KeyModifier));
                if (!chords.TryGetValue(chord, out var list))
                    chords[chord] = list = new List<InputId>();

                list.Add(id);
                BoundCount++;
            }

            if (TryParseHotbar(id, out var href))
                hotbarRefs[id] = href;
        }
    }

    /// <summary>
    /// Load localized keybind names from the ConfigKey sheet.
    ///
    /// The sheet pairs a SCREAMING_SNAKE id with its display text, but which of
    /// its two string columns holds which is not guaranteed stable across Lumina
    /// regenerations -- so try both against the InputId name set and take
    /// whichever matches. No hardcoded table, and it survives a reshuffle.
    /// </summary>
    internal void LoadNames(Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.ConfigKey>? sheet)
    {
        friendlyNames.Clear();
        if (sheet == null)
            return;

        var byName = new Dictionary<string, InputId>(StringComparer.Ordinal);
        foreach (var id in Enum.GetValues<InputId>())
            byName[id.ToString()] = id;

        foreach (var row in sheet)
        {
            var text = row.Text.ExtractText();
            var label = row.Label.ExtractText();

            if (byName.TryGetValue(text, out var fromText))
                friendlyNames[fromText] = label;
            else if (byName.TryGetValue(label, out var fromLabel))
                friendlyNames[fromLabel] = text;
        }
    }

    /// <summary>
    /// Best input id for a chord. FFXIV only enforces uniqueness within a
    /// category, so a chord can be bound several times; prefer a hotbar slot,
    /// then anything with a user-facing name, then whatever is left.
    /// </summary>
    internal bool TryResolve(KeyChord chord, out InputId id)
    {
        id = default;
        if (chord.IsEmpty || !chords.TryGetValue(chord, out var candidates) || candidates.Count == 0)
            return false;

        foreach (var c in candidates)
            if (hotbarRefs.ContainsKey(c)) { id = c; return true; }

        foreach (var c in candidates)
            if (friendlyNames.ContainsKey(c)) { id = c; return true; }

        id = candidates[0];
        return true;
    }

    internal bool TryHotbar(InputId id, out HotbarRef href) => hotbarRefs.TryGetValue(id, out href);

    internal string NameOf(InputId id) =>
        friendlyNames.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)
            ? n
            : id.ToString();

    internal bool HasFriendlyName(InputId id) => friendlyNames.ContainsKey(id);

    private static Mods ToMods(KeyModifierFlag f)
    {
        var m = Mods.None;
        if (f.HasFlag(KeyModifierFlag.Shift)) m |= Mods.Shift;
        if (f.HasFlag(KeyModifierFlag.Ctrl)) m |= Mods.Ctrl;
        if (f.HasFlag(KeyModifierFlag.Alt)) m |= Mods.Alt;
        return m;
    }

    /// <summary>
    /// HOTBAR_&lt;bar&gt;_&lt;slot&gt; -> zero-based indices. The slot suffix runs
    /// 1-9 then 0, A, B for slots 10, 11 and 12 -- not plain hex.
    /// </summary>
    private static bool TryParseHotbar(InputId id, out HotbarRef href)
    {
        href = default;

        var m = HotbarId.Match(id.ToString());
        if (!m.Success)
            return false;

        if (!int.TryParse(m.Groups[1].Value, out var bar) || bar < 1)
            return false;

        var c = m.Groups[2].Value[0];
        var slot = c switch
        {
            >= '1' and <= '9' => c - '1',
            '0' => 9,
            'A' => 10,
            'B' => 11,
            _ => -1,
        };

        if (slot < 0)
            return false;

        href = new HotbarRef(bar - 1, slot);
        return true;
    }
}
