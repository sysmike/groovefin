using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Playback;

public sealed class PlaybackEngineBaseTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly QueuedDispatcher _dispatcher = new();
    private readonly FakePlaybackEngine _sut;
    private readonly List<string> _events = [];

    public PlaybackEngineBaseTests()
    {
        _sut = new FakePlaybackEngine(_dispatcher, _time);
        _sut.StateChanged += (_, _) => _events.Add($"state {_sut.State}");
        _sut.PositionChanged += (_, _) => _events.Add($"position {_sut.Position.TotalSeconds}/{_sut.Duration.TotalSeconds}");
        _sut.CurrentChanged += (_, _) => _events.Add($"current {_sut.Current?.Id ?? "none"}");
        _sut.Ended += (_, _) => _events.Add("ended");
        _sut.Failed += (_, e) => _events.Add($"failed {e.Source.Id} {e.Message}");
    }

    private static PlaybackSource Source(string id, int seconds = 200) =>
        new(id, new Uri($"https://server/Audio/{id}"), new Track { Id = id, Name = id, Duration = TimeSpan.FromSeconds(seconds) });

    [Fact]
    public void Starts_idle_at_full_volume()
    {
        _sut.State.Should().Be(PlaybackState.Idle);
        _sut.Current.Should().BeNull();
        _sut.Volume.Should().Be(1);
        _sut.IsMuted.Should().BeFalse();
        _sut.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Load_sets_the_source_and_its_known_length()
    {
        _sut.Load(Source("a"));

        _sut.Current!.Id.Should().Be("a");
        _sut.Duration.Should().Be(TimeSpan.FromSeconds(200));
        _sut.Calls.Should().Equal("load a autoPlay=True");
        _events.Should().Equal("current a", "position 0/200");
    }

    [Fact]
    public void Load_without_a_known_length_and_without_autoplay()
    {
        var source = Source("a") with { Track = new Track { Id = "a", Name = "a" } };

        _sut.Load(source, autoPlay: false);

        _sut.Duration.Should().Be(TimeSpan.Zero);
        _sut.Calls.Should().Equal("load a autoPlay=False");
    }

    [Fact]
    public void Load_rejects_null()
    {
        var act = () => _sut.Load(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Transport_calls_without_a_source_do_nothing()
    {
        _sut.Play();
        _sut.Pause();
        _sut.Seek(TimeSpan.FromSeconds(5));
        _sut.Stop();

        _sut.Calls.Should().BeEmpty();
        _events.Should().BeEmpty();
    }

    [Fact]
    public void Play_and_pause_go_to_the_player()
    {
        _sut.Load(Source("a"), autoPlay: false);

        _sut.Play();
        _sut.Pause();

        _sut.Calls.Should().Equal("load a autoPlay=False", "play", "pause");
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(42, 42)]
    [InlineData(500, 200)]
    public void Seek_is_clamped_to_the_length(int seconds, int expected)
    {
        _sut.Load(Source("a"));

        _sut.Seek(TimeSpan.FromSeconds(seconds));

        _sut.Position.Should().Be(TimeSpan.FromSeconds(expected));
        _sut.Calls[^1].Should().Be($"seek {expected}");
        _events[^1].Should().Be($"position {expected}/200");
    }

    [Fact]
    public void Seek_with_an_unknown_length_is_only_clamped_at_zero()
    {
        _sut.Load(Source("a") with { Track = new Track { Id = "a", Name = "a" } });

        _sut.Seek(TimeSpan.FromSeconds(999));

        _sut.Position.Should().Be(TimeSpan.FromSeconds(999));
    }

    [Fact]
    public void Stop_unloads_and_goes_idle()
    {
        _sut.Load(Source("a"));
        _sut.SimulateState(PlaybackState.Playing);
        _dispatcher.RunAll();
        _events.Clear();

        _sut.Stop();

        _sut.Current.Should().BeNull();
        _sut.State.Should().Be(PlaybackState.Idle);
        _sut.Duration.Should().Be(TimeSpan.Zero);
        _sut.Calls[^1].Should().Be("stop");
        _events.Should().Equal("state Idle", "current none", "position 0/0");
    }

    [Fact]
    public void Volume_is_clamped_and_sent_only_when_it_changes()
    {
        _sut.Volume = 0.5;
        _sut.Volume = 0.5;
        _sut.Volume = 0.5 + (PlaybackEngineBase.VolumeTolerance / 2);
        _sut.Volume = 3;
        _sut.Volume = -1;
        _sut.Volume = double.NaN;

        _sut.Volume.Should().Be(0);
        _sut.Calls.Should().Equal("volume 0.5", "volume 1", "volume 0");
    }

    [Fact]
    public void Mute_is_sent_only_when_it_changes()
    {
        _sut.IsMuted = true;
        _sut.IsMuted = true;
        _sut.IsMuted = false;

        _sut.Calls.Should().Equal("muted True", "muted False");
    }

    [Fact]
    public void Player_reports_reach_handlers_only_through_the_dispatcher()
    {
        _sut.Load(Source("a"));
        _events.Clear();

        _sut.SimulateState(PlaybackState.Playing);
        _sut.SimulatePosition(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(200));
        _sut.SimulateEnded();

        _events.Should().BeEmpty();
        _sut.State.Should().Be(PlaybackState.Idle);
        _dispatcher.Pending.Should().Be(3);

        _dispatcher.RunAll();

        _events.Should().Equal("state Playing", "position 1/200", "ended");
    }

    [Fact]
    public void Repeated_states_are_raised_once()
    {
        _sut.Load(Source("a"));
        _events.Clear();

        _sut.SimulateState(PlaybackState.Buffering);
        _sut.SimulateState(PlaybackState.Buffering);
        _sut.SimulateState(PlaybackState.Playing);
        _dispatcher.RunAll();

        _events.Should().Equal("state Buffering", "state Playing");
    }

    [Fact]
    public void Position_updates_are_throttled_but_a_new_length_always_comes_through()
    {
        _sut.Load(Source("a", seconds: 0));
        _events.Clear();

        _sut.SimulatePosition(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(200));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _sut.SimulatePosition(TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(200));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _sut.SimulatePosition(TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(201));
        _time.Advance(PlaybackEngineBase.PositionInterval);
        _sut.SimulatePosition(TimeSpan.FromSeconds(1.5), TimeSpan.Zero);
        _dispatcher.RunAll();

        _events.Should().Equal("position 1/200", "position 1.2/201", "position 1.5/201");
    }

    private static PlaybackSource Restartable(string id) =>
        Source(id) with { UrlAt = position => new Uri($"https://server/Audio/{id}?start={position.TotalSeconds}") };

    [Fact]
    public void A_seekable_stream_seeks_in_place()
    {
        _sut.Load(Restartable("a"));
        _sut.Calls.Clear();

        _sut.Seek(TimeSpan.FromSeconds(60));

        _sut.Calls.Should().Equal("seek 60");
        _sut.Position.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Where_the_stream_cannot_seek_it_starts_again_at_the_target()
    {
        // A transcode arrives from the front only; seeking past what arrived used to end the track (#154).
        _sut.Load(Restartable("a"));
        _sut.CanSeek = false;
        _sut.Calls.Clear();

        _sut.Seek(TimeSpan.FromSeconds(60));
        _sut.SimulatePosition(TimeSpan.FromSeconds(5), TimeSpan.Zero);
        _dispatcher.RunAll();

        _sut.Calls.Should().Equal("restart https://server/Audio/a?start=60");
        _sut.Position.Should().Be(TimeSpan.FromSeconds(65));
    }

    [Fact]
    public void After_a_restart_seeks_are_relative_to_where_the_stream_starts()
    {
        _sut.Load(Restartable("a"));
        _sut.CanSeek = false;
        _sut.Seek(TimeSpan.FromSeconds(60));
        _sut.CanSeek = true;
        _sut.Calls.Clear();

        _sut.Seek(TimeSpan.FromSeconds(90));
        _sut.Seek(TimeSpan.FromSeconds(30));

        // Before the start of the restarted stream there is nothing to seek to: it starts again.
        _sut.Calls.Should().Equal("seek 30", "restart https://server/Audio/a?start=30");
        _sut.Position.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Without_a_url_for_a_position_the_player_seeks_anyway()
    {
        _sut.Load(Source("a"));
        _sut.CanSeek = false;
        _sut.Calls.Clear();

        _sut.Seek(TimeSpan.FromSeconds(60));

        _sut.Calls.Should().Equal("seek 60");
    }

    [Fact]
    public void A_new_track_counts_from_its_start_again()
    {
        _sut.Load(Restartable("a"));
        _sut.CanSeek = false;
        _sut.Seek(TimeSpan.FromSeconds(60));

        _sut.Load(Source("b"));
        _sut.SimulatePosition(TimeSpan.FromSeconds(5), TimeSpan.Zero);
        _dispatcher.RunAll();

        _sut.Position.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void The_track_length_from_the_server_wins_over_the_player()
    {
        // A transcoded stream has no known length while it arrives; the player reports nonsense (#153).
        _sut.Load(Source("a", seconds: 200));
        _events.Clear();

        _sut.SimulatePosition(TimeSpan.FromSeconds(1), TimeSpan.FromHours(4778));
        _dispatcher.RunAll();

        _sut.Duration.Should().Be(TimeSpan.FromSeconds(200));
        _events.Should().Equal("position 1/200");
    }

    [Fact]
    public void Position_reports_after_stop_are_ignored()
    {
        _sut.Load(Source("a"));
        _sut.SimulatePosition(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(200));
        _sut.Stop();
        _events.Clear();

        _dispatcher.RunAll();

        _events.Should().BeEmpty();
        _sut.Position.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Failure_goes_idle_and_reports_the_source()
    {
        _sut.Load(Source("a"));
        _sut.SimulateState(PlaybackState.Playing);
        _sut.SimulateFailure("MediaSourceNotSupported: nope", new InvalidOperationException());
        _dispatcher.RunAll();

        _sut.State.Should().Be(PlaybackState.Idle);
        _events.Should().EndWith(["state Idle", "failed a MediaSourceNotSupported: nope"]);
    }

    [Fact]
    public void Ended_and_failed_of_a_replaced_source_are_ignored()
    {
        var old = Source("old");
        _sut.Load(old);
        _sut.Load(Source("new"));
        _events.Clear();

        _sut.SimulateEnded(old);
        _sut.SimulateFailure(old);
        _dispatcher.RunAll();

        _events.Should().BeEmpty();
    }

    [Fact]
    public void The_fake_defaults_to_an_immediate_dispatcher()
    {
        var engine = new FakePlaybackEngine();
        var raised = 0;
        engine.StateChanged += (_, _) => raised++;
        engine.Load(Source("a"));

        engine.SimulateState(PlaybackState.Playing);

        raised.Should().Be(1);
    }

    [Fact]
    public void Works_without_subscribers()
    {
        var engine = new FakePlaybackEngine();
        var source = Source("a");

        engine.Load(source);
        engine.Seek(TimeSpan.FromSeconds(10));
        engine.SimulateState(PlaybackState.Playing);
        engine.SimulatePosition(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(200));
        engine.SimulateEnded();
        engine.SimulateFailure();
        engine.Load(source);
        engine.Stop();

        engine.State.Should().Be(PlaybackState.Idle);
        engine.Current.Should().BeNull();
    }

    [Fact]
    public void A_source_never_shows_its_url_when_logged()
    {
        var source = new PlaybackSource("7", new Uri("https://server/Audio/x/universal?ApiKey=secret-token"), new Track { Id = "x", Name = "One More Time" });

        source.ToString().Should().Be("7 (One More Time)").And.NotContain("secret-token");
    }

    [Fact]
    public void SetNext_without_a_source_is_ignored()
    {
        _sut.SetNext(Source("b"));

        _sut.Next.Should().BeNull();
        _sut.SetNextCount.Should().Be(0);
    }

    [Fact]
    public void SetNext_queues_and_replaces_the_next_source_once()
    {
        _sut.Load(Source("a"));
        var b = Source("b");

        _sut.SetNext(b);
        _sut.SetNext(b);
        _sut.SetNext(null);

        _sut.Next.Should().BeNull();
        _sut.SetNextCount.Should().Be(2);
    }

    [Fact]
    public void Load_and_stop_clear_the_next_source()
    {
        _sut.Load(Source("a"));
        _sut.SetNext(Source("b"));
        _sut.Load(Source("c"));
        _sut.Next.Should().BeNull();

        _sut.SetNext(Source("d"));
        _sut.Stop();

        _sut.Next.Should().BeNull();
    }

    [Fact]
    public void Advancing_makes_the_next_source_current_on_the_ui_thread()
    {
        _sut.Load(Source("a"));
        var b = Source("b", seconds: 120);
        _sut.SetNext(b);
        _events.Clear();

        _sut.SimulateAdvance();
        _sut.Current!.Id.Should().Be("a");
        _dispatcher.RunAll();

        _sut.Current.Should().BeSameAs(b);
        _sut.Next.Should().BeNull();
        _sut.Duration.Should().Be(TimeSpan.FromSeconds(120));
        _events.Should().Equal("current b", "position 0/120");
    }

    [Fact]
    public void Advancing_to_a_source_replaced_a_moment_ago_still_follows_the_player()
    {
        _sut.Load(Source("a"));
        var b = Source("b");
        var c = Source("c");
        _sut.SetNext(b);
        _sut.SetNext(c);

        _sut.SimulateAdvance(b);
        _dispatcher.RunAll();

        _sut.Current.Should().BeSameAs(b);
        _sut.Next.Should().BeSameAs(c, "the player still has it queued after b");
    }

    [Fact]
    public void Advancing_after_a_load_or_stop_is_ignored()
    {
        _sut.Load(Source("a"));
        var b = Source("b");
        _sut.SetNext(b);
        _sut.SimulateAdvance(b);
        _sut.Load(Source("c"));
        _events.Clear();

        _dispatcher.RunAll();

        _sut.Current!.Id.Should().Be("c");
        _events.Should().BeEmpty();

        _sut.SetNext(b);
        _sut.SimulateAdvance(b);
        _sut.Stop();
        _dispatcher.RunAll();

        _sut.Current.Should().BeNull();
    }

    [Fact]
    public void Advancing_to_an_unknown_source_is_ignored()
    {
        _sut.Load(Source("a"));
        _events.Clear();

        _sut.SimulateAdvance(Source("x"));
        _dispatcher.RunAll();

        _sut.Current!.Id.Should().Be("a");
        _events.Should().BeEmpty();
    }

    [Fact]
    public void A_next_source_that_fails_to_buffer_is_dropped()
    {
        _sut.Load(Source("a"));
        _sut.SetNext(Source("b"));

        _sut.SimulateNextFailed();
        _dispatcher.RunAll();

        _sut.Next.Should().BeNull();
        _sut.SetNextCount.Should().Be(2, "the engine is told to drop it");
        _sut.Current!.Id.Should().Be("a");
    }

    [Fact]
    public void A_buffer_failure_of_a_replaced_next_source_is_ignored()
    {
        _sut.Load(Source("a"));
        _sut.SetNext(Source("b"));
        _sut.SimulateNextFailed();
        var c = Source("c");
        _sut.SetNext(c);

        _dispatcher.RunAll();

        _sut.Next.Should().BeSameAs(c);
    }

    [Fact]
    public void A_source_gain_turns_the_player_down_on_load_and_back_up_for_the_next_source()
    {
        _sut.Volume = 0.8;
        _sut.Calls.Clear();

        _sut.Load(Source("a") with { Gain = 0.5 });
        _sut.Load(Source("b"));

        _sut.Volume.Should().Be(0.8, "the user's volume is unchanged");
        _sut.Calls.Should().Equal("volume 0.4", "load a autoPlay=True", "volume 0.8", "load b autoPlay=True");
    }

    [Fact]
    public void Volume_changes_keep_the_source_gain()
    {
        _sut.Load(Source("a") with { Gain = 0.5 });
        _sut.Calls.Clear();

        _sut.Volume = 0.6;

        _sut.Calls.Should().Equal("volume 0.3");
    }

    [Fact]
    public void A_gapless_transition_applies_the_gain_of_the_next_source()
    {
        _sut.Load(Source("a"));
        _sut.SetNext(Source("b") with { Gain = 0.25 });
        _sut.Calls.Clear();

        _sut.SimulateAdvance();
        _dispatcher.RunAll();

        _sut.Calls.Should().Equal("volume 0.25");
    }

    [Theory]
    [InlineData(double.NaN, "")]
    [InlineData(3.0, "")]
    [InlineData(-1.0, "volume 0")]
    public void Broken_gains_are_clamped_or_ignored(double gain, string expected)
    {
        _sut.Load(Source("a") with { Gain = gain });

        _sut.Calls.Where(c => c.StartsWith("volume", StringComparison.Ordinal)).Should().Equal(expected.Length == 0 ? Array.Empty<string>() : new[] { expected });
    }
}
