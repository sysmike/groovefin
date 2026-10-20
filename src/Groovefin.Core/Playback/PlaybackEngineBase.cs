using Groovefin.Core.Services;

namespace Groovefin.Core.Playback;

/// <summary>
/// Platform-independent part of an <see cref="IPlaybackEngine"/>: state, clamping, position throttling
/// and raising every event on the UI thread through <see cref="IDispatcher"/>. A platform engine
/// implements the <c>…Core</c> methods and reports what its player does with the <c>Report…</c>
/// methods, from any thread.
/// </summary>
public abstract class PlaybackEngineBase(IDispatcher dispatcher, TimeProvider time) : IPlaybackEngine
{
    /// <summary>Shortest interval between two <see cref="PositionChanged"/> events from the player.</summary>
    public static readonly TimeSpan PositionInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Smaller volume changes (slider jitter) do not reach the player.</summary>
    public const double VolumeTolerance = 0.001;

    private double _volume = 1;

    /// <summary>The volume last passed to <see cref="SetVolumeCore"/>: <see cref="Volume"/> times the gain.</summary>
    private double _playerVolume = 1;
    private bool _isMuted;
    private long _lastPositionReport = long.MinValue;

    /// <summary>Where in the track the loaded stream starts: not zero after a transcode was restarted to seek.</summary>
    private TimeSpan _streamStart;

    /// <summary>Sources passed to <see cref="SetNext"/> since the last load, oldest first. The player can
    /// move on to one that was replaced a moment ago, before the replacement reached it.</summary>
    private readonly List<PlaybackSource> _queuedNext = [];

    public event EventHandler? StateChanged;

    public event EventHandler? PositionChanged;

    public event EventHandler? CurrentChanged;

    public event EventHandler? Ended;

    public event EventHandler<PlaybackFailedEventArgs>? Failed;

    public PlaybackState State { get; private set; }

    public PlaybackSource? Current { get; private set; }

    public PlaybackSource? Next { get; private set; }

    public TimeSpan Position { get; private set; }

    public TimeSpan Duration { get; private set; }

    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = double.IsNaN(value) ? _volume : Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - _volume) >= VolumeTolerance)
            {
                _volume = clamped;
                ApplyVolume();
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (value != _isMuted)
            {
                _isMuted = value;
                SetMutedCore(value);
            }
        }
    }

    public void Load(PlaybackSource source, bool autoPlay = true)
    {
        ArgumentNullException.ThrowIfNull(source);

        Current = source;
        ClearNext();
        _streamStart = TimeSpan.Zero;
        Position = source.StartPosition;
        Duration = source.Track.Duration ?? TimeSpan.Zero;
        _lastPositionReport = long.MinValue;
        ApplyVolume();
        LoadCore(source, autoPlay);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetNext(PlaybackSource? next)
    {
        if (Current is null || ReferenceEquals(next, Next))
        {
            return;
        }

        Next = next;
        if (next is not null)
        {
            _queuedNext.Add(next);
        }

        SetNextCore(next);
    }

    public void Play()
    {
        if (Current is not null)
        {
            PlayCore();
        }
    }

    public void Pause()
    {
        if (Current is not null)
        {
            PauseCore();
        }
    }

    public void Seek(TimeSpan position)
    {
        if (Current is null)
        {
            return;
        }

        var target = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (Duration > TimeSpan.Zero && target > Duration)
        {
            target = Duration;
        }

        // A transcoded stream can only be read from the front: where the player cannot seek, the stream
        // starts again at the target (#154). Positions in the stream are relative to where it started.
        if (target >= _streamStart && CanSeekCore(target - _streamStart))
        {
            SeekCore(target - _streamStart);
        }
        else if (Current.UrlAt is { } urlAt)
        {
            _streamStart = target;
            RestartCore(urlAt(target));
        }
        else
        {
            SeekCore(target > _streamStart ? target - _streamStart : TimeSpan.Zero);
        }

        Position = target;
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (Current is null)
        {
            return;
        }

        StopCore();
        Current = null;
        ClearNext();
        _streamStart = TimeSpan.Zero;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        SetState(PlaybackState.Idle);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected abstract void LoadCore(PlaybackSource source, bool autoPlay);

    /// <summary>Queues <paramref name="next"/> after the current source, replacing a queued one; <see langword="null"/> clears it.</summary>
    protected abstract void SetNextCore(PlaybackSource? next);

    protected abstract void PlayCore();

    protected abstract void PauseCore();

    /// <summary>Moves to <paramref name="position"/> in the loaded stream (relative to where it started).</summary>
    protected abstract void SeekCore(TimeSpan position);

    /// <summary>
    /// Gets a value indicating whether the player can move to <paramref name="position"/> in the loaded
    /// stream. When not, <see cref="Seek"/> restarts the stream there (<see cref="RestartCore"/>).
    /// </summary>
    protected virtual bool CanSeekCore(TimeSpan position) => true;

    /// <summary>Replaces the current stream with <paramref name="url"/>, the same track from a later or earlier point, keeping play or pause.</summary>
    protected abstract void RestartCore(Uri url);

    protected abstract void StopCore();

    protected abstract void SetVolumeCore(double volume);

    protected abstract void SetMutedCore(bool isMuted);

    /// <summary>The player changed state. Safe to call from any thread.</summary>
    protected void ReportState(PlaybackState state) => Dispatch(() => SetState(state));

    /// <summary>
    /// The player's position or length changed. Safe to call from any thread; events are throttled to
    /// <see cref="PositionInterval"/>, a changed length is always reported. The length is only taken when
    /// the track has none from the server.
    /// </summary>
    protected void ReportPosition(TimeSpan position, TimeSpan duration)
    {
        var now = time.GetTimestamp();
        Dispatch(() =>
        {
            if (Current is null)
            {
                return;
            }

            // The server's track length wins: the player's is wrong while a transcoded stream, which has no
            // known length, is still arriving (#153). It only fills in for tracks without one.
            var hasTrackLength = Current.Track.Duration > TimeSpan.Zero;
            var durationChanged = !hasTrackLength && duration > TimeSpan.Zero && duration != Duration;
            if (!durationChanged && _lastPositionReport != long.MinValue && time.GetElapsedTime(_lastPositionReport, now) < PositionInterval)
            {
                return;
            }

            _lastPositionReport = now;
            Position = _streamStart + position;
            if (durationChanged)
            {
                Duration = duration;
            }

            PositionChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>The player reached the end of <paramref name="source"/>. Safe to call from any thread.</summary>
    protected void ReportEnded(PlaybackSource source) => Dispatch(() =>
    {
        if (ReferenceEquals(source, Current))
        {
            Ended?.Invoke(this, EventArgs.Empty);
        }
    });

    /// <summary>
    /// The player moved on to <paramref name="source"/>, a source queued with <see cref="SetNext"/>, by
    /// itself. Safe to call from any thread; ignored when a load or stop came first.
    /// </summary>
    protected void ReportAdvanced(PlaybackSource source) => Dispatch(() =>
    {
        var index = _queuedNext.FindIndex(queued => ReferenceEquals(queued, source));
        if (Current is null || index < 0)
        {
            return;
        }

        _queuedNext.RemoveRange(0, index + 1);
        Current = source;
        if (ReferenceEquals(source, Next))
        {
            Next = null;
        }

        ApplyVolume();

        _streamStart = TimeSpan.Zero;
        Position = TimeSpan.Zero;
        Duration = source.Track.Duration ?? TimeSpan.Zero;
        _lastPositionReport = long.MinValue;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>
    /// The player could not pre-buffer <paramref name="source"/>, the queued next source: it is dropped and
    /// the current source ends normally. Safe to call from any thread.
    /// </summary>
    protected void ReportNextFailed(PlaybackSource source) => Dispatch(() =>
    {
        if (ReferenceEquals(source, Next))
        {
            Next = null;
            SetNextCore(null);
        }
    });

    /// <summary>The player could not play <paramref name="source"/>. Safe to call from any thread.</summary>
    protected void ReportFailed(PlaybackSource source, string message, Exception? exception) => Dispatch(() =>
    {
        if (ReferenceEquals(source, Current))
        {
            SetState(PlaybackState.Idle);
            Failed?.Invoke(this, new PlaybackFailedEventArgs(source, message, exception));
        }
    });

    private void SetState(PlaybackState state)
    {
        if (state != State)
        {
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Passes the user's volume times the current source's gain to the player, when it changed.</summary>
    private void ApplyVolume()
    {
        var gain = Current?.Gain is { } g && double.IsFinite(g) ? Math.Clamp(g, 0, 1) : 1;
        var volume = _volume * gain;
        if (Math.Abs(volume - _playerVolume) >= VolumeTolerance)
        {
            _playerVolume = volume;
            SetVolumeCore(volume);
        }
    }

    private void ClearNext()
    {
        Next = null;
        _queuedNext.Clear();
    }

    private void Dispatch(Action action) => _ = dispatcher.EnqueueAsync(action);
}
