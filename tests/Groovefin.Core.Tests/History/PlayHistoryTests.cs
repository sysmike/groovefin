using Groovefin.Core.Api;
using Groovefin.Core.History;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ReturnsExtensions;

namespace Groovefin.Core.Tests.History;

public sealed class PlayHistoryTests : IDisposable
{
    private static readonly ServerConnection Alice = new(new Uri("https://jellyfin.example/"), "alice", "token");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePlaybackEngine _engine;
    private readonly IPlaybackReporter _reporter = Substitute.For<IPlaybackReporter>();
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly MemoryPlayHistoryStore _store = new();
    private readonly PrivacyMode _privacy = new();
    private readonly PlaybackReporting _reporting;
    private readonly PlayHistory _sut;
    private readonly List<Track> _played = [];

    public PlayHistoryTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _client.Connection.Returns(Alice);
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var query = call.Arg<TrackQuery>();
            var page = _played.OrderByDescending(t => t.LastPlayed).Skip(query.StartIndex).Take(query.Limit).ToList();
            return new PagedResult<Track>(page, _played.Count, query.StartIndex);
        });
        _reporting = new PlaybackReporting(_engine, _reporter, _privacy, new ImmediateDispatcher(), _time, NullLogger<PlaybackReporting>.Instance);
        _sut = new PlayHistory(_engine, _reporting, _client, _library, _store, _privacy, _time, NullLogger<PlayHistory>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _reporting.Dispose();
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private static Track Song(string id, int seconds = 200) => new()
    {
        Id = id,
        Name = id,
        Duration = TimeSpan.FromSeconds(seconds),
        AlbumId = $"album-{id}",
        Artists = [new ArtistRef($"artist-{id}", id), new ArtistRef("guest", "Guest")],
    };

    private async Task<PlayHistoryData> SavedAsync()
    {
        await PlaysAsync();
        return _store.Saved[_store.LastKey!];
    }

    /// <summary>Loads and plays <paramref name="track"/> for <paramref name="seconds"/>, in steps of one second.</summary>
    private void Listen(Track track, double seconds)
    {
        _engine.Load(new PlaybackSource($"q-{track.Id}", new Uri($"http://stream/{track.Id}"), track));
        _engine.SimulateState(PlaybackState.Playing);
        PlayFor(seconds);
    }

    private void PlayFor(double seconds)
    {
        for (var elapsed = 0.0; elapsed < seconds; elapsed++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            _engine.SimulatePosition(_engine.Position + TimeSpan.FromSeconds(1), _engine.Duration);
        }
    }

    /// <summary>Makes the server report <paramref name="id"/> with this play count and last play.</summary>
    private void OnServer(string id, int playCount, DateTimeOffset lastPlayed)
    {
        _played.RemoveAll(t => t.Id == id);
        _played.Add(Song(id) with { PlayCount = playCount, LastPlayed = lastPlayed });
    }

    private async Task<List<PlayRecord>> PlaysAsync()
    {
        await _reporting.FlushAsync();
        await _sut.FlushAsync();
        return _store.Saved.TryGetValue(_store.LastKey ?? string.Empty, out var data) ? data.Plays : [];
    }

    private async Task<IEnumerable<string>> OnRepeatAsync()
    {
        await _reporting.FlushAsync();
        await _sut.FlushAsync();
        return (await _sut.GetOnRepeatAsync()).Select(e => $"{e.TrackId}×{e.Plays}");
    }

    [Fact]
    public async Task A_track_counts_once_listened_to_for_half_its_length()
    {
        Listen(Song("a"), 99);
        (await PlaysAsync()).Should().BeEmpty();

        PlayFor(150);

        // Counted at 100 s listened, 149 s ago.
        (await PlaysAsync()).Should().ContainSingle().Which.Should().Be(new PlayRecord("a", Now.AddSeconds(-149), PlaySource.Groovefin));
    }

    [Fact]
    public async Task A_long_track_counts_after_four_minutes()
    {
        Listen(Song("long", seconds: 1200), 239);
        (await PlaysAsync()).Should().BeEmpty();

        PlayFor(1);

        (await PlaysAsync()).Should().ContainSingle().Which.TrackId.Should().Be("long");
    }

    [Fact]
    public async Task Paused_time_and_seeking_do_not_count()
    {
        Listen(Song("a"), 50);
        _engine.SimulateState(PlaybackState.Paused);
        _time.Advance(TimeSpan.FromMinutes(10));
        _engine.SimulatePosition(TimeSpan.FromSeconds(190), _engine.Duration);
        _engine.SimulateState(PlaybackState.Playing);
        PlayFor(49);

        (await PlaysAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Skipped_tracks_do_not_count_and_each_load_counts_once()
    {
        Listen(Song("skipped"), 20);
        Listen(Song("a"), 150);
        Listen(Song("a"), 150);

        (await PlaysAsync()).Select(p => p.TrackId).Should().Equal("a", "a");
    }

    [Fact]
    public async Task Nothing_counts_in_privacy_mode_even_if_it_goes_off_before_the_track_ends()
    {
        _privacy.IsOn = true;
        Listen(Song("hidden"), 150);
        Listen(Song("half"), 50);
        _privacy.IsOn = false;
        PlayFor(100);

        Listen(Song("seen"), 50);
        _privacy.IsOn = true;
        _privacy.IsOn = false;
        PlayFor(100);

        (await PlaysAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_connection_nothing_is_recorded()
    {
        _client.Connection.ReturnsNull();

        Listen(Song("a"), 150);
        await _sut.SyncAsync();

        (await PlaysAsync()).Should().BeEmpty();
        (await _sut.GetOnRepeatAsync()).Should().BeEmpty();
        _store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task The_first_sync_seeds_the_tracks_played_in_the_window_with_their_play_count()
    {
        OnServer("recent", 3, Now.AddDays(-2));
        OnServer("yesterday", 1, Now.AddDays(-1));
        OnServer("old", 9, Now.AddDays(-45));

        await _sut.SyncAsync();

        // Marked as seeded (#382): On repeat counts them, the statistics of a period do not.
        (await PlaysAsync()).Should().BeEquivalentTo(
        [
            new PlayRecord("yesterday", Now.AddDays(-1), PlaySource.Seeded),
            new PlayRecord("recent", Now.AddDays(-2), PlaySource.Seeded),
            new PlayRecord("recent", Now.AddDays(-2), PlaySource.Seeded),
            new PlayRecord("recent", Now.AddDays(-2), PlaySource.Seeded),
        ]);
        (await OnRepeatAsync()).Should().Equal("recent×3");
    }

    [Fact]
    public async Task A_later_sync_adds_the_gain_in_play_count_less_the_starts_groovefin_reported()
    {
        OnServer("a", 5, Now.AddDays(-3));
        await _sut.SyncAsync();

        // Groovefin plays "a" once (the server counts its start); another device plays it twice.
        Listen(Song("a"), 150);
        OnServer("a", 8, Now);
        await _sut.SyncAsync();

        var plays = await PlaysAsync();
        plays.Count(p => p.Source == PlaySource.Groovefin).Should().Be(1);
        plays.Count(p => p.Source == PlaySource.Seeded).Should().Be(5);
        plays.Count(p => p.Source == PlaySource.Server).Should().Be(2);
    }

    [Fact]
    public async Task A_track_first_seen_after_the_first_sync_counts_once()
    {
        await _sut.SyncAsync();
        _time.Advance(TimeSpan.FromHours(1));

        OnServer("new", 40, Now);
        await _sut.SyncAsync();

        (await PlaysAsync()).Should().ContainSingle().Which.Should().Be(new PlayRecord("new", Now, PlaySource.Server));
    }

    [Fact]
    public async Task A_sync_reads_only_the_tracks_played_since_the_last_one()
    {
        OnServer("a", 1, Now.AddDays(-1));
        await _sut.SyncAsync();
        _library.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromHours(1));

        await _sut.SyncAsync();

        // Also the hourly sync ran: neither read past the first page, nor counted "a" again.
        (await PlaysAsync()).Should().ContainSingle();
        await _library.Received().GetTracksAsync(
            Arg.Is<TrackQuery>(q => q.SortBy == TrackSortField.DatePlayed && q.Descending && q.StartIndex == 0),
            Arg.Any<CancellationToken>());
        await _library.DidNotReceive().GetTracksAsync(Arg.Is<TrackQuery>(q => q.StartIndex > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_sync_changes_nothing_and_the_next_one_reads_from_the_same_point()
    {
        OnServer("a", 2, Now.AddDays(-1));
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Track>>(new JellyfinException("offline")));

        await _sut.SyncAsync();

        (await PlaysAsync()).Should().BeEmpty();
        _store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task The_history_syncs_a_minute_after_startup_and_then_every_hour()
    {
        OnServer("a", 2, Now.AddDays(-1));

        _time.Advance(PlayHistory.FirstSyncDelay);
        (await PlaysAsync()).Should().HaveCount(2);

        OnServer("a", 3, Now);
        _time.Advance(PlayHistory.SyncInterval);
        (await PlaysAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Plays_are_kept_for_a_year_and_on_repeat_looks_at_the_last_30_days()
    {
        Listen(Song("a"), 150);
        Listen(Song("a"), 150);
        await PlaysAsync();

        _time.Advance(PlayHistory.Window + TimeSpan.FromMinutes(1));
        Listen(Song("b"), 150);

        (await PlaysAsync()).Select(p => p.TrackId).Should().Equal("a", "a", "b");
        (await OnRepeatAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Plays_older_than_a_year_are_dropped_with_their_songs()
    {
        Listen(Song("a"), 150);
        await PlaysAsync();

        _time.Advance(PlayHistory.Retention + TimeSpan.FromMinutes(1));
        Listen(Song("b"), 150);

        var saved = await SavedAsync();
        saved.Plays.Select(p => p.TrackId).Should().Equal("b");
        saved.Tracks.Keys.Should().Equal("b");
    }

    [Fact]
    public async Task A_play_keeps_its_songs_album_artists_and_length()
    {
        Listen(Song("a", seconds: 300), 150);
        OnServer("b", 1, Now);
        await _sut.SyncAsync();

        var saved = await SavedAsync();
        saved.Tracks["a"].Should().BeEquivalentTo(new PlayedTrack("album-a", ["artist-a", "guest"], TimeSpan.FromSeconds(300)));
        saved.Tracks["b"].Should().BeEquivalentTo(new PlayedTrack("album-b", ["artist-b", "guest"], TimeSpan.FromSeconds(200)));
    }

    [Fact]
    public async Task The_log_is_a_copy_of_the_plays_since_the_history_began()
    {
        var started = (await _sut.GetLogAsync()).Started;
        started.Should().Be(Now, "the history begins at its first use");
        Listen(Song("a"), 150);
        await PlaysAsync();

        var log = await _sut.GetLogAsync();
        Listen(Song("b"), 150);
        await PlaysAsync();

        log.Started.Should().Be(started);
        log.Plays.Select(p => p.TrackId).Should().Equal("a");
        log.Tracks.Keys.Should().Equal("a");
    }

    [Fact]
    public async Task A_history_from_before_the_statistics_begins_at_its_first_use()
    {
        var history = new PlayHistoryData();
        history.Plays.Add(new PlayRecord("a", Now.AddDays(-3), PlaySource.Server));
        _store.Preload(history);

        var log = await _sut.GetLogAsync();

        log.Started.Should().Be(Now);
        log.Plays.Should().ContainSingle();
        log.Tracks.Should().BeEmpty();
    }

    [Fact]
    public async Task Remembered_songs_are_kept_for_their_plays_only()
    {
        var history = new PlayHistoryData();
        history.Plays.Add(new PlayRecord("old", Now.AddDays(-3), PlaySource.Server));
        _store.Preload(history);
        await _sut.GetLogAsync();

        await _sut.RememberTracksAsync([Song("old"), Song("never-played")]);

        var log = await _sut.GetLogAsync();
        log.Tracks.Keys.Should().Equal("old");
        log.Tracks["old"].AlbumId.Should().Be("album-old");
    }

    [Fact]
    public async Task Clearing_drops_the_plays_but_not_what_the_next_sync_needs()
    {
        OnServer("a", 3, Now.AddDays(-1));
        await _sut.SyncAsync();
        Listen(Song("b"), 150);
        await PlaysAsync();
        _time.Advance(TimeSpan.FromHours(1));

        await _sut.ClearAsync();
        await _sut.SyncAsync();

        var log = await _sut.GetLogAsync();
        log.Plays.Should().BeEmpty("the sync must not count the plays it saw before again");
        log.Tracks.Should().BeEmpty();
        log.Started.Should().Be(Now);
        (await OnRepeatAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Each_user_has_a_history_of_their_own()
    {
        Listen(Song("a"), 150);
        await PlaysAsync();
        var alice = _store.LastKey;

        _client.Connection.Returns(Alice with { UserId = "bob" });
        Listen(Song("b"), 150);
        await PlaysAsync();

        _store.LastKey.Should().NotBe(alice);
        _store.Saved[alice!].Plays.Select(p => p.TrackId).Should().Equal("a");
        _store.Saved[_store.LastKey!].Plays.Select(p => p.TrackId).Should().Equal("b");
    }

    [Fact]
    public async Task The_saved_history_is_read_back_at_the_first_use()
    {
        var history = new PlayHistoryData();
        history.Plays.Add(new PlayRecord("a", Now.AddDays(-1), PlaySource.Groovefin));
        history.Plays.Add(new PlayRecord("a", Now.AddDays(-2), PlaySource.Server));
        using var fresh = new PlayHistory(_engine, _reporting, _client, _library, _store, _privacy, _time, NullLogger<PlayHistory>.Instance);
        _store.Preload(history);

        (await fresh.GetOnRepeatAsync()).Should().ContainSingle().Which.Should().Be(new OnRepeatEntry("a", 2, Now.AddDays(-1)));
    }
}
