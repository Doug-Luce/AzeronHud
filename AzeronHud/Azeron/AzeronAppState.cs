using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AzeronHud.Azeron;

/// <summary>
/// The few things worth knowing from the Azeron app's own UI state: which
/// profile it has linked to a game, and which it used last.
///
/// This lives in Chromium's IndexedDB, a log-structured LevelDB store. We do
/// not parse it -- we scan the raw files for the JSON the app wrote and take
/// the most recent match. That is a heuristic, so every failure here is
/// treated as "no information" and the caller falls back to something simpler.
/// It is never an error.
/// </summary>
internal sealed class AzeronAppState
{
    /// <summary>Don't read an unbounded amount if the store has grown strangely.</summary>
    private const long MaxBytes = 64 * 1024 * 1024;

    private static readonly Regex LinkedGame = new(
        """"profileId":"(?<id>[0-9a-fA-F-]+)","linkedGame":"(?<game>[^"]*)","linkedPath":"(?<path>(?:[^"\\]|\\.)*)"""",
        RegexOptions.Compiled);

    private static readonly Regex LastUsed = new(
        """"lastUsedSoftwareProfile":"(?<id>[^"]*)"""",
        RegexOptions.Compiled);

    /// <summary>Profile the app switches to for this game, or null.</summary>
    internal string? ProfileLinkedToGame { get; private init; }

    /// <summary>Profile the app last had loaded, or null.</summary>
    internal string? LastUsedProfile { get; private init; }

    internal static AzeronAppState Empty => new();

    /// <summary>
    /// Read what we can. <paramref name="gameMarker"/> is matched
    /// case-insensitively against both the linked game name and its path, so
    /// "ffxiv" catches both "ffxiv_dx11" and the executable path.
    /// </summary>
    internal static AzeronAppState Read(string root, string gameMarker)
    {
        var blob = ReadStore(root);
        if (blob == null)
            return Empty;

        string? linked = null;
        foreach (Match m in LinkedGame.Matches(blob))
        {
            var game = m.Groups["game"].Value;
            var path = m.Groups["path"].Value;

            if (game.Contains(gameMarker, StringComparison.OrdinalIgnoreCase)
                || path.Contains(gameMarker, StringComparison.OrdinalIgnoreCase))
            {
                // Later records supersede earlier ones in a log-structured store.
                linked = m.Groups["id"].Value;
            }
        }

        string? lastUsed = null;
        foreach (Match m in LastUsed.Matches(blob))
        {
            var id = m.Groups["id"].Value;
            if (id.Length > 0)
                lastUsed = id;
        }

        return new AzeronAppState { ProfileLinkedToGame = linked, LastUsedProfile = lastUsed };
    }

    private static string? ReadStore(string root)
    {
        try
        {
            var indexedDb = Path.Combine(root, "IndexedDB");
            if (!Directory.Exists(indexedDb))
                return null;

            var parts = new List<string>();
            var budget = MaxBytes;

            foreach (var dir in Directory.EnumerateDirectories(indexedDb, "*.indexeddb.leveldb"))
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (Path.GetExtension(file) is not (".log" or ".ldb"))
                    continue;

                var info = new FileInfo(file);
                if (info.Length > budget)
                    continue;

                budget -= info.Length;

                // The store is binary with JSON embedded; decode loosely and
                // let the regexes find the parts that are text.
                parts.Add(File.ReadAllText(file, System.Text.Encoding.UTF8));
            }

            return parts.Count == 0 ? null : string.Concat(parts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The app may be mid-write, or the store may not exist at all.
            return null;
        }
    }
}
