using Groovefin.Core.History;
using Groovefin.Core.Models;
using Groovefin.Core.Statistics;

namespace Groovefin.Core.Tests.Statistics;

public sealed class ListeningReportsTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, PlayedTrack> Tracks = new(StringComparer.Ordinal)
    {
        ["a"] = new PlayedTrack("album-1", ["artist-1"], TimeSpan.FromMinutes(4)),
        ["b"] = new PlayedTrack("album-1", ["artist-1", "artist-2"], TimeSpan.FromMinutes(3)),
        ["c"] = new PlayedTrack("album-2", ["artist-3"], TimeSpan.FromMinutes(5)),
    };

    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

    private static StatisticsPeriod Month(int year, int month) =>
        StatisticsPeriod.Containing(StatisticsRange.Month, Day(year, month, 1), DayOfWeek.Monday);

    private static PlayRecord Play(string id, int month, int day, int hour = 12, PlaySource source = PlaySource.Groovefin) =>
        new(id, new DateTimeOffset(2026, month, day, hour, 0, 0, TimeSpan.Zero), source);

    private static PlayLog Log(params PlayRecord[] plays) => new(Started, plays, Tracks);

    [Fact]
    public void A_period_counts_its_plays_time_songs_and_artists()
    {
        var log = Log(Play("a", 10, 1), Play("a", 10, 2), Play("b", 10, 3), Play("c", 11, 1));

        var totals = ListeningReports.ForPeriod(log, Month(2026, 10), TimeZoneInfo.Utc).Totals;

        totals.Plays.Should().Be(3);
        totals.Time.Should().Be(TimeSpan.FromMinutes(4 + 4 + 3));
        totals.SongCount.Should().Be(2);
        totals.ArtistCount.Should().Be(2);
    }

    [Fact]
    public void The_top_lists_add_up_the_plays_of_each_album_and_artist()
    {
        var log = Log(Play("a", 10, 1), Play("a", 10, 2), Play("b", 10, 3), Play("c", 10, 4), Play("c", 10, 5), Play("c", 10, 6));

        var totals = ListeningReports.ForPeriod(log, Month(2026, 10), TimeZoneInfo.Utc).Totals;

        totals.TopSongs.Should().Equal(new RankedId("c", 3), new RankedId("a", 2), new RankedId("b", 1));
        totals.TopAlbums.Should().Equal(new RankedId("album-1", 3), new RankedId("album-2", 3));
        totals.TopArtists.Should().Equal(new RankedId("artist-1", 3), new RankedId("artist-3", 3), new RankedId("artist-2", 1));
    }

    [Fact]
    public void Seeded_plays_and_plays_before_the_history_began_do_not_count()
    {
        var log = Log(
            Play("a", 9, 20, source: PlaySource.Seeded),
            Play("a", 9, 15, hour: 9),
            Play("b", 9, 15, hour: 11, source: PlaySource.Server));

        var report = ListeningReports.ForPeriod(log, Month(2026, 9), TimeZoneInfo.Utc);

        report.Totals.TopSongs.Should().Equal(new RankedId("b", 1));
        report.IsPartial.Should().BeTrue("the history began on 15 September");
        report.HistoryStart.Should().Be(new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void A_song_the_history_knows_nothing_of_counts_its_plays_without_time_album_or_artist()
    {
        var log = Log(Play("unknown", 10, 1), Play("a", 10, 1));

        var totals = ListeningReports.ForPeriod(log, Month(2026, 10), TimeZoneInfo.Utc).Totals;

        totals.Plays.Should().Be(2);
        totals.Time.Should().Be(TimeSpan.FromMinutes(4));
        totals.TopAlbums.Should().Equal(new RankedId("album-1", 1));
    }

    [Fact]
    public void The_period_before_is_compared_only_when_the_history_covers_all_of_it()
    {
        var log = Log(Play("a", 10, 10), Play("a", 11, 2), Play("b", 11, 3));

        ListeningReports.ForPeriod(log, Month(2026, 11), TimeZoneInfo.Utc).Previous!.Plays.Should().Be(1);
        ListeningReports.ForPeriod(log, Month(2026, 10), TimeZoneInfo.Utc).Previous.Should().BeNull("the history began in September");
    }

    [Fact]
    public void Plays_count_on_their_local_day_and_hour()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        // 23:30 UTC is 00:30 the next day in Berlin, in winter (+1) and in summer (+2).
        var log = Log(
            new PlayRecord("a", new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero), PlaySource.Groovefin),
            new PlayRecord("a", new DateTimeOffset(2026, 10, 15, 22, 30, 0, TimeSpan.Zero), PlaySource.Groovefin));

        var october = ListeningReports.ForPeriod(log, Month(2026, 10), berlin);
        var november = ListeningReports.ForPeriod(log, Month(2026, 11), berlin);

        october.Totals.Plays.Should().Be(1);
        october.Activity[15].Plays.Should().Be(1, "the play at 22:30 UTC in October was at 00:30 on the 16th");
        october.Hours[0].Should().Be(1);
        november.Totals.Plays.Should().Be(1);
        november.Activity[0].Plays.Should().Be(1);
    }

    [Fact]
    public void A_week_or_month_has_a_bar_per_day_and_a_year_one_per_month()
    {
        var log = Log(Play("a", 10, 1), Play("a", 10, 1), Play("b", 10, 31), Play("c", 11, 1));

        var month = ListeningReports.ForPeriod(log, Month(2026, 10), TimeZoneInfo.Utc);
        var week = ListeningReports.ForPeriod(log, StatisticsPeriod.Containing(StatisticsRange.Week, Day(2026, 10, 1), DayOfWeek.Monday), TimeZoneInfo.Utc);
        var year = ListeningReports.ForPeriod(log, StatisticsPeriod.Containing(StatisticsRange.Year, Day(2026, 10, 1), DayOfWeek.Monday), TimeZoneInfo.Utc);

        month.Activity.Should().HaveCount(31);
        month.Activity[0].Should().Be(new ActivityBar(Day(2026, 10, 1), 2));
        month.Activity[30].Plays.Should().Be(1);
        week.Activity.Should().HaveCount(7);
        week.Activity[0].Start.Should().Be(Day(2026, 9, 28));
        year.Activity.Should().HaveCount(12);
        year.Activity.Select(b => b.Plays).Should().Equal(0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 1, 0);
    }

    [Fact]
    public void The_listening_clock_counts_the_plays_per_hour()
    {
        var log = Log(Play("a", 10, 1, hour: 21), Play("a", 10, 2, hour: 21), Play("b", 10, 3, hour: 8));

        var hours = ListeningReports.ForPeriod(log, Month(2026, 10), TimeZoneInfo.Utc).Hours;

        hours.Should().HaveCount(24);
        hours[21].Should().Be(2);
        hours[8].Should().Be(1);
        hours.Sum().Should().Be(3);
    }

    [Fact]
    public void An_empty_period_has_nothing_but_its_bars()
    {
        var report = ListeningReports.ForPeriod(Log(), Month(2026, 10), TimeZoneInfo.Utc);

        report.Totals.Should().Be(ListeningTotals.Empty);
        report.Activity.Should().HaveCount(31).And.OnlyContain(b => b.Plays == 0);
    }

    [Fact]
    public void All_time_counts_the_servers_play_counts()
    {
        Track Song(string id, int plays, string album, params string[] artists) => new()
        {
            Id = id,
            Name = id,
            AlbumId = album,
            Artists = [.. artists.Select(a => new ArtistRef(a, a))],
            Duration = TimeSpan.FromMinutes(2),
            PlayCount = plays,
        };

        var report = ListeningReports.ForAllTime(
            [Song("a", 10, "album-1", "artist-1"), Song("b", 4, "album-1", "artist-1", "artist-2"), Song("never", 0, "album-3", "artist-9")],
            Log(),
            TimeZoneInfo.Utc,
            Day(2026, 11, 9));

        report.Previous.Should().BeNull();
        report.Totals.Plays.Should().Be(14);
        report.Totals.Time.Should().Be(TimeSpan.FromMinutes(28));
        report.Totals.SongCount.Should().Be(2);
        report.Totals.ArtistCount.Should().Be(2);
        report.Totals.TopAlbums.Should().Equal(new RankedId("album-1", 14));
        report.Totals.TopArtists.Should().Equal(new RankedId("artist-1", 14), new RankedId("artist-2", 4));
    }

    [Fact]
    public void All_times_charts_show_the_months_the_history_covers_up_to_a_year()
    {
        var log = Log(Play("a", 9, 20), Play("a", 11, 2), Play("a", 11, 3, source: PlaySource.Seeded));

        var report = ListeningReports.ForAllTime([], log, TimeZoneInfo.Utc, Day(2026, 11, 9));
        var later = ListeningReports.ForAllTime([], log, TimeZoneInfo.Utc, Day(2028, 1, 9));

        report.Activity.Select(b => (b.Start, b.Plays)).Should().Equal((Day(2026, 9, 1), 1), (Day(2026, 10, 1), 0), (Day(2026, 11, 1), 1));
        report.Hours.Sum().Should().Be(2);
        later.Activity.Should().HaveCount(ListeningReports.AllTimeMonths);
        later.Activity[0].Start.Should().Be(Day(2027, 2, 1));
    }
}
