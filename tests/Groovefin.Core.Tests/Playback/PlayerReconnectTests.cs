using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ReturnsExtensions;

namespace Groovefin.Core.Tests.Playback;

/// <summary>A stream the network broke is tried again where it stopped before it is skipped (#385).</summary>
public sealed class PlayerReconnectTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly QueueManager _queue = new(new Random(1));
    private readonly FakePlaybackEngine _engine = new();
    private readonly IStreamUrlProvider _streams = Substitute.For<IStreamUrlProvider>();
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IOfflineLibrary _offline = Substitute.For<IOfflineLibrary>();
    private readonly FakeNetworkStatus _network = new();
    private readonly Player _sut;
    private int _reconnectingChanges;

    public PlayerReconnectTests()
    {
        _client.Connection.Returns(new ServerConnection(new Uri("http://media:8096/"), "user", "token"));
        _streams.GetStreamUrl(Arg.Any<string>(), Arg.Any<StreamOptions>()).Returns(call => new Uri($"http://stream/{call.Arg<string>()}"));
        _offline.FileOf(Arg.Any<string>()).ReturnsNull();
        _sut = new Player(_queue, _engine, _streams, _client, new InMemorySettingsService(), _errors, NullLogger<Player>.Instance, _offline, _network, _time);
        _sut.ReconnectingChanged += (_, _) => _reconnectingChanges++;
    }

    public void Dispose() => _sut.Dispose();

    private static Track[] Tracks(params string[] ids) => ids.Select(id => new Track { Id = id, Name = id }).ToArray();

    private string? CurrentTrack => _queue.Current?.Track.Id;

    /// <summary>Plays "a" (then "b", "c") and breaks its stream at 1:30.</summary>
    private void PlayAndBreak()
    {
        _sut.Play(Tracks("a", "b", "c"));
        _engine.SimulateState(PlaybackState.Playing);
        _engine.SimulatePosition(TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(4));
        _engine.Calls.Clear();
        _engine.SimulateNetworkFailure();
    }

    [Fact]
    public void A_broken_stream_waits_and_tries_the_same_song_again_where_it_stopped()
    {
        PlayAndBreak();

        _sut.IsReconnecting.Should().BeTrue();
        _engine.Calls.Should().BeEmpty("it waits first");
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);

        _time.Advance(Player.RetryDelays[0]);

        _engine.Calls.Should().Equal($"load {_queue.Current!.Id} autoPlay=True");
        _engine.Current!.Track.Id.Should().Be("a");
        _engine.Current.StartPosition.Should().Be(TimeSpan.FromSeconds(90));
        CurrentTrack.Should().Be("a", "the queue does not move");
    }

    [Fact]
    public void A_try_that_plays_ends_the_waiting()
    {
        PlayAndBreak();
        _time.Advance(Player.RetryDelays[0]);

        _engine.SimulateState(PlaybackState.Playing);

        _sut.IsReconnecting.Should().BeFalse();
        _reconnectingChanges.Should().Be(2);
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public void Each_try_waits_longer_and_all_start_where_the_stream_broke()
    {
        PlayAndBreak();

        foreach (var delay in Player.RetryDelays)
        {
            _engine.Calls.Clear();
            _time.Advance(delay - TimeSpan.FromMilliseconds(1));
            _engine.Calls.Should().BeEmpty();
            _time.Advance(TimeSpan.FromMilliseconds(1));
            _engine.Calls.Should().ContainSingle();
            _engine.Current!.StartPosition.Should().Be(TimeSpan.FromSeconds(90));
            _engine.SimulateNetworkFailure();
        }

        CurrentTrack.Should().Be("b", "after the last try the song counts as failed and is skipped");
        _sut.IsReconnecting.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<PlaybackException>(), Operations.Playback);
    }

    [Fact]
    public void The_retry_delays_add_up_to_about_half_a_minute()
    {
        Player.RetryDelays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d).Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_file_that_cannot_play_is_skipped_at_once()
    {
        _sut.Play(Tracks("a", "b"));

        _engine.SimulateFailure("MediaSourceNotSupported");

        _sut.IsReconnecting.Should().BeFalse();
        CurrentTrack.Should().Be("b");
        _errors.Received(1).Report(Arg.Any<PlaybackException>(), Operations.Playback);
    }

    [Fact]
    public void Offline_the_tries_wait_without_moving_the_queue_and_go_when_the_network_is_back()
    {
        PlayAndBreak();
        _network.IsOnline = false;

        _time.Advance(Player.RetryDelays[0]);
        _engine.Calls.Should().BeEmpty("offline a load would skip to a downloaded song");
        CurrentTrack.Should().Be("a");
        _sut.IsReconnecting.Should().BeTrue();

        _network.IsOnline = true;

        _engine.Calls.Should().ContainSingle().Which.Should().StartWith("load");
        _engine.Current!.StartPosition.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void Any_failure_while_offline_counts_as_the_network()
    {
        _sut.Play(Tracks("a", "b"));
        _network.IsOnline = false;

        _engine.SimulateFailure("Unknown");

        _sut.IsReconnecting.Should().BeTrue();
        CurrentTrack.Should().Be("a");
    }

    [Fact]
    public void A_downloaded_file_is_not_tried_again()
    {
        _offline.FileOf("a").Returns(Path.Join(Path.GetTempPath(), "a.flac"));
        _sut.Play(Tracks("a", "b"));

        _engine.SimulateNetworkFailure();

        _sut.IsReconnecting.Should().BeFalse();
        CurrentTrack.Should().Be("b");
    }

    [Theory]
    [InlineData("next")]
    [InlineData("previous")]
    [InlineData("play")]
    public void Moving_on_ends_the_waiting(string action)
    {
        PlayAndBreak();

        switch (action)
        {
            case "next": _sut.Next(); break;
            case "previous": _sut.Previous(); break;
            default: _sut.Play(Tracks("x")); break;
        }

        _sut.IsReconnecting.Should().BeFalse();
        _engine.Calls.Clear();
        _time.Advance(TimeSpan.FromMinutes(1));
        _engine.Calls.Should().BeEmpty("the waiting try was dropped");
    }

    [Fact]
    public void Play_while_waiting_tries_again_at_once()
    {
        PlayAndBreak();

        _sut.TogglePlayPause();

        _engine.Calls.Should().ContainSingle().Which.Should().StartWith("load");
        _engine.Current!.StartPosition.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void A_stream_stuck_buffering_starts_again_when_the_network_is_back()
    {
        _sut.Play(Tracks("a", "b"));
        _engine.SimulateState(PlaybackState.Playing);
        _engine.SimulatePosition(TimeSpan.FromSeconds(42), TimeSpan.FromMinutes(4));
        _network.IsOnline = false;
        _engine.SimulateState(PlaybackState.Buffering);
        _engine.Calls.Clear();

        _network.IsOnline = true;

        _engine.Calls.Should().ContainSingle().Which.Should().StartWith("load");
        _engine.Current!.StartPosition.Should().Be(TimeSpan.FromSeconds(42));
        CurrentTrack.Should().Be("a");
    }

    [Fact]
    public void A_playing_stream_is_left_alone_when_the_network_comes_back()
    {
        _sut.Play(Tracks("a"));
        _engine.SimulateState(PlaybackState.Playing);
        _network.IsOnline = false;
        _engine.Calls.Clear();

        _network.IsOnline = true;

        _engine.Calls.Should().BeEmpty();
    }
}
