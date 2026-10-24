using System.ComponentModel;
using System.Globalization;
using Groovefin.Core.Api;
using Groovefin.Core.Privacy;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Playback;

/// <summary>
/// Follows the engine and reports to the server: start when a track is loaded, progress every
/// <see cref="ProgressInterval"/> while playing and at once on pause, resume and seek, stopped when the
/// track ends, is replaced or playback stops. Reports go out one after another in order; failures are
/// logged and never touch playback. In privacy mode nothing is reported: turning it on reports the current
/// track stopped, and turning it off reports again from the next track (#234). Engine events and the
/// progress timer are handled on the UI thread. Create it once at startup and keep it alive.
/// </summary>
public sealed partial class PlaybackReporting : IDisposable
{
    /// <summary>How often progress is reported while playing.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(10);

    /// <summary>A position jump larger than this beyond the elapsed time counts as a seek.</summary>
    public static readonly TimeSpan SeekTolerance = TimeSpan.FromSeconds(3);

    private readonly IPlaybackEngine _engine;
    private readonly IPlaybackReporter _reporter;
    private readonly PrivacyMode _privacy;
    private readonly IDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaybackReporting> _logger;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private Task _queue = Task.CompletedTask;

    private Session? _session;

    public PlaybackReporting(
        IPlaybackEngine engine,
        IPlaybackReporter reporter,
        PrivacyMode privacy,
        IDispatcher dispatcher,
        TimeProvider time,
        ILogger<PlaybackReporting> logger)
    {
        _engine = engine;
        _reporter = reporter;
        _privacy = privacy;
        _dispatcher = dispatcher;
        _time = time;
        _logger = logger;
        _timer = time.CreateTimer(_ => _ = _dispatcher.EnqueueAsync(OnTimer), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _engine.CurrentChanged += OnCurrentChanged;
        _engine.StateChanged += OnStateChanged;
        _engine.PositionChanged += OnPositionChanged;
        _engine.Ended += OnEnded;
        _privacy.PropertyChanged += OnPrivacyChanged;
    }

    /// <summary>Waits until all reports queued so far were sent (tests, shutdown).</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _queue;
        }
    }

    public void Dispose()
    {
        _engine.CurrentChanged -= OnCurrentChanged;
        _engine.StateChanged -= OnStateChanged;
        _engine.PositionChanged -= OnPositionChanged;
        _engine.Ended -= OnEnded;
        _privacy.PropertyChanged -= OnPrivacyChanged;
        _timer.Dispose();
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        Stop();

        if (_engine.Current is { } source && !_privacy.IsOn)
        {
            _session = new Session(source.Track.Id, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture))
            {
                Position = TimeSpan.Zero,
                PositionAt = _time.GetTimestamp(),
                WasPaused = _engine.State == PlaybackState.Paused,
            };
            Send(_reporter.ReportStartAsync, Report(_session));
            UpdateTimer();
        }
    }

    /// <summary>Turned on: the current track's report ends here. Turned off: the next track is reported.</summary>
    private void OnPrivacyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrivacyMode.IsOn) && _privacy.IsOn)
        {
            if (_session is { } session)
            {
                session.Position = _engine.Position;
            }

            Stop();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_session is not { } session || _engine.State is not (PlaybackState.Playing or PlaybackState.Paused))
        {
            return;
        }

        var paused = _engine.State == PlaybackState.Paused;
        if (paused != session.WasPaused)
        {
            session.WasPaused = paused;
            session.PositionAt = _time.GetTimestamp();
            Send(_reporter.ReportProgressAsync, Report(session));
        }

        UpdateTimer();
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        var now = _time.GetTimestamp();
        var expected = session.Position + (session.WasPaused ? TimeSpan.Zero : _time.GetElapsedTime(session.PositionAt, now));
        var jumped = (_engine.Position - expected).Duration() > SeekTolerance;
        session.Position = _engine.Position;
        session.PositionAt = now;

        if (jumped)
        {
            Send(_reporter.ReportProgressAsync, Report(session));
        }
    }

    private void OnEnded(object? sender, EventArgs e)
    {
        if (_session is { } session)
        {
            session.Position = _engine.Duration > TimeSpan.Zero ? _engine.Duration : session.Position;
        }

        Stop();
    }

    private void OnTimer()
    {
        if (_session is { WasPaused: false } session)
        {
            session.Position = _engine.Position;
            session.PositionAt = _time.GetTimestamp();
            Send(_reporter.ReportProgressAsync, Report(session));
        }
    }

    private void Stop()
    {
        if (_session is { } session)
        {
            _session = null;
            UpdateTimer();
            Send(_reporter.ReportStoppedAsync, Report(session));
        }
    }

    private void UpdateTimer()
    {
        var playing = _session is { WasPaused: false };
        _timer.Change(playing ? ProgressInterval : Timeout.InfiniteTimeSpan, playing ? ProgressInterval : Timeout.InfiniteTimeSpan);
    }

    private PlaybackReport Report(Session session) =>
        new(session.ItemId, session.PlaySessionId, session.Position, session.WasPaused, (int)Math.Round(_engine.Volume * 100), _engine.IsMuted);

    /// <summary>Queues a report behind the previous ones; failures are logged, never thrown.</summary>
    private void Send(Func<PlaybackReport, CancellationToken, Task> send, PlaybackReport report)
    {
        lock (_gate)
        {
            _queue = _queue.ContinueWith(
                async _ =>
                {
                    try
                    {
                        await send(report, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
                    {
                        LogReportFailed(ex, report.ItemId);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reporting playback of {ItemId} to the server failed")]
    private partial void LogReportFailed(Exception exception, string itemId);

    private sealed class Session(string itemId, string playSessionId)
    {
        public string ItemId { get; } = itemId;

        public string PlaySessionId { get; } = playSessionId;

        public TimeSpan Position { get; set; }

        /// <summary>Gets or sets the timestamp of <see cref="Position"/>.</summary>
        public long PositionAt { get; set; }

        public bool WasPaused { get; set; }
    }
}
