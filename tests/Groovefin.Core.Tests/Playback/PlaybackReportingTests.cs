using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Playback;

public sealed class PlaybackReportingTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakePlaybackEngine _engine;
    private readonly IPlaybackReporter _reporter = Substitute.For<IPlaybackReporter>();
    private readonly FakeLogger<PlaybackReporting> _logger = new();
    private readonly PrivacyMode _privacy = new();
    private readonly PlaybackReporting _sut;
    private readonly List<string> _reports = [];

    public PlaybackReportingTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _reporter.ReportStartAsync(Arg.Do<PlaybackReport>(r => _reports.Add($"start {Describe(r)}")), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _reporter.ReportProgressAsync(Arg.Do<PlaybackReport>(r => _reports.Add($"progress {Describe(r)}")), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _reporter.ReportStoppedAsync(Arg.Do<PlaybackReport>(r => _reports.Add($"stopped {Describe(r)}")), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _sut = new PlaybackReporting(_engine, _reporter, _privacy, new ImmediateDispatcher(), _time, _logger);
    }

    public void Dispose() => _sut.Dispose();

    private static string Describe(PlaybackReport r) => $"{r.ItemId} {r.Position.TotalSeconds}s{(r.IsPaused ? " paused" : string.Empty)}";

    private static PlaybackSource Source(string trackId) =>
        new($"q-{trackId}", new Uri($"http://stream/{trackId}"), new Track { Id = trackId, Name = trackId, Duration = TimeSpan.FromSeconds(200) });

    private async Task<List<string>> ReportsAsync()
    {
        await _sut.FlushAsync();
        return _reports;
    }

    /// <summary>Plays for <paramref name="seconds"/>: the clock and the engine position move together.</summary>
    private void PlayFor(double seconds)
    {
        var step = TimeSpan.FromSeconds(1);
        for (var elapsed = 0.0; elapsed < seconds; elapsed++)
        {
            _engine.SimulatePosition(_engine.Position + step, _engine.Duration);
            _time.Advance(step);
        }
    }

    [Fact]
    public async Task Loading_a_track_reports_start_with_a_new_play_session()
    {
        _engine.Load(Source("a"));
        _engine.Load(Source("b"));
        await _sut.FlushAsync();

        var first = _reporter.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<PlaybackReport>().First();
        var last = _reporter.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<PlaybackReport>().Last();
        first.PlaySessionId.Should().NotBe(last.PlaySessionId).And.HaveLength(32);
        (await ReportsAsync()).Should().Equal("start a 0s", "stopped a 0s", "start b 0s");
    }

    [Fact]
    public async Task Progress_is_reported_every_ten_seconds_while_playing()
    {
        _engine.Load(Source("a"));
        _engine.SimulateState(PlaybackState.Playing);

        PlayFor(25);

        (await ReportsAsync()).Should().Equal("start a 0s", "progress a 10s", "progress a 20s");
    }

    [Fact]
    public async Task Pause_and_resume_are_reported_at_once_and_no_progress_while_paused()
    {
        _engine.Load(Source("a"));
        _engine.SimulateState(PlaybackState.Playing);
        PlayFor(4);

        _engine.SimulateState(PlaybackState.Paused);
        _time.Advance(TimeSpan.FromSeconds(30));
        _engine.SimulateState(PlaybackState.Playing);

        (await ReportsAsync()).Should().Equal("start a 0s", "progress a 4s paused", "progress a 4s");
    }

    [Fact]
    public async Task A_seek_is_reported_at_once()
    {
        _engine.Load(Source("a"));
        _engine.SimulateState(PlaybackState.Playing);
        PlayFor(2);

        _engine.Seek(TimeSpan.FromSeconds(120));
        PlayFor(1);

        (await ReportsAsync()).Should().Equal("start a 0s", "progress a 120s");
    }

    [Fact]
    public async Task A_track_that_ends_is_reported_stopped_at_its_length()
    {
        _engine.Load(Source("a"));
        _engine.SimulateState(PlaybackState.Playing);

        _engine.SimulateEnded();
        _engine.Load(Source("b"));

        (await ReportsAsync()).Should().Equal("start a 0s", "stopped a 200s", "start b 0s");
    }

    [Fact]
    public async Task Stopping_reports_the_last_position()
    {
        _engine.Load(Source("a"));
        _engine.SimulateState(PlaybackState.Playing);
        PlayFor(7);

        _engine.Stop();
        _time.Advance(TimeSpan.FromSeconds(30));

        (await ReportsAsync()).Should().Equal("start a 0s", "stopped a 7s");
    }

    [Fact]
    public async Task In_privacy_mode_nothing_is_reported()
    {
        _privacy.IsOn = true;

        _engine.Load(Source("a"));
        _engine.SimulateState(PlaybackState.Playing);
        PlayFor(25);
        _engine.SimulateState(PlaybackState.Paused);
        _engine.SimulateEnded();
        _engine.Load(Source("b"));
        _engine.Stop();

        (await ReportsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Turning_privacy_mode_on_ends_the_current_report_there()
    {
        _engine.Load(Source("a"));
        _engine.SimulateState(PlaybackState.Playing);
        PlayFor(7);

        _privacy.IsOn = true;
        PlayFor(25);
        _engine.Seek(TimeSpan.FromSeconds(120));
        _engine.Load(Source("b"));

        (await ReportsAsync()).Should().Equal("start a 0s", "stopped a 7s");
    }

    [Fact]
    public async Task Turning_privacy_mode_off_reports_again_from_the_next_track()
    {
        _privacy.IsOn = true;
        _engine.Load(Source("a"));
        _engine.SimulateState(PlaybackState.Playing);
        PlayFor(5);

        _privacy.IsOn = false;
        PlayFor(15);
        _engine.Load(Source("b"));

        (await ReportsAsync()).Should().Equal("start b 0s");
    }

    [Fact]
    public async Task Reports_carry_volume_and_mute()
    {
        _engine.Volume = 0.42;
        _engine.IsMuted = true;

        _engine.Load(Source("a"));
        await _sut.FlushAsync();

        await _reporter.Received(1).ReportStartAsync(
            Arg.Is<PlaybackReport>(r => r.Volume == 42 && r.IsMuted && !r.IsPaused),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failing_reports_are_logged_and_the_next_ones_still_go_out()
    {
        _reporter.ReportStartAsync(Arg.Any<PlaybackReport>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new JellyfinException("offline")));

        _engine.Load(Source("a"));
        _engine.Stop();
        await _sut.FlushAsync();

        await _reporter.Received(1).ReportStoppedAsync(Arg.Any<PlaybackReport>(), Arg.Any<CancellationToken>());
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task Reports_are_sent_in_order()
    {
        var slowStart = new TaskCompletionSource();
        _reporter.ReportStartAsync(Arg.Any<PlaybackReport>(), Arg.Any<CancellationToken>()).Returns(slowStart.Task);
        var stoppedBeforeStartFinished = false;
        _reporter.ReportStoppedAsync(Arg.Any<PlaybackReport>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stoppedBeforeStartFinished = !slowStart.Task.IsCompleted;
                return Task.CompletedTask;
            });

        _engine.Load(Source("a"));
        _engine.Stop();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        slowStart.SetResult();
        await _sut.FlushAsync();

        stoppedBeforeStartFinished.Should().BeFalse();
    }

    [Fact]
    public async Task Buffering_and_opening_states_do_not_report()
    {
        _engine.Load(Source("a"));

        _engine.SimulateState(PlaybackState.Opening);
        _engine.SimulateState(PlaybackState.Buffering);
        _engine.SimulateState(PlaybackState.Playing);

        (await ReportsAsync()).Should().Equal("start a 0s");
    }

    [Fact]
    public async Task Nothing_is_reported_without_a_track_and_after_dispose()
    {
        _engine.SimulateState(PlaybackState.Playing);
        _time.Advance(TimeSpan.FromMinutes(1));

        _sut.Dispose();
        _engine.Load(Source("a"));

        (await ReportsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_accepted_start_is_announced_and_a_failed_one_is_not()
    {
        var announced = new List<string>();
        _sut.StartReported += (_, id) => announced.Add(id);
        _reporter.ReportStartAsync(Arg.Is<PlaybackReport>(r => r.ItemId == "b"), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new JellyfinException("offline")));

        _engine.Load(Source("a"));
        _engine.Load(Source("b"));
        await _sut.FlushAsync();

        announced.Should().Equal("a");
    }
}
