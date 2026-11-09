using Groovefin.Core.Api;
using Groovefin.Core.History;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Statistics;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Statistics;

public sealed class ListeningStatisticsTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePlaybackEngine _engine;
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly MemoryPlayHistoryStore _store = new();
    private readonly PlaybackReporting _reporting;
    private readonly PlayHistory _history;
    private readonly ListeningStatistics _sut;

    public ListeningStatisticsTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _client.Connection.Returns(new ServerConnection(new Uri("https://jellyfin.example/"), "alice", "token"));
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<Track>([], 0, 0));
        var privacy = new PrivacyMode();
        _reporting = new PlaybackReporting(_engine, Substitute.For<IPlaybackReporter>(), privacy, new ImmediateDispatcher(), _time, NullLogger<PlaybackReporting>.Instance);
        _history = new PlayHistory(_engine, _reporting, _client, _library, _store, privacy, _time, NullLogger<PlayHistory>.Instance);
        _sut = new ListeningStatistics(_history, _library, _time);
    }

    public void Dispose()
    {
        _history.Dispose();
        _reporting.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StatisticsPeriod November => StatisticsPeriod.Containing(StatisticsRange.Month, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified), DayOfWeek.Monday);

    private static Track Song(string id, int plays = 0) => new()
    {
        Id = id,
        Name = id,
        AlbumId = $"album-{id}",
        Artists = [new ArtistRef($"artist-{id}", id)],
        Duration = TimeSpan.FromMinutes(3),
        PlayCount = plays,
    };

    /// <summary>A history from before #382: its plays have no songs, and it began earlier this month.</summary>
    private void Preload(params string[] ids)
    {
        var history = new PlayHistoryData { Started = _time.GetUtcNow().AddDays(-8) };
        foreach (var id in ids)
        {
            history.Plays.Add(new PlayRecord(id, _time.GetUtcNow().AddDays(-1), PlaySource.Groovefin));
        }

        _store.Preload(history);
    }

    [Fact]
    public async Task Plays_without_their_songs_get_them_from_the_server_once()
    {
        Preload("a", "b");
        _library.GetTracksByIdAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<string>>().Select(id => Song(id)).ToList());

        var report = await _sut.GetReportAsync(November, Ct);
        await _sut.GetReportAsync(November, Ct);

        report.Totals.TopAlbums.Select(a => a.Id).Should().BeEquivalentTo("album-a", "album-b");
        report.Totals.Time.Should().Be(TimeSpan.FromMinutes(6));
        await _library.Received(1).GetTracksByIdAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        (await _history.GetLogAsync()).Tracks.Keys.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task Without_the_server_the_plays_count_without_their_songs()
    {
        Preload("a");
        _library.GetTracksByIdAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Track>>(new JellyfinException("offline")));

        var report = await _sut.GetReportAsync(November, Ct);

        report.Totals.Plays.Should().Be(1);
        report.Totals.TopAlbums.Should().BeEmpty();
    }

    [Fact]
    public async Task All_time_reads_the_played_songs_most_played_first_until_the_unplayed_ones()
    {
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var query = call.Arg<TrackQuery>();
            List<Track> songs = query.StartIndex == 0
                ? [.. Enumerable.Range(0, ListeningStatistics.PageSize).Select(i => Song($"s{i}", plays: 2))]
                : [Song("last", plays: 1), Song("unplayed")];
            return new PagedResult<Track>(songs, 10_000, query.StartIndex);
        });

        var report = await _sut.GetReportAsync(StatisticsPeriod.AllTime, Ct);

        report.Totals.Plays.Should().Be((ListeningStatistics.PageSize * 2) + 1);
        report.Totals.SongCount.Should().Be(ListeningStatistics.PageSize + 1);
        await _library.Received(2).GetTracksAsync(
            Arg.Is<TrackQuery>(q => q.SortBy == TrackSortField.PlayCount && q.Descending && q.Limit == ListeningStatistics.PageSize),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Today_is_the_local_date()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("late", TimeSpan.FromHours(13), "late", "late"));

        _sut.Today.Should().Be(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Unspecified));
        (await _sut.GetReportAsync(November, Ct)).Period.Should().Be(November);
    }
}
