using System.Net;
using Groovefin.Core.Api;
using Groovefin.Core.History;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.History;

/// <summary>Plays the server missed while offline are kept and sent once it can be reached (#339).</summary>
public sealed class PlayHistoryOfflineTests : IDisposable
{
    private static readonly ServerConnection Alice = new(new Uri("https://jellyfin.example/"), "alice", "token");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePlaybackEngine _engine;
    private readonly IPlaybackReporter _reporter = Substitute.For<IPlaybackReporter>();
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly MemoryPlayHistoryStore _store = new();
    private readonly PrivacyMode _privacy = new();
    private readonly FakeNetworkStatus _network = new();
    private readonly PlaybackReporting _reporting;
    private readonly PlayHistory _sut;
    private readonly List<Track> _played = [];
    private bool _offline = true;

    public PlayHistoryOfflineTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _client.Connection.Returns(Alice);
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var query = call.Arg<TrackQuery>();
            return new PagedResult<Track>([.. _played.Skip(query.StartIndex).Take(query.Limit)], _played.Count, query.StartIndex);
        });
        _reporter.ReportStartAsync(Arg.Any<PlaybackReport>(), Arg.Any<CancellationToken>())
            .Returns(_ => _offline ? Task.FromException(Unreachable) : Task.CompletedTask);
        _reporter.MarkPlayedAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => _offline ? Task.FromException(Unreachable) : Task.CompletedTask);
        _reporting = new PlaybackReporting(_engine, _reporter, _privacy, new ImmediateDispatcher(), _time, NullLogger<PlaybackReporting>.Instance);
        _sut = new PlayHistory(_engine, _reporting, _client, _library, _store, _privacy, _time, NullLogger<PlayHistory>.Instance, _reporter, _network);
    }

    private static JellyfinException Unreachable => new("Unreachable", (HttpStatusCode?)null);

    public void Dispose()
    {
        _sut.Dispose();
        _reporting.Dispose();
    }

    private static Track Song(string id) => new() { Id = id, Name = id, Duration = TimeSpan.FromSeconds(200) };

    private void Start(string id) => _engine.Load(new PlaybackSource($"q-{id}", new Uri($"file:///{id}.flac"), Song(id)));

    private async Task SettledAsync()
    {
        await _reporting.FlushAsync();
        await _sut.FlushAsync();
    }

    private PlayHistoryData Saved => _store.Saved[Alice.UserDataKey];

    [Fact]
    public async Task A_start_the_server_missed_is_kept_with_its_time()
    {
        var at = _time.GetUtcNow();

        Start("a");
        await SettledAsync();

        Saved.Pending.Should().Equal(new PendingPlay("a", at));
    }

    [Fact]
    public async Task The_kept_plays_go_when_the_network_is_back_and_count_as_reported()
    {
        var at = _time.GetUtcNow();
        Start("a");
        _time.Advance(TimeSpan.FromMinutes(4));
        Start("b");
        await SettledAsync();
        _offline = false;

        _network.IsOnline = false;
        _network.IsOnline = true;
        await SettledAsync();

        Received.InOrder(() =>
        {
            _reporter.MarkPlayedAsync("a", at, Arg.Any<CancellationToken>());
            _reporter.MarkPlayedAsync("b", at.AddMinutes(4), Arg.Any<CancellationToken>());
        });
        Saved.Pending.Should().BeEmpty();
        Saved.ReportedStarts.Select(s => s.TrackId).Should().Equal("a", "b");
    }

    [Fact]
    public async Task A_start_that_reaches_the_server_sends_the_kept_plays_too()
    {
        Start("a");
        await SettledAsync();
        _offline = false;

        Start("b");
        await SettledAsync();

        await _reporter.Received(1).MarkPlayedAsync("a", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        Saved.Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Still_offline_the_plays_wait_and_a_refused_one_is_dropped()
    {
        Start("gone");
        Start("a");
        await SettledAsync();

        await _sut.SyncAsync();
        Saved.Pending.Select(p => p.TrackId).Should().Equal("gone", "a");

        _offline = false;
        _reporter.MarkPlayedAsync("gone", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new JellyfinException("Not found", HttpStatusCode.NotFound)));
        await _sut.SyncAsync();

        Saved.Pending.Should().BeEmpty();
        Saved.ReportedStarts.Select(s => s.TrackId).Should().Equal("a");
    }

    [Fact]
    public async Task A_sent_play_is_not_counted_again_as_another_devices()
    {
        Start("a");
        await SettledAsync();
        await _sut.SyncAsync();
        _offline = false;
        _time.Advance(TimeSpan.FromMinutes(1));
        _played.Add(Song("a") with { PlayCount = 1, LastPlayed = _time.GetUtcNow() });

        await _sut.SyncAsync();

        Saved.Plays.Should().NotContain(p => p.Source != PlaySource.Groovefin, "the server's new play is the one Groovefin sent");
    }

    [Fact]
    public async Task Nothing_is_kept_in_privacy_mode()
    {
        _privacy.IsOn = true;

        Start("a");
        await SettledAsync();

        _store.Saved.Should().NotContainKey(Alice.UserDataKey);
    }
}
