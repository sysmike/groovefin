namespace Groovefin.Core.History;

/// <summary>A track of the "On repeat" list: how often it was played in the window, and when last.</summary>
public sealed record OnRepeatEntry(string TrackId, int Plays, DateTimeOffset LastPlayed);

/// <summary>
/// Ranks the "On repeat" list (#251): the tracks played most in the last <see cref="PlayHistory.Window"/>,
/// at least <see cref="MinPlays"/> times each, ties broken by the latest play, the first <see cref="Size"/>.
/// </summary>
public static class OnRepeatRanking
{
    public const int Size = 30;

    public const int MinPlays = 2;

    public static IReadOnlyList<OnRepeatEntry> Rank(IEnumerable<PlayRecord> plays, DateTimeOffset now, TimeSpan window, int size = Size)
    {
        ArgumentNullException.ThrowIfNull(plays);
        var since = now - window;
        return plays
            .Where(p => p.PlayedAt >= since && p.PlayedAt <= now)
            .GroupBy(p => p.TrackId, StringComparer.Ordinal)
            .Select(g => new OnRepeatEntry(g.Key, g.Count(), g.Max(p => p.PlayedAt)))
            .Where(e => e.Plays >= MinPlays)
            .OrderByDescending(e => e.Plays)
            .ThenByDescending(e => e.LastPlayed)
            .ThenBy(e => e.TrackId, StringComparer.Ordinal)
            .Take(size)
            .ToList();
    }
}
