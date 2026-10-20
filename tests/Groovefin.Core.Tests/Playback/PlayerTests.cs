using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ReturnsExtensions;

namespace Groovefin.Core.Tests.Playback;

public sealed class PlayerTests : IDisposable
{
    private static readonly Uri Server = new("http://192.168.1.10:8096/");

    private readonly QueueManager _queue = new(new Random(1));
    private readonly FakePlaybackEngine _engine = new();
    private readonly IStreamUrlProvider _streams = Substitute.For<IStreamUrlProvider>();
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly InMemorySettingsService _settings = new();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly Player _sut;

    public PlayerTests()
    {
        _client.Connection.Returns(new ServerConnection(Server, "user", "token"));
        _streams.GetStreamUrl(Arg.Any<string>(), Arg.Any<StreamOptions>())
            .Returns(call => new Uri($"http://stream/{call.Arg<string>()}"));
        _sut = new Player(_queue, _engine, _streams, _client, _settings, _errors, NullLogger<Player>.Instance, new Random(7));
    }

    public void Dispose() => _sut.Dispose();

    private static Track[] Tracks(params string[] ids) => ids.Select(id => new Track { Id = id, Name = id }).ToArray();

    private string? Playing => _engine.Current?.Track.Id;

    [Fact]
    public void The_crossfade_setting_reaches_the_engine()
    {
        _engine.Crossfade.Should().Be(TimeSpan.Zero);

        _settings.Update(s => s with { Playback = s.Playback with { CrossfadeSeconds = 6 } });
        _engine.Crossfade.Should().Be(TimeSpan.FromSeconds(6));

        _settings.Update(s => s with { Playback = s.Playback with { CrossfadeSeconds = -3 } });
        _engine.Crossfade.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_crossfade_moves_the_queue_on_like_a_gapless_transition()
    {
        // A fake clock: the fade timer must not run on its own during the test.
        var queue = new QueueManager(new Random(1));
        var engine = new FakePlaybackEngine(time: new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        _settings.Update(s => s with { Playback = s.Playback with { CrossfadeSeconds = 4 } });
        using var player = new Player(queue, engine, _streams, _client, _settings, _errors, NullLogger<Player>.Instance, new Random(7));
        player.Play([new Track { Id = "a", Name = "a", AlbumId = "x", Duration = TimeSpan.FromSeconds(200) },
            new Track { Id = "b", Name = "b", AlbumId = "y", Duration = TimeSpan.FromSeconds(200) }]);
        engine.SimulateState(PlaybackState.Playing);

        engine.SimulatePosition(TimeSpan.FromSeconds(197), TimeSpan.Zero);

        engine.Calls.Should().Contain($"crossfade {queue.Current!.Id}");
        engine.Current!.Track.Id.Should().Be("b");
        engine.Calls.Count(c => c.StartsWith("load", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void Play_queues_the_tracks_and_loads_the_chosen_one_with_local_stream_options()
    {
        _sut.Play(Tracks("a", "b", "c"), startIndex: 1);

        Playing.Should().Be("b");
        _engine.Current!.Id.Should().Be(_queue.Current!.Id);
        _engine.Current.Url.Should().Be(new Uri("http://stream/b"));
        _engine.Calls.Should().Equal($"load {_queue.Current.Id} autoPlay=True");
        _streams.Received(1).GetStreamUrl("b", new StreamOptions(null, TranscodingCodec.Aac));
    }

    [Fact]
    public void Remote_servers_get_the_remote_bitrate_limit()
    {
        _client.Connection.Returns(new ServerConnection(new Uri("https://media.example.com/"), "user", "token"));

        _sut.Play(Tracks("a"));

        _streams.Received(1).GetStreamUrl("a", new StreamOptions(320, TranscodingCodec.Aac));
    }

    [Fact]
    public void A_source_can_build_its_stream_from_a_later_point()
    {
        _client.Connection.Returns(new ServerConnection(new Uri("https://media.example.com/"), "user", "token"));
        _sut.Play(Tracks("a"));

        _engine.Current!.UrlAt!(TimeSpan.FromSeconds(60));

        _streams.Received(1).GetStreamUrl("a", new StreamOptions(320, TranscodingCodec.Aac) { StartPosition = TimeSpan.FromSeconds(60) });
    }

    [Fact]
    public void Playing_nothing_changes_nothing()
    {
        _sut.Play([]);
        _sut.Shuffle([]);

        _engine.Calls.Should().BeEmpty();
        _queue.Items.Should().BeEmpty();
    }

    [Fact]
    public void Null_tracks_are_rejected()
    {
        _sut.Invoking(p => p.Play(null!)).Should().Throw<ArgumentNullException>();
        _sut.Invoking(p => p.Shuffle(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Shuffle_turns_shuffle_on_and_plays()
    {
        _sut.Shuffle(Tracks("a", "b", "c", "d"));

        _queue.Shuffle.Should().BeTrue();
        _queue.CurrentIndex.Should().Be(0);
        Playing.Should().Be(_queue.Current!.Track.Id);
    }

    [Fact]
    public void PlayNext_and_AddToQueue_change_the_queue_but_not_the_playing_track()
    {
        _sut.Play(Tracks("a", "b"));
        _engine.Calls.Clear();

        _sut.PlayNext(Tracks("x"));
        _sut.AddToQueue(Tracks("z"));

        _queue.Items.Select(i => i.Track.Id).Should().Equal("a", "x", "b", "z");
        _engine.Calls.Should().BeEmpty();
        Playing.Should().Be("a");
    }

    [Fact]
    public void InsertIntoQueue_inserts_among_the_upcoming_tracks_and_updates_the_next_one()
    {
        _sut.Play(Tracks("a", "b"));
        _engine.Calls.Clear();

        _sut.InsertIntoQueue(Tracks("x"), 0);

        _queue.Items.Select(i => i.Track.Id).Should().Equal("a", "x", "b");
        _engine.Calls.Should().BeEmpty();
        _engine.Next!.Track.Id.Should().Be("x");
        Playing.Should().Be("a");
    }

    [Fact]
    public void Resume_loads_the_current_entry_paused_at_the_position()
    {
        _queue.PlayNow(Tracks("a", "b"), 1);

        _sut.Resume(TimeSpan.FromSeconds(42));

        Playing.Should().Be("b");
        _engine.Calls.Should().Equal($"load {_queue.Current!.Id} autoPlay=False");
        _engine.Current!.StartPosition.Should().Be(TimeSpan.FromSeconds(42));
        _engine.Position.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public void A_track_that_ends_is_followed_by_the_next_one()
    {
        _sut.Play(Tracks("a", "b"));

        _engine.SimulateEnded();

        Playing.Should().Be("b");
    }

    [Fact]
    public void The_end_of_the_queue_stops_playback_and_play_starts_the_last_entry_again()
    {
        _sut.Play(Tracks("a"));

        _engine.SimulateEnded();

        _engine.Current.Should().BeNull();
        _engine.Calls[^1].Should().Be("stop");

        _sut.TogglePlayPause();

        Playing.Should().Be("a");
    }

    [Fact]
    public void Toggle_pauses_while_playing_and_resumes_while_paused()
    {
        _sut.Play(Tracks("a"));
        _engine.SimulateState(PlaybackState.Playing);

        _sut.TogglePlayPause();
        _engine.SimulateState(PlaybackState.Paused);
        _sut.TogglePlayPause();

        _engine.Calls.Should().EndWith(["pause", "play"]);
    }

    [Fact]
    public void Toggle_with_an_empty_queue_does_nothing()
    {
        _sut.TogglePlayPause();

        _engine.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Next_moves_on_and_stops_at_the_end()
    {
        _sut.Play(Tracks("a", "b"));

        _sut.Next();
        Playing.Should().Be("b");

        _sut.Next();
        _engine.Current.Should().BeNull();
    }

    [Fact]
    public void Previous_restarts_a_track_that_has_played_for_a_while()
    {
        _sut.Play(Tracks("a", "b"));
        _sut.Next();
        _engine.Seek(TimeSpan.FromSeconds(10));

        _sut.Previous();

        Playing.Should().Be("b");
        _engine.Position.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Previous_early_in_a_track_goes_back()
    {
        _sut.Play(Tracks("a", "b"));
        _sut.Next();

        _sut.Previous();

        Playing.Should().Be("a");
    }

    [Fact]
    public void Previous_on_the_first_track_restarts_it()
    {
        _sut.Play(Tracks("a", "b"));
        _engine.Calls.Clear();

        _sut.Previous();

        Playing.Should().Be("a");
        _engine.Calls.Should().Equal("seek 0");
    }

    [Fact]
    public void Previous_with_an_empty_queue_does_nothing()
    {
        _sut.Previous();

        _engine.Calls.Should().BeEmpty();
        _engine.Current.Should().BeNull();
    }

    [Fact]
    public void JumpTo_plays_the_entry()
    {
        _sut.Play(Tracks("a", "b", "c"));

        _sut.JumpTo(_queue.Items[2].Id);
        Playing.Should().Be("c");

        _engine.Calls.Clear();
        _sut.JumpTo("missing");
        _engine.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_failing_track_is_reported_and_skipped()
    {
        _sut.Play(Tracks("a", "b"));

        _engine.SimulateFailure("MediaSourceNotSupported");

        Playing.Should().Be("b");
        _errors.Received(1).Report(Arg.Is<Exception>(e => e is PlaybackException && e.Message == "MediaSourceNotSupported"), Operations.Playback);
    }

    [Fact]
    public void A_failure_is_reported_with_the_engine_error_inside()
    {
        _sut.Play(Tracks("a", "b"));
        var denied = new UnauthorizedAccessException();

        _engine.SimulateFailure("SourceNotSupportedError", denied);

        _errors.Received(1).Report(Arg.Is<PlaybackException>(e => e.InnerException == denied), Operations.Playback);
    }

    [Fact]
    public void Playback_stops_after_too_many_failures_in_a_row()
    {
        _sut.Play(Tracks("a", "b", "c", "d", "e"));

        for (var i = 0; i < Player.MaxConsecutiveFailures; i++)
        {
            _engine.SimulateFailure(exception: new InvalidOperationException("boom"));
        }

        _engine.Current.Should().BeNull();
        _errors.Received(Player.MaxConsecutiveFailures).Report(Arg.Is<PlaybackException>(e => e.InnerException is InvalidOperationException), Operations.Playback);
    }

    [Fact]
    public void A_track_that_plays_resets_the_failure_count()
    {
        _sut.Play(Tracks("a", "b", "c", "d", "e"));

        _engine.SimulateFailure();
        _engine.SimulateFailure();
        _engine.SimulateEnded();
        _engine.SimulateFailure();

        Playing.Should().Be("e");
    }

    [Fact]
    public void Without_a_connection_nothing_plays_and_the_error_is_shown()
    {
        _client.Connection.ReturnsNull();

        _sut.Play(Tracks("a"));

        _engine.Current.Should().BeNull();
        _errors.Received(1).Report(Arg.Any<InvalidOperationException>(), Operations.Playback);
    }

    [Fact]
    public void Removing_the_playing_entry_elsewhere_plays_the_next_one()
    {
        _sut.Play(Tracks("a", "b"));
        _engine.SimulateState(PlaybackState.Playing);

        _queue.Remove(_queue.Current!.Id);

        Playing.Should().Be("b");
        _engine.Calls[^1].Should().EndWith("autoPlay=True");
    }

    [Fact]
    public void Removing_the_paused_entry_elsewhere_loads_the_next_one_paused()
    {
        _sut.Play(Tracks("a", "b"));
        _engine.SimulateState(PlaybackState.Paused);

        _queue.Remove(_queue.Current!.Id);

        _engine.Calls[^1].Should().EndWith("autoPlay=False");
    }

    [Fact]
    public void Clearing_the_queue_elsewhere_stops_playback()
    {
        _sut.Play(Tracks("a", "b"));

        _queue.Clear();

        _engine.Current.Should().BeNull();
    }

    [Fact]
    public void Queue_changes_that_keep_the_playing_entry_or_happen_while_idle_change_nothing()
    {
        _sut.Play(Tracks("a", "b"));
        _engine.Calls.Clear();

        _queue.AddLast(Tracks("c"));
        _engine.Stop();
        _engine.Calls.Clear();
        _queue.JumpTo(_queue.Items[1].Id);

        _engine.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_detaches_from_the_engine_and_the_queue()
    {
        _sut.Play(Tracks("a", "b"));
        _sut.Dispose();

        _engine.SimulateEnded();

        Playing.Should().Be("a");
    }

    [Fact]
    public void The_entry_after_the_playing_one_is_queued_for_gapless_playback()
    {
        _sut.Play(Tracks("a", "b", "c"));

        _engine.Next!.Id.Should().Be(_queue.Items[1].Id);
        _engine.Next.Track.Id.Should().Be("b");
        _engine.Next.Url.Should().Be(new Uri("http://stream/b"));
    }

    [Fact]
    public void The_last_entry_has_nothing_queued_after_it()
    {
        _sut.Play(Tracks("a"));

        _engine.Next.Should().BeNull();
    }

    [Fact]
    public void A_gapless_transition_moves_the_queue_without_reloading()
    {
        _sut.Play(Tracks("a", "b", "c"));
        _engine.Calls.Clear();

        _engine.SimulateAdvance();

        Playing.Should().Be("b");
        _queue.Current!.Track.Id.Should().Be("b");
        _engine.Calls.Should().BeEmpty();
        _engine.Next!.Track.Id.Should().Be("c");
    }

    [Fact]
    public void Repeat_one_queues_the_playing_entry_again()
    {
        _sut.Play(Tracks("a", "b"));

        _queue.Repeat = RepeatMode.One;
        _engine.Next!.Id.Should().Be(_queue.Current!.Id);
        _engine.SimulateAdvance();

        Playing.Should().Be("a");
        _engine.Next!.Id.Should().Be(_queue.Current!.Id);
    }

    [Fact]
    public void Queue_changes_update_the_queued_entry()
    {
        _sut.Play(Tracks("a", "b"));

        _sut.PlayNext(Tracks("c"));
        _engine.Next!.Track.Id.Should().Be("c");

        _queue.Remove(_queue.Items[1].Id);
        _engine.Next!.Track.Id.Should().Be("b");

        _queue.Remove(_queue.Items[1].Id);
        _engine.Next.Should().BeNull();

        _sut.AddToQueue(Tracks("d"));
        _engine.Next!.Track.Id.Should().Be("d");
    }

    [Fact]
    public void An_unchanged_next_entry_is_not_queued_again()
    {
        _sut.Play(Tracks("a", "b"));
        var queued = _engine.Next;

        _sut.AddToQueue(Tracks("c"));

        _engine.Next.Should().BeSameAs(queued);
    }

    [Fact]
    public void A_transition_to_an_entry_removed_meanwhile_plays_what_the_queue_says()
    {
        _sut.Play(Tracks("a", "b", "c"));
        var b = _engine.Next!;
        _queue.Remove(b.Id);

        _engine.SimulateAdvance(b);

        Playing.Should().Be("c");
        _engine.Calls[^1].Should().Be($"load {_queue.Current!.Id} autoPlay=True");
    }

    [Fact]
    public void When_the_next_stream_url_cannot_be_built_nothing_is_queued()
    {
        _sut.Play(Tracks("a", "b"));
        _client.Connection.ReturnsNull();

        _sut.PlayNext(Tracks("c"));

        _engine.Next.Should().BeNull();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
        Playing.Should().Be("a");
    }

    [Fact]
    public void A_gapless_transition_resets_the_failure_count()
    {
        _sut.Play(Tracks("a", "b", "c", "d", "e"));

        _engine.SimulateFailure();
        _engine.SimulateFailure();
        _engine.SimulateAdvance();
        _engine.SimulateFailure();

        Playing.Should().Be("e");
    }

    [Fact]
    public void Dispose_also_stops_following_gapless_transitions()
    {
        _sut.Play(Tracks("a", "b"));
        _sut.Dispose();

        _engine.SimulateAdvance();

        _queue.Current!.Track.Id.Should().Be("a");
    }

    [Theory]
    [InlineData(NormalizationMode.Off, 1.0)]
    [InlineData(NormalizationMode.Track, 0.5)]
    [InlineData(NormalizationMode.Album, 0.25)]
    public void Sources_carry_the_normalization_gain_of_the_setting(NormalizationMode mode, double expected)
    {
        _settings.Update(s => s with { Playback = new PlaybackSettings { Normalization = mode } });
        var halfAsLoud = 20 * Math.Log10(0.5);
        var quarterAsLoud = 20 * Math.Log10(0.25);
        var tracks = new[]
        {
            new Track { Id = "a", Name = "a", NormalizationGain = halfAsLoud, AlbumNormalizationGain = quarterAsLoud },
            new Track { Id = "b", Name = "b", NormalizationGain = halfAsLoud, AlbumNormalizationGain = quarterAsLoud },
        };

        _sut.Play(tracks);

        _engine.Current!.Gain.Should().BeApproximately(expected, 1e-9);
        _engine.Next!.Gain.Should().BeApproximately(expected, 1e-9);
    }
}
