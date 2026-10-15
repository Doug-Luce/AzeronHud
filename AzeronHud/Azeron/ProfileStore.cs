using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace AzeronHud.Azeron;

/// <summary>
/// Finds and loads Azeron Software profiles off disk.
///
/// The plugin runs inside the game under Wine, where the prefix maps Z: to the
/// Linux root -- so the Azeron config is reachable with ordinary file IO at
/// Z:\home\you\.config\Azeron Software\... No IPC, no helper daemon.
///
/// The configurator rewrites these files in place with no lock file, so a read
/// can land mid-write. Reads retry briefly rather than surfacing a parse error.
/// </summary>
internal sealed class ProfileStore
{
    private const string DefaultRoot = @"Z:\home\{0}\.config\Azeron Software";
    private const string DeviceDir = "Storage/DevicesStorage";
    private const string ProfileDir = "ProfileStorage";

    private readonly string root;
    private DateTime lastScan = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> seenMtimes = new(StringComparer.Ordinal);

    internal ProfileStore(string? configuredRoot)
    {
        root = string.IsNullOrWhiteSpace(configuredRoot) ? GuessRoot() : configuredRoot;
    }

    internal string Root => root;

    /// <summary>The default location, derived from the Wine user name.</summary>
    internal static string GuessRoot()
    {
        // Under Wine, USERNAME matches the Linux user, so the Z: path lines up.
        var user = Environment.GetEnvironmentVariable("USERNAME")
                   ?? Environment.UserName;
        return string.Format(DefaultRoot, user);
    }

    internal bool Exists => Directory.Exists(root);

    /// <summary>Product-ID directories under DevicesStorage, e.g. "4855" for a Cyborg II.</summary>
    internal IEnumerable<int> Devices()
    {
        var dir = Path.Combine(root, DeviceDir);
        if (!Directory.Exists(dir))
            yield break;

        foreach (var d in Directory.EnumerateDirectories(dir))
            if (int.TryParse(Path.GetFileName(d), out var pid))
                yield return pid;
    }

    internal IReadOnlyList<string> ProfilePaths(int productId)
    {
        var dir = Path.Combine(root, DeviceDir, productId.ToString(), ProfileDir);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        return Directory.GetFiles(dir, "profile_*.json");
    }

    /// <summary>
    /// True when any profile file for this device has changed since the last
    /// call. Cheap enough to poll; the directory holds a handful of files.
    /// </summary>
    internal bool HasChanged(int productId, TimeSpan minInterval)
    {
        var now = DateTime.UtcNow;
        if (now - lastScan < minInterval)
            return false;
        lastScan = now;

        var changed = false;
        var paths = ProfilePaths(productId);

        foreach (var p in paths)
        {
            DateTime mtime;
            try { mtime = File.GetLastWriteTimeUtc(p); }
            catch (IOException) { continue; }

            if (!seenMtimes.TryGetValue(p, out var prev) || prev != mtime)
            {
                seenMtimes[p] = mtime;
                changed = true;
            }
        }

        // A profile being deleted counts too.
        if (seenMtimes.Count > paths.Count)
        {
            foreach (var gone in seenMtimes.Keys.Except(paths).ToList())
                seenMtimes.Remove(gone);
            changed = true;
        }

        return changed;
    }

    internal IReadOnlyList<AzeronProfile> LoadAll(int productId)
    {
        var profiles = new List<AzeronProfile>();

        foreach (var path in ProfilePaths(productId))
        {
            var p = Load(path);
            if (p != null)
                profiles.Add(p);
        }

        // Most recently saved first -- a decent default for "the one in use",
        // and far simpler than decoding the app's Chromium IndexedDB state.
        profiles.Sort((a, b) => Mtime(b.SourcePath).CompareTo(Mtime(a.SourcePath)));
        return profiles;
    }

    internal static AzeronProfile? Load(string path)
    {
        // The configurator writes without a lock; a read landing mid-write gives
        // truncated JSON. Retry a couple of times before giving up on this pass.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var json = File.ReadAllText(path);
                var profile = JsonConvert.DeserializeObject<AzeronProfile>(json);
                if (profile is { LooksValid: true })
                {
                    profile.SourcePath = path;
                    return profile;
                }

                // Parsed but not a shape we understand -- retrying won't help.
                return null;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Thread.Sleep(30);
            }
        }

        return null;
    }

    private static DateTime Mtime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (IOException) { return DateTime.MinValue; }
    }
}
