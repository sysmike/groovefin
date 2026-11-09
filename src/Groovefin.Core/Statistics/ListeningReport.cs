using Groovefin.Core.History;
using Groovefin.Core.Models;

namespace Groovefin.Core.Statistics;

/// <summary>A song, album or artist of a top list, and how often it was played.</summary>
public sealed record RankedId(string Id, int Plays);

/// <summary>
/// What was played in a period (#382): plays, listening time (the songs' lengths), how many songs and artists, and
/// the top songs, albums and artists, most played first, ties by ID.
/// </summary>
public sealed record ListeningTotals(
    int Plays,
    TimeSpan Time,
    int SongCount,
    int ArtistCount,
    IReadOnlyList<RankedId> TopSongs,
    IReadOnlyList<RankedId> TopAlbums,
    IReadOnlyList<RankedId> TopArtists)
{
    public static ListeningTotals Empty { get; } = new(0, TimeSpan.Zero, 0, 0, [], [], []);
}

/// <summary>The plays of one day (a week or a month) or one month (a year, all time) in the activity chart.</summary>
public sealed record ActivityBar(DateTime Start, int Plays);

/// <summary>
/// The statistics of a period (#382).
/// </summary>
/// <param name="Period">The period.</param>
/// <param name="Totals">What was played in it.</param>
/// <param name="Previous">
/// What was played in the period before, for the comparison; <see langword="null"/> when the history does not cover
/// all of it, and for all time.
/// </param>
/// <param name="Activity">The plays per day or month; for all time, the months the history covers, up to 12.</param>
/// <param name="Hours">The plays per hour of the day, 24 of them.</param>
/// <param name="HistoryStart">When the history began, local: the period's dates start no earlier.</param>
public sealed record ListeningReport(
    StatisticsPeriod Period,
    ListeningTotals Totals,
    ListeningTotals? Previous,
    IReadOnlyList<ActivityBar> Activity,
    IReadOnlyList<int> Hours,
    DateTime HistoryStart)
{
    /// <summary>
    /// Gets a value indicating whether the history began after the period's start: its plays before are not known.
    /// For all time, the charts' plays start then.
    /// </summary>
    public bool IsPartial => HistoryStart > Period.Start;
}

/// <summary>Works out the statistics of a period from the play history, or of all time from the server's play counts (#382).</summary>
public static class ListeningReports
{
    /// <summary>The longest top lists.</summary>
    public const int TopSize = 50;

    /// <summary>The months the all-time activity chart shows at most: the history keeps a year.</summary>
    public const int AllTimeMonths = 12;

    /// <summary>
    /// The statistics of <paramref name="period"/> (not all time) from <paramref name="log"/>: the plays since the
    /// history began, without the first sync's (<see cref="PlaySource.Seeded"/>), in <paramref name="zone"/>'s
    /// local time.
    /// </summary>
    public static ListeningReport ForPeriod(PlayLog log, StatisticsPeriod period, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(zone);
        if (period.IsAllTime)
        {
            throw new ArgumentException("All time comes from the server's play counts: use ForAllTime.", nameof(period));
        }

        var plays = Counted(log, zone);
        var inPeriod = plays.Where(p => period.Contains(p.Local)).ToList();
        var historyStart = Local(log.Started, zone);
        var previous = period.Previous;
        return new ListeningReport(
            period,
            Totals(inPeriod, log.Tracks),
            historyStart <= previous.Start ? Totals([.. plays.Where(p => previous.Contains(p.Local))], log.Tracks) : null,
            Activity(inPeriod, period),
            Hours(inPeriod),
            historyStart);
    }

    /// <summary>
    /// The statistics of all time: the totals and top lists from the server's play counts of <paramref name="played"/>
    /// (its played songs), the charts from <paramref name="log"/>'s plays of the last <see cref="AllTimeMonths"/>
    /// months up to <paramref name="today"/>.
    /// </summary>
    public static ListeningReport ForAllTime(IReadOnlyList<Track> played, PlayLog log, TimeZoneInfo zone, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(played);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(zone);
        var songs = played
            .Where(t => t.PlayCount > 0)
            .DistinctBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => new SongPlays(t.Id, PlayedTrack.Of(t), t.PlayCount))
            .ToList();
        var historyStart = Local(log.Started, zone);
        var thisMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var firstMonth = new DateTime(historyStart.Year, historyStart.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var from = firstMonth > thisMonth.AddMonths(1 - AllTimeMonths) ? firstMonth : thisMonth.AddMonths(1 - AllTimeMonths);
        from = from > thisMonth ? thisMonth : from;
        var charted = Counted(log, zone).Where(p => p.Local >= from).ToList();
        List<ActivityBar> activity = [];
        for (var month = from; month <= thisMonth; month = month.AddMonths(1))
        {
            var next = month.AddMonths(1);
            activity.Add(new ActivityBar(month, charted.Count(p => p.Local >= month && p.Local < next)));
        }

        return new ListeningReport(StatisticsPeriod.AllTime, Totals(songs), null, activity, Hours(charted), historyStart);
    }

    private static DateTime Local(DateTimeOffset time, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(time, zone).DateTime;

    private static List<LocalPlay> Counted(PlayLog log, TimeZoneInfo zone) =>
    [
        .. log.Plays
            .Where(p => p.Source != PlaySource.Seeded && p.PlayedAt >= log.Started)
            .Select(p => new LocalPlay(p.TrackId, Local(p.PlayedAt, zone))),
    ];

    private static ListeningTotals Totals(IReadOnlyList<LocalPlay> plays, IReadOnlyDictionary<string, PlayedTrack> tracks) =>
        Totals(
        [
            .. plays
                .GroupBy(p => p.TrackId, StringComparer.Ordinal)
                .Select(g => new SongPlays(g.Key, tracks.GetValueOrDefault(g.Key), g.Count())),
        ]);

    private static ListeningTotals Totals(IReadOnlyList<SongPlays> songs)
    {
        if (songs.Count == 0)
        {
            return ListeningTotals.Empty;
        }

        var time = TimeSpan.Zero;
        foreach (var song in songs)
        {
            time += (song.Track?.Duration ?? TimeSpan.Zero) * song.Plays;
        }

        var albums = songs
            .Where(s => s.Track?.AlbumId is not null)
            .Select(s => (Id: s.Track!.AlbumId!, s.Plays));
        var artists = songs
            .SelectMany(s => (s.Track?.ArtistIds ?? []).Select(id => (Id: id, s.Plays)))
            .ToList();
        return new ListeningTotals(
            songs.Sum(s => s.Plays),
            time,
            songs.Count,
            artists.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count(),
            Rank(songs.Select(s => (s.Id, s.Plays))),
            Rank(albums),
            Rank(artists));
    }

    private static List<RankedId> Rank(IEnumerable<(string Id, int Plays)> plays) =>
    [
        .. plays
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .Select(g => new RankedId(g.Key, g.Sum(p => p.Plays)))
            .OrderByDescending(r => r.Plays)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .Take(TopSize),
    ];

    private static List<ActivityBar> Activity(IReadOnlyList<LocalPlay> plays, StatisticsPeriod period)
    {
        List<ActivityBar> bars = [];
        var byMonth = period.Range == StatisticsRange.Year;
        for (var start = period.Start; start < period.End; start = byMonth ? start.AddMonths(1) : start.AddDays(1))
        {
            var end = byMonth ? start.AddMonths(1) : start.AddDays(1);
            bars.Add(new ActivityBar(start, plays.Count(p => p.Local >= start && p.Local < end)));
        }

        return bars;
    }

    private static int[] Hours(IReadOnlyList<LocalPlay> plays)
    {
        var hours = new int[24];
        foreach (var play in plays)
        {
            hours[play.Local.Hour]++;
        }

        return hours;
    }

    private sealed record LocalPlay(string TrackId, DateTime Local);

    private sealed record SongPlays(string Id, PlayedTrack? Track, int Plays);
}
