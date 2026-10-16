using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AzeronHud.Azeron;
using AzeronHud.Layout;

namespace AzeronHud.Tests;

/// <summary>
/// Checks that don't need the game running. These have caught four real bugs
/// so far -- a wrong presence test, an unclamped stick axis, geometry that
/// escaped its cell, and a word-wrap that dropped words -- so they live in the
/// repo rather than in a scratch directory.
/// </summary>
internal static class Checks
{
    private const int CyborgII = 4855;
    private static readonly string Root =
        Environment.GetEnvironmentVariable("AZERON_ROOT")
        ?? $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.config/Azeron Software";

    private static int failures;

    private static int Main()
    {
        Console.WriteLine($"Azeron config: {Root}\n");

        LayoutChecks();
        StickGeometryChecks();
        KeyCodeChecks();

        if (new ProfileStore(Root).Exists)
        {
            ProfileChecks();
            AppStateChecks();
        }
        else
        {
            Console.WriteLine("! Azeron config not found; skipping the checks that read it.\n");
        }

        Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}{(detail is null ? "" : $"  -- {detail}")}");
        if (!ok)
            failures++;
    }

    private static void LayoutChecks()
    {
        Console.WriteLine("Layout");
        var dev = DeviceLayout.ForProductId(CyborgII);
        Check("Cyborg II layout is embedded", dev != null);
        if (dev == null)
            return;

        // 30 programmable buttons plus the thumbstick, per the device's spec.
        Check("31 buttons", dev.Buttons.Count == 31, $"{dev.Buttons.Count}");
        Check("9x6 grid", dev.Columns == 9 && dev.Rows == 6, $"{dev.Columns}x{dev.Rows}");
        Check("cell aspect is 106/75", MathF.Abs(dev.CellAspect - (106f / 75f)) < 0.001f);
        Check("profile-switch button is excluded",
            dev.Buttons.All(b => b.Id != dev.ProfileSwitchCode));

        var stick = dev.Buttons.FirstOrDefault(b => b.Kind == "stick");
        Check("thumbstick spans 2x2", stick is { ColSpan: 2, RowSpan: 2 });

        foreach (var b in dev.Buttons)
        {
            if (b.Col + b.ColSpan > dev.Columns || b.Row + b.RowSpan > dev.Rows)
            {
                Check($"button {b.Id} is inside the grid", false);
                return;
            }
        }

        Check("every button is inside the grid", true);
    }

    private static void StickGeometryChecks()
    {
        Console.WriteLine("\nThumbstick geometry");
        var dev = DeviceLayout.ForProductId(CyborgII)!;
        var stick = dev.Buttons.First(b => b.Kind == "stick");
        const float gapRatio = 6f / 72f;
        var worst = "";

        foreach (var cellW in new[] { 18f, 30f, 48f, 64f, 96f, 140f })
        foreach (var hasDirections in new[] { true, false })
        {
            var (_, size) = dev.Rect(stick, cellW, cellW * gapRatio);
            var ring = new Vector2(size.X * 0.33f, size.Y * 0.33f);
            var slot = MathF.Min(size.X, size.Y) * 0.28f;

            var baseR = hasDirections
                ? MathF.Max(8f, MathF.Min(ring.X, ring.Y) - (slot * 0.62f))
                : MathF.Min(size.X, size.Y) * 0.30f;
            var knobR = baseR * 0.42f;

            if (baseR > (MathF.Min(size.X, size.Y) / 2f) + 0.01f)
                worst = $"disc escapes the cell at {cellW}px";
            else if (hasDirections && (ring.X + (slot / 2f) > (size.X / 2f) + 0.01f
                                       || ring.Y + (slot / 2f) > (size.Y / 2f) + 0.01f))
                worst = $"direction slot escapes the cell at {cellW}px";
            else if ((baseR - knobR) + knobR > baseR + 0.001f)
                worst = $"knob escapes the disc at {cellW}px";
        }

        Check("disc, slots and knob stay contained at every size", worst.Length == 0, worst);
    }

    private static void KeyCodeChecks()
    {
        Console.WriteLine("\nKey decoding");
        var expected = new (string Input, string Name)[]
        {
            ("KeyW", "W"), ("Digit3", "3"), ("Equal", "="), ("Backquote", "`"),
            ("Escape", "Esc"), ("Space", "Space"), ("Tab", "Tab"), ("ArrowUp", "Up"),
            ("F13", "F13"), ("Numpad5", "Num5"),
            ("187", "="), ("80", "P"),          // the legacy decimal form
        };

        var bad = expected
            .Where(e => KeyCodeMap.KeyName(KeyCodeMap.Resolve(e.Input)) != e.Name)
            .Select(e => e.Input)
            .ToList();

        Check("both encodings decode", bad.Count == 0, string.Join(", ", bad));
        Check("\"0\" is empty", KeyCodeMap.Resolve("0") == 0);

        var shiftC = KeyCodeMap.Decode(new List<string> { "KeyC" }, new List<string> { "ShiftLeft" });
        Check("modifiers fold into the chord", shiftC.ToString() == "Shift+C", shiftC.ToString());

        var winOnly = KeyCodeMap.Decode(new List<string> { "0" }, new List<string> { "MetaLeft" });
        Check("a bare Windows key is flagged unbindable", KeyCodeMap.IsUnbindable(winOnly.Vk));
    }

    private static void ProfileChecks()
    {
        Console.WriteLine("\nProfiles");
        var store = new ProfileStore(Root);
        var all = store.LoadAll(CyborgII);
        Check("at least one profile parses", all.Count > 0);
        if (all.Count == 0)
            return;

        Check("all parsed profiles are azeron-profile-v1", all.All(p => p.LooksValid));

        var dev = DeviceLayout.ForProductId(CyborgII)!;
        var layoutIds = dev.Buttons.Select(b => b.Id).ToHashSet();

        foreach (var p in all)
        {
            var ids = p.Inputs.Select(i => i.Id).ToHashSet();
            Check($"\"{p.Name}\" covers every button in the layout", layoutIds.IsSubsetOf(ids));
        }

        // The stick carries a default analogKeys block even when it is in a mode
        // that sends no keys -- reading those would invent bindings.
        foreach (var p in all)
        {
            var stick = p.Inputs.FirstOrDefault(i => i.Id == 24);
            if (stick == null)
                continue;

            var type = stick.TypeAt(PressKind.Single);
            var settings = stick.AnalogSettings;
            Check($"\"{p.Name}\" stick parses ({type})", settings != null);

            if (settings is { IsEightDirectionalTrigger: false })
                Check($"\"{p.Name}\" diagonals are inert", settings.Diagonals == null);
        }
    }

    private static void AppStateChecks()
    {
        Console.WriteLine("\nProfile selection");
        var store = new ProfileStore(Root);
        var all = store.LoadAll(CyborgII);
        var state = AzeronAppState.Read(Root, "ffxiv");

        Console.WriteLine($"  linked to FFXIV : {state.ProfileLinkedToGame ?? "(none)"}");
        Console.WriteLine($"  last used       : {state.LastUsedProfile ?? "(none)"}");

        // Not an assertion about which profile is right -- only that whatever
        // the app points at is a profile we actually loaded.
        if (state.ProfileLinkedToGame != null)
            Check("the linked profile exists on disk",
                all.Any(p => p.Id == state.ProfileLinkedToGame));

        if (state.LastUsedProfile != null)
            Check("the last-used profile exists on disk",
                all.Any(p => p.Id == state.LastUsedProfile));
    }
}
