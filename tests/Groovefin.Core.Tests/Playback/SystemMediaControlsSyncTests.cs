using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ReturnsExtensions;

namespace Groovefin.Core.Tests.Playback;

public sealed class SystemMediaControlsSyncTests : IDisposable
{
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(3);

    private readonly FakeTimeProvider _time = new();
    private readonly FakePlaybackEngine _engine;
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly ISystemMediaControls _controls = Substitute.For<ISystemMediaControls>();
    private readonly IImageCache _images = Substitute.For<IImageCache>();
    private readonly QueuedDispatcher _dispatcher = new();
    private readonly SystemMediaControlsSync _sut;

    public SystemMediaControlsSyncTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _player.Engine.Returns(_engine);
        _sut = new SystemMediaControlsSync(_player, _controls, _images, _dispatcher, _time);
        _controls.ClearReceivedCalls();
    }

    public void Dispose() => _sut.Dispose();

    private static Track Song(string id, string? imageTag = "tag") => new()
    {
        Id = id,
        Name = $"Song {id}",
        AlbumId = $"album-{id}",
        AlbumName = $"Album {id}",
        AlbumImageTag = imageTag,
        Artists = [new ArtistRef("x", "Artist X"), new ArtistRef("y", "Artist Y")],
        Duration = Length,
    };

    private void Load(Track track) => _engine.Load(new PlaybackSource($"q-{track.Id}", new Uri($"http://stream/{track.Id}"), track));

    /// <summary>Moves the clock and reports the position, like a playing engine (past its throttle).</summary>
    private void PlayFor(TimeSpan elapsed, TimeSpan? position = null)
    {
        _time.Advance(elapsed);
        _engine.SimulatePosition(position ?? _engine.Position + elapsed, _engine.Duration);
    }

    [Fact]
    public void Starting_up_clears_the_controls()
    {
        var controls = Substitute.For<ISystemMediaControls>();

        using var sut = new SystemMediaControlsSync(_player, controls, _images, _dispatcher, _time);

        controls.Received(1).SetDisplayAsync(null);
        controls.Received(1).SetStatus(PlaybackState.Idle);
        controls.Received(1).SetTimeline(TimeSpan.Zero, TimeSpan.Zero);
    }

    [Fact]
    public async Task A_loaded_track_shows_its_details_and_then_its_cover()
    {
        _images.GetAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>()).Returns("C:\\cache\\a.jpg");

        Load(Song("a"));
        await _sut.DisplayUpdate;

        var display = new MediaControlsDisplay("Song a", "Artist X, Artist Y", "Album a", null);
        Received.InOrder(() =>
        {
            _controls.SetDisplayAsync(display);
            _controls.SetDisplayAsync(display with { ArtworkPath = "C:\\cache\\a.jpg" });
        });
        await _images.Received(1).GetAsync(
            new ImageRequest("album-a", ImageKind.Primary, "tag", (int)SystemMediaControlsSync.ArtworkWidth),
            Arg.Any<CancellationToken>());
        _controls.Received(1).SetTimeline(TimeSpan.Zero, Length);
    }

    [Fact]
    public async Task Missing_details_are_shown_empty()
    {
        _engine.Load(new PlaybackSource("q", new Uri("http://stream/a"), new Track { Id = "a", Name = "Lonely" }));
        await _sut.DisplayUpdate;

        await _controls.Received(1).SetDisplayAsync(new MediaControlsDisplay("Lonely", string.Empty, string.Empty, null));
        await _images.DidNotReceiveWithAnyArgs().GetAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Without_a_cover_only_the_details_are_shown()
    {
        Load(Song("a", imageTag: null));
        await _sut.DisplayUpdate;

        await _controls.ReceivedWithAnyArgs(1).SetDisplayAsync(default);
        await _images.DidNotReceiveWithAnyArgs().GetAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_cover_that_cannot_be_loaded_is_left_out()
    {
        _images.GetAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>()).ReturnsNull();

        Load(Song("a"));
        await _sut.DisplayUpdate;

        await _controls.ReceivedWithAnyArgs(1).SetDisplayAsync(default);
    }

    [Fact]
    public async Task A_cover_arriving_after_the_next_track_started_is_dropped()
    {
        var slowCover = new TaskCompletionSource<string?>();
        _images.GetAsync(Arg.Is<ImageRequest>(r => r.ItemId == "album-a"), Arg.Any<CancellationToken>()).Returns(slowCover.Task);
        _images.GetAsync(Arg.Is<ImageRequest>(r => r.ItemId == "album-b"), Arg.Any<CancellationToken>()).Returns("b.jpg");

        Load(Song("a"));
        var first = _sut.DisplayUpdate;
        Load(Song("b"));
        await _sut.DisplayUpdate;
        slowCover.SetResult("a.jpg");
        await first;

        await _controls.DidNotReceive().SetDisplayAsync(Arg.Is<MediaControlsDisplay?>(d => d != null && d.ArtworkPath == "a.jpg"));
        await _controls.Received(1).SetDisplayAsync(Arg.Is<MediaControlsDisplay?>(d => d != null && d.ArtworkPath == "b.jpg"));
    }

    [Fact]
    public async Task Stopping_clears_the_controls()
    {
        Load(Song("a", imageTag: null));
        _controls.ClearReceivedCalls();

        _engine.Stop();
        await _sut.DisplayUpdate;

        await _controls.Received(1).SetDisplayAsync(null);
        _controls.Received().SetStatus(PlaybackState.Idle);
    }

    [Theory]
    [InlineData(PlaybackState.Opening)]
    [InlineData(PlaybackState.Playing)]
    [InlineData(PlaybackState.Paused)]
    public void State_changes_update_the_status_and_the_timeline(PlaybackState state)
    {
        Load(Song("a"));
        _controls.ClearReceivedCalls();

        _engine.SimulateState(state);

        _controls.Received(1).SetStatus(state);
        _controls.Received(1).SetTimeline(TimeSpan.Zero, Length);
    }

    [Fact]
    public void While_playing_the_timeline_is_refreshed_every_interval()
    {
        Load(Song("a"));
        _engine.SimulateState(PlaybackState.Playing);
        _controls.ClearReceivedCalls();

        PlayFor(TimeSpan.FromSeconds(2));
        PlayFor(TimeSpan.FromSeconds(2));
        _controls.DidNotReceiveWithAnyArgs().SetTimeline(default, default);

        PlayFor(TimeSpan.FromSeconds(1));
        _controls.Received(1).SetTimeline(SystemMediaControlsSync.TimelineInterval, Length);
    }

    [Fact]
    public void A_seek_refreshes_the_timeline_at_once()
    {
        Load(Song("a"));
        _engine.SimulateState(PlaybackState.Playing);
        _controls.ClearReceivedCalls();

        PlayFor(TimeSpan.FromSeconds(1), position: TimeSpan.FromSeconds(90));

        _controls.Received(1).SetTimeline(TimeSpan.FromSeconds(90), Length);
    }

    [Fact]
    public void A_newly_known_length_refreshes_the_timeline()
    {
        _engine.Load(new PlaybackSource("q", new Uri("http://stream/a"), new Track { Id = "a", Name = "a" }));
        _engine.SimulateState(PlaybackState.Playing);
        _controls.ClearReceivedCalls();

        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SimulatePosition(TimeSpan.FromSeconds(1), Length);

        _controls.Received(1).SetTimeline(TimeSpan.FromSeconds(1), Length);
    }

    [Fact]
    public void While_paused_a_position_report_without_a_jump_changes_nothing()
    {
        Load(Song("a"));
        _engine.SimulateState(PlaybackState.Paused);
        _controls.ClearReceivedCalls();

        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SimulatePosition(TimeSpan.Zero, Length);

        _controls.DidNotReceiveWithAnyArgs().SetTimeline(default, default);
    }

    [Theory]
    [InlineData(MediaControlsButton.Play, PlaybackState.Paused, true)]
    [InlineData(MediaControlsButton.Play, PlaybackState.Idle, true)]
    [InlineData(MediaControlsButton.Play, PlaybackState.Playing, false)]
    [InlineData(MediaControlsButton.Pause, PlaybackState.Playing, true)]
    [InlineData(MediaControlsButton.Pause, PlaybackState.Buffering, true)]
    [InlineData(MediaControlsButton.Pause, PlaybackState.Paused, false)]
    public void Play_and_pause_toggle_only_when_they_change_something(MediaControlsButton button, PlaybackState state, bool toggles)
    {
        Load(Song("a"));
        _engine.SimulateState(state);

        _controls.ButtonPressed += Raise.EventWith(new MediaControlsButtonEventArgs(button));
        _dispatcher.RunAll();

        _player.Received(toggles ? 1 : 0).TogglePlayPause();
    }

    [Fact]
    public void Next_and_previous_run_on_the_ui_thread()
    {
        _controls.ButtonPressed += Raise.EventWith(new MediaControlsButtonEventArgs(MediaControlsButton.Next));
        _controls.ButtonPressed += Raise.EventWith(new MediaControlsButtonEventArgs(MediaControlsButton.Previous));
        _player.DidNotReceive().Next();

        _dispatcher.RunAll();

        Received.InOrder(() =>
        {
            _player.Next();
            _player.Previous();
        });
    }

    [Fact]
    public void After_dispose_nothing_is_synced()
    {
        _sut.Dispose();

        Load(Song("a"));
        _controls.ButtonPressed += Raise.EventWith(new MediaControlsButtonEventArgs(MediaControlsButton.Next));

        _controls.DidNotReceiveWithAnyArgs().SetDisplayAsync(default);
        _controls.DidNotReceiveWithAnyArgs().SetStatus(default);
        _controls.DidNotReceiveWithAnyArgs().SetTimeline(default, default);
        _dispatcher.Pending.Should().Be(0);
    }
}
