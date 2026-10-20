using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Playback;

public sealed class CrossfadeTests
{
    private static readonly TimeSpan Fade = TimeSpan.FromSeconds(4);

    private readonly FakeTimeProvider _time = new();
    private readonly QueuedDispatcher _dispatcher = new();
    private readonly FakePlaybackEngine _sut;
    private readonly List<string> _events = [];

    public CrossfadeTests()
    {
        _sut = new FakePlaybackEngine(_dispatcher, _time) { Crossfade = Fade };
        _sut.CurrentChanged += (_, _) => _events.Add($"current {_sut.Current?.Id ?? "none"}");
        _sut.Ended += (_, _) => _events.Add("ended");
    }

    private static PlaybackSource Source(string id, string? album = null, int seconds = 200, double gain = 1) =>
        new(id, new Uri($"https://server/Audio/{id}"), new Track { Id = id, Name = id, AlbumId = album ?? $"album-{id}", Duration = TimeSpan.FromSeconds(seconds) }, gain);

    /// <summary>Plays <paramref name="current"/> with <paramref name="next"/> queued, at <paramref name="seconds"/>.</summary>
    private void PlayAt(PlaybackSource current, PlaybackSource? next, double seconds)
    {
        _sut.Load(current);
        _sut.SetNext(next);
        _sut.SimulateState(PlaybackState.Playing);
        _dispatcher.RunAll();
        Report(seconds);
    }

    private void Report(double seconds)
    {
        _time.Advance(PlaybackEngineBase.PositionInterval);
        _sut.SimulatePosition(TimeSpan.FromSeconds(seconds), TimeSpan.Zero);
        _dispatcher.RunAll();
    }

    /// <summary>Lets the fade timer run for <paramref name="elapsed"/>.</summary>
    private void Wait(TimeSpan elapsed)
    {
        for (var t = TimeSpan.Zero; t < elapsed; t += PlaybackEngineBase.FadeStep)
        {
            _time.Advance(PlaybackEngineBase.FadeStep);
            _dispatcher.RunAll();
        }
    }

    private IEnumerable<string> Fading => _sut.Calls.Where(c => c.StartsWith("crossfade", StringComparison.Ordinal) || c == "end fade");

    [Fact]
    public void Off_by_default_the_next_track_follows_gaplessly()
    {
        var sut = new FakePlaybackEngine(_dispatcher, _time);

        sut.Crossfade.Should().Be(TimeSpan.Zero);
        sut.Load(Source("a"));
        sut.SetNext(Source("b"));
        sut.SimulateState(PlaybackState.Playing);
        sut.SimulatePosition(TimeSpan.FromSeconds(199), TimeSpan.Zero);
        _dispatcher.RunAll();

        sut.Calls.Should().NotContain(c => c.StartsWith("crossfade", StringComparison.Ordinal));
        sut.Current!.Id.Should().Be("a");
    }

    [Fact]
    public void The_duration_is_limited()
    {
        _sut.Crossfade = TimeSpan.FromSeconds(60);
        _sut.Crossfade.Should().Be(PlaybackEngineBase.MaxCrossfade);

        _sut.Crossfade = TimeSpan.FromSeconds(-1);
        _sut.Crossfade.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Before_the_fade_the_current_track_plays_on()
    {
        PlayAt(Source("a"), Source("b"), 195.5);

        Fading.Should().BeEmpty();
        _sut.Current!.Id.Should().Be("a");
        _sut.IsFading.Should().BeFalse();
    }

    [Fact]
    public void At_the_fade_the_next_track_starts_on_the_second_player_and_becomes_current()
    {
        PlayAt(Source("a"), Source("b"), 196);

        Fading.Should().Equal("crossfade b");
        _sut.IsFading.Should().BeTrue();
        _sut.Current!.Id.Should().Be("b");
        _sut.Next.Should().BeNull();
        _sut.Position.Should().Be(TimeSpan.Zero);
        _sut.Duration.Should().Be(TimeSpan.FromSeconds(200));
        _events.Should().EndWith("current b");
    }

    [Fact]
    public void The_old_player_gives_up_its_gapless_next_track_before_the_fade()
    {
        PlayAt(Source("a"), Source("b"), 150);
        var setNext = _sut.SetNextCount;

        Report(196);

        // SetNextCore(null) on the old player, so it does not move on to "b" as well.
        _sut.SetNextCount.Should().Be(setNext + 1);
    }

    [Fact]
    public void Over_the_fade_the_volumes_cross_with_equal_power_then_the_old_player_stops()
    {
        PlayAt(Source("a"), Source("b"), 196);
        _sut.Calls.Clear();

        Wait(Fade / 2);

        var volumes = _sut.Calls.Where(c => c.StartsWith("volume ", StringComparison.Ordinal)).Select(c => double.Parse(c[7..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var fading = _sut.Calls.Where(c => c.StartsWith("fading volume ", StringComparison.Ordinal)).Select(c => double.Parse(c[14..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        volumes.Should().BeInAscendingOrder();
        fading.Should().BeInDescendingOrder();

        // Halfway: both at sin(45°) = cos(45°), so the power adds up to one track.
        volumes[^1].Should().BeApproximately(Math.Sqrt(0.5), 0.03);
        fading[^1].Should().BeApproximately(Math.Sqrt(0.5), 0.03);

        Wait(Fade / 2);

        _sut.IsFading.Should().BeFalse();
        _sut.Calls.Should().Contain("end fade");
        _sut.Calls[^1].Should().Be("volume 1");
    }

    [Fact]
    public void The_fade_respects_the_volume_and_the_gains_of_both_tracks()
    {
        _sut.Volume = 0.5;
        PlayAt(Source("a", gain: 0.8), Source("b", gain: 0.5), 196);

        Wait(Fade + PlaybackEngineBase.FadeStep);

        _sut.Calls.Should().Contain(c => c.StartsWith("fading volume 0.4", StringComparison.Ordinal) || c.StartsWith("fading volume 0.39", StringComparison.Ordinal));
        _sut.Calls.Where(c => c.StartsWith("volume ", StringComparison.Ordinal)).Last().Should().Be("volume 0.25");
    }

    [Fact]
    public void Tracks_of_the_same_album_stay_gapless()
    {
        PlayAt(Source("a", album: "x"), Source("b", album: "x"), 199);

        Fading.Should().BeEmpty();
        _sut.Current!.Id.Should().Be("a");
        _sut.Next!.Id.Should().Be("b");
    }

    [Fact]
    public void Tracks_without_an_album_fade()
    {
        PlaybackEngineBase.FadesInto(Source("a") with { Track = new Track { Id = "a", Name = "a" } }, Source("b")).Should().BeTrue();
        PlaybackEngineBase.FadesInto(Source("a", album: "x"), Source("b", album: "y")).Should().BeTrue();
        PlaybackEngineBase.FadesInto(Source("a", album: "x"), Source("b", album: "x")).Should().BeFalse();
    }

    [Theory]
    [InlineData(7, 200)]
    [InlineData(200, 7)]
    public void Short_tracks_stay_gapless(int current, int next)
    {
        PlayAt(Source("a", seconds: current), Source("b", seconds: next), current - 1);

        Fading.Should().BeEmpty();
    }

    [Fact]
    public void Without_a_queued_next_track_the_track_just_ends()
    {
        PlayAt(Source("a"), null, 199);

        Fading.Should().BeEmpty();
        _sut.SimulateEnded();
        _dispatcher.RunAll();
        _events.Should().EndWith("ended");
    }

    [Fact]
    public void Paused_near_the_end_it_does_not_fade()
    {
        PlayAt(Source("a"), Source("b"), 150);
        _sut.SimulateState(PlaybackState.Paused);
        _dispatcher.RunAll();

        Report(197);

        Fading.Should().BeEmpty();
    }

    [Fact]
    public void An_engine_without_a_second_player_stays_gapless()
    {
        _sut.CanCrossfade = false;

        PlayAt(Source("a"), Source("b"), 197);

        Fading.Should().BeEmpty();
    }

    [Fact]
    public void A_seek_into_the_last_seconds_fades_in_the_time_left()
    {
        PlayAt(Source("a"), Source("b"), 100);

        _sut.Seek(TimeSpan.FromSeconds(199));
        Report(199);
        Wait(TimeSpan.FromSeconds(1.1));

        _sut.Calls.Should().Contain("end fade");
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("seek")]
    [InlineData("load")]
    [InlineData("stop")]
    public void Pause_seek_load_and_stop_end_the_fade_at_once(string action)
    {
        PlayAt(Source("a"), Source("b"), 196);
        Wait(TimeSpan.FromSeconds(1));

        switch (action)
        {
            case "pause": _sut.Pause(); break;
            case "seek": _sut.Seek(TimeSpan.FromSeconds(30)); break;
            case "load": _sut.Load(Source("c")); break;
            default: _sut.Stop(); break;
        }

        _sut.IsFading.Should().BeFalse();
        _sut.Calls.Count(c => c == "end fade").Should().Be(1);

        // The timer is gone: nothing more happens.
        var calls = _sut.Calls.Count;
        Wait(Fade);
        _sut.Calls.Should().HaveCount(calls);
    }

    [Fact]
    public void After_the_fade_the_next_track_can_be_queued_and_fade_again()
    {
        PlayAt(Source("a"), Source("b"), 196);
        Wait(Fade + PlaybackEngineBase.FadeStep);

        _sut.SetNext(Source("c"));
        Report(196);

        Fading.Should().Equal("crossfade b", "end fade", "crossfade c");
        _sut.Current!.Id.Should().Be("c");
    }

    [Fact]
    public void The_old_track_ending_during_the_fade_is_ignored()
    {
        var a = Source("a");
        PlayAt(a, Source("b"), 196);
        _events.Clear();

        _sut.SimulateEnded(a);
        _dispatcher.RunAll();

        _events.Should().BeEmpty();
    }
}
