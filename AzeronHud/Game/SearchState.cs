using System;
using AzeronHud.Game;

namespace AzeronHud;

/// <summary>
/// "Where is X?" -- a live query that highlights whichever buttons cast it.
///
/// Session state, not configuration: it is a thing you do for ten seconds
/// while learning a job, not a preference worth persisting.
/// </summary>
internal sealed class SearchState
{
    private string query = string.Empty;

    internal string Query
    {
        get => query;
        set => query = value ?? string.Empty;
    }

    internal bool Active => query.Trim().Length > 0;

    internal void Clear() => query = string.Empty;

    /// <summary>
    /// Whether a button answers the query. Matches on everything the user
    /// might reasonably type: the action's name, the key it sends, their own
    /// Azeron label, and the raw FFXIV keybind id. A thumbstick matches if any
    /// of its directions does.
    /// </summary>
    internal bool Matches(ResolvedButton b)
    {
        if (!Active)
            return false;

        var needle = query.Trim();

        if (MatchesSelf(b, needle))
            return true;

        if (b.Directions == null)
            return false;

        foreach (var d in b.Directions)
            if (MatchesSelf(d, needle))
                return true;

        return false;
    }

    private static bool MatchesSelf(ResolvedButton b, string needle) =>
        Contains(b.Title, needle)
        || Contains(b.Chord, needle)
        || Contains(b.AzeronLabel, needle)
        || Contains(b.InputIdName, needle);

    private static bool Contains(string? haystack, string needle) =>
        haystack is { Length: > 0 }
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
