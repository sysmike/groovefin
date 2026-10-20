using Groovefin.Core.Services;

namespace Groovefin.Core.Playback;

/// <summary>
/// Platform-independent part of an <see cref="IPlaybackEngine"/>: state, clamping, position throttling
/// and raising every event on the UI thread through <see cref="IDispatcher"/>. A platform engine
/// implements the <c>…Core</c> methods and reports what its player does with the <c>Report…</c>
/// methods, from any thread.
/// </summary>
/// <remarks>
/// <para>
/// Crossfade (<see cref="Crossfade"/>, for engines with <see cref="SupportsCrossfade"/>): the queued next
/// source still goes to the player for a gapless transition, but when the current one is
/// <see cref="Crossfade"/> from its end, the next one starts on a second player (<see cref="CrossfadeCore"/>)
/// and becomes <see cref="Current"/>. Over the fade the new one rises and the old one falls (equal power),
/// then the old player stops (<see cref="EndFadeCore"/>). Tracks of the same album never fade into each
/// other: they stay gapless. Pause, seek, load and stop end a fade at once.
/// </para>
/// </remarks>
public abstract class PlaybackEngineBase(IDispatcher dispatcher, TimeProvider time) : IPlaybackEngine
{
    /// <summary>Shortest interval between two <see cref="PositionChanged"/> events from the player.</summary>
    public static readonly TimeSpan PositionInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Smaller volume changes (slider jitter) do not reach the player.</summary>
    public const double VolumeTolerance = 0.001;

    /// <summary>The longest <see cref="Crossfade"/>.</summary>
    public static readonly TimeSpan MaxCrossfade = TimeSpan.FromSeconds(12);

    /// <summary>How often the volumes change during a fade.</summary>
    public static readonly TimeSpan FadeStep = TimeSpan.FromMilliseconds(50);

    /// <summary>A fade started late (after a seek near the end) is at least this long.</summary>
    public static readonly TimeSpan MinFade = TimeSpan.FromMilliseconds(500);

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

    private TimeSpan _crossfade;

    /// <summary>The running fade, or <see langword="null"/>.</summary>
    private Fade? _fade;

    /// <summary>How loud the current player is in a fade: from 0 to 1, 1 outside a fade.</summary>
    private double _fadeIn = 1;

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

    /// <summary>
    /// Gets or sets how long the end of a track overlaps the start of the next; zero (the default) for
    /// none. Values are limited to <see cref="MaxCrossfade"/>. Ignored by engines without
    /// <see cref="SupportsCrossfade"/>. A change applies from the next fade.
    /// </summary>
    public TimeSpan Crossfade
    {
        get => _crossfade;
        set => _crossfade = value < TimeSpan.Zero ? TimeSpan.Zero : value > MaxCrossfade ? MaxCrossfade : value;
    }

    /// <summary>Gets a value indicating whether a fade is running.</summary>
    public bool IsFading => _fade is not null;

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

        EndFade();
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
            // A paused fade would resume with both tracks at half volume: the new one takes over.
            EndFade();
            PauseCore();
        }
    }

    public void Seek(TimeSpan position)
    {
        if (Current is null)
        {
            return;
        }

        EndFade();
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

        EndFade();
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

    /// <summary>Gets a value indicating whether the engine can play two sources at once for <see cref="Crossfade"/>.</summary>
    protected virtual bool SupportsCrossfade => false;

    /// <summary>
    /// Starts <paramref name="next"/> on a second player at volume 0, which from now on is the player all
    /// other <c>…Core</c> methods and <c>Report…</c> calls are about. The old player keeps playing until
    /// <see cref="EndFadeCore"/>; only <see cref="SetFadingVolumeCore"/> reaches it.
    /// </summary>
    protected virtual void CrossfadeCore(PlaybackSource next) => throw new NotSupportedException();

    /// <summary>Sets the volume of the player that fades out.</summary>
    protected virtual void SetFadingVolumeCore(double volume)
    {
    }

    /// <summary>Stops the player that faded out and lets go of its stream.</summary>
    protected virtual void EndFadeCore()
    {
    }

    /// <summary>Ends a running fade at once (the new source at full volume), e.g. before the engine is disposed.</summary>
    protected void EndFade()
    {
        if (_fade is not { } fade)
        {
            return;
        }

        _fade = null;
        fade.Timer.Dispose();
        _fadeIn = 1;
        EndFadeCore();

        // Exactly the full volume, even when the last step was already within the tolerance of it.
        ApplyVolume(force: true);
    }

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
            StartFadeIfDue();
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

    /// <summary>
    /// Gets a value indicating whether <paramref name="current"/> fades into <paramref name="next"/>: not
    /// within an album, which is meant to play without a gap (live albums, mixes, classical works).
    /// </summary>
    public static bool FadesInto(PlaybackSource current, PlaybackSource next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        return current.Track.AlbumId is not { Length: > 0 } album || !string.Equals(album, next.Track.AlbumId, StringComparison.Ordinal);
    }

    /// <summary>Starts the fade into <see cref="Next"/> once the current source is <see cref="Crossfade"/> from its end.</summary>
    private void StartFadeIfDue()
    {
        if (!SupportsCrossfade || _fade is not null || _crossfade <= TimeSpan.Zero
            || Current is not { } current || Next is not { } next || State != PlaybackState.Playing
            || Duration <= TimeSpan.Zero || Duration - Position > _crossfade || !FadesInto(current, next))
        {
            return;
        }

        // Both tracks need room for the fade: a short one (a jingle, an interlude) stays gapless.
        if (Duration < _crossfade * 2 || next.Track.Duration is { } nextLength && nextLength < _crossfade * 2)
        {
            return;
        }

        var remaining = Duration - Position;
        var length = remaining < MinFade ? MinFade : remaining;
        var outgoing = _playerVolume;

        // The old player must not move on to the next source by itself while it fades out.
        SetNextCore(null);
        _fadeIn = 0;
        CrossfadeCore(next);

        _queuedNext.Clear();
        Current = next;
        Next = null;
        _streamStart = TimeSpan.Zero;
        Position = TimeSpan.Zero;
        Duration = next.Track.Duration ?? TimeSpan.Zero;
        _lastPositionReport = long.MinValue;
        _playerVolume = 0;
        ApplyVolume();

        _fade = new Fade(time.GetTimestamp(), length, outgoing, time.CreateTimer(_ => Dispatch(StepFade), null, FadeStep, FadeStep));
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One step of the fade: equal power, so the sum sounds as loud as one track.</summary>
    private void StepFade()
    {
        if (_fade is not { } fade)
        {
            return;
        }

        var progress = Math.Clamp(time.GetElapsedTime(fade.Started) / fade.Length, 0, 1);
        if (progress >= 1)
        {
            EndFade();
            return;
        }

        _fadeIn = Math.Sin(progress * Math.PI / 2);
        ApplyVolume();
        SetFadingVolumeCore(fade.OutgoingVolume * Math.Cos(progress * Math.PI / 2));
    }

    private void SetState(PlaybackState state)
    {
        if (state != State)
        {
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Passes the user's volume times the current source's gain (and the fade) to the player, when it changed.</summary>
    private void ApplyVolume(bool force = false)
    {
        var gain = Current?.Gain is { } g && double.IsFinite(g) ? Math.Clamp(g, 0, 1) : 1;
        var volume = _volume * gain * _fadeIn;
        if (force || Math.Abs(volume - _playerVolume) >= VolumeTolerance)
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

    /// <summary>A running fade: when it started (timestamp), how long it lasts and the old player's volume at the start.</summary>
    private sealed record Fade(long Started, TimeSpan Length, double OutgoingVolume, ITimer Timer);
}
