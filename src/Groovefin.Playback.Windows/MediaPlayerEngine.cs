using Groovefin.Audio;
using Groovefin.Core.Equalizer;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Groovefin.Playback.Windows;

/// <summary>
/// <see cref="IPlaybackEngine"/> on <c>Windows.Media.Playback.MediaPlayer</c>. Every load gets a
/// <see cref="MediaPlaybackList"/> holding the current item and the queued next one, so the next one is
/// pre-buffered and follows without a gap. MediaPlayer raises its events on background threads;
/// <see cref="PlaybackEngineBase"/> moves them to the UI thread. The equalizer is an audio effect on the
/// player (<see cref="EqualizerEffectContract"/>), added once for everything it plays.
/// </summary>
/// <remarks>
/// A crossfade needs two players at once. Each player with its list is a <see cref="Deck"/>: the active
/// deck plays and reports; during a fade the other one fades out and is then stopped. The second deck is
/// only created for the first fade. The transport controls stay those of the first player, which
/// <see cref="SystemMediaControls"/> drives by hand whichever deck plays.
/// </remarks>
public sealed partial class MediaPlayerEngine : PlaybackEngineBase, IDisposable, IAudioEqualizer
{
    /// <summary>How long before the end of the current item the next one starts buffering.</summary>
    public static readonly TimeSpan PrefetchTime = TimeSpan.FromSeconds(30);

    /// <summary>The equalizer effect's property set: empty, its parameters go through the DLL's exports (#169).</summary>
    private readonly PropertySet _equalizer = new();
    private readonly ILogger<MediaPlayerEngine> _logger;

    /// <summary>The first deck; its player's transport controls are the app's.</summary>
    private readonly Deck _primary;

    /// <summary>Guards the decks and their lists below, which MediaPlayer's event threads read.</summary>
    private readonly Lock _gate = new();
    private Deck _active;
    private Deck? _spare;
    private Deck? _fading;
    private bool _isMuted;
    private bool _disposed;

    /// <summary>Whether Windows could create the equalizer effect at start-up.</summary>
    private readonly bool _equalizerAdded;

    /// <summary>Whether the effect was taken out again because a track failed with it (#167).</summary>
    private bool _effectDropped;

    /// <summary>When the track was started again without the effect, and on which deck.</summary>
    private long _retriedAt;
    private Deck? _retriedDeck;

    /// <summary>The effect's counters at the last check, to log only what is new.</summary>
    private EqualizerEffectStatus? _effectStatus;

    /// <summary>Whether the parameters set last change the sound.</summary>
    private bool _equalizerActive;

    public MediaPlayerEngine(IDispatcher dispatcher, TimeProvider time, ILogger<MediaPlayerEngine> logger)
        : base(dispatcher, time)
    {
        _logger = logger;
        _equalizerAdded = CheckEqualizer();
        _primary = new Deck(this);
        _active = _primary;
    }

    /// <summary>Gets a value indicating whether the equalizer effect is in the audio path.</summary>
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _equalizerAdded && !_effectDropped;
            }
        }
    }

    /// <summary>Gets the System Media Transport Controls of the first player (media keys, the media flyout).</summary>
    public SystemMediaTransportControls TransportControls => _primary.Player.SystemMediaTransportControls;

    protected override bool SupportsCrossfade => true;

    /// <summary>Gets the deck that plays the current source.</summary>
    private Deck Active
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>
    /// Releases the players. Safe to call twice: the container disposes it once for each of its
    /// registrations (itself and <see cref="IPlaybackEngine"/>), and a closed player throws E_ABORT (#145).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EndFade();
        lock (_gate)
        {
            // After a fade the first deck may be the spare: each player is disposed once.
            Deck?[] decks = [_primary, _active, _spare];
            foreach (var deck in decks.OfType<Deck>().Distinct())
            {
                deck.Dispose();
            }
        }
    }

    /// <summary>Passes new parameters to the effect, which applies them from its next buffer.</summary>
    public void Apply(EqualizerParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!IsAvailable)
        {
            return;
        }

        try
        {
            EqualizerEffectLibrary.SetParameters(parameters);
            lock (_gate)
            {
                _equalizerActive = parameters.IsActive;
            }

            LogEqualizerApplied(parameters.Format());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            LogEqualizerNotApplied(ex);
        }
    }

    protected override void LoadCore(PlaybackSource source, bool autoPlay) => Active.Load(source, autoPlay, volume: null);

    protected override void SetNextCore(PlaybackSource? next) => Active.SetNext(next);

    protected override void PlayCore() => Active.Player.Play();

    protected override void PauseCore() => Active.Player.Pause();

    protected override void SeekCore(TimeSpan position) => Active.Player.PlaybackSession.Position = position;

    /// <summary>
    /// A file the server plays as it is has a length and supports range requests: any position works. A
    /// transcode arrives as one stream of unknown length (MediaPlayer reports <see cref="TimeSpan.MaxValue"/>)
    /// and can only be seeked within what has arrived; anywhere else MediaPlayer would end the item (#154).
    /// </summary>
    protected override bool CanSeekCore(TimeSpan position)
    {
        var session = Active.Player.PlaybackSession;
        if (session.NaturalDuration != TimeSpan.MaxValue)
        {
            return true;
        }

        foreach (var range in session.GetBufferedRanges())
        {
            if (position >= range.Start && position <= range.End)
            {
                return true;
            }
        }

        return false;
    }

    protected override void RestartCore(Uri url) => Active.Restart(url);

    protected override void StopCore() => Active.Stop();

    protected override void SetVolumeCore(double volume) => Active.Player.Volume = volume;

    protected override void SetMutedCore(bool isMuted)
    {
        lock (_gate)
        {
            _isMuted = isMuted;
            _active.Player.IsMuted = isMuted;
            if (_fading is not null)
            {
                _fading.Player.IsMuted = isMuted;
            }
        }
    }

    /// <summary>The next source starts on the other deck at volume 0, which becomes the active one; the old deck fades out.</summary>
    protected override void CrossfadeCore(PlaybackSource next)
    {
        Deck incoming;
        lock (_gate)
        {
            _spare ??= new Deck(this);
            incoming = _spare;
            _spare = null;
            _fading = _active;
            _active = incoming;
            incoming.Player.IsMuted = _isMuted;
        }

        incoming.Load(next, autoPlay: true, volume: 0);
    }

    protected override void SetFadingVolumeCore(double volume)
    {
        lock (_gate)
        {
            if (_fading is not null)
            {
                _fading.Player.Volume = volume;
            }
        }
    }

    /// <summary>The faded-out deck stops and waits as the spare for the next fade.</summary>
    protected override void EndFadeCore()
    {
        Deck? faded;
        lock (_gate)
        {
            faded = _fading;
            _fading = null;
            if (faded is not null)
            {
                _spare = faded;
            }
        }

        faded?.Stop();
    }

    private static PlaybackState Map(MediaPlaybackState state) => state switch
    {
        MediaPlaybackState.Opening => PlaybackState.Opening,
        MediaPlaybackState.Buffering => PlaybackState.Buffering,
        MediaPlaybackState.Playing => PlaybackState.Playing,
        MediaPlaybackState.Paused => PlaybackState.Paused,
        _ => PlaybackState.Idle,
    };

    /// <summary>
    /// Asks Windows whether it can create the equalizer effect: with a missing DLL or registration MediaPlayer
    /// would only skip it without a word, so this asks first and logs the answer. The effect is optional
    /// either way: if it fails later, the music plays without it.
    /// </summary>
    private bool CheckEqualizer()
    {
        var result = AudioEffectRegistration.Check(EqualizerEffectContract.ClassId);
        if (result >= 0)
        {
            try
            {
                EqualizerEffectLibrary.SetParameters(EqualizerParameters.Off);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                result = ex.HResult;
            }
        }

        if (result < 0)
        {
            LogEqualizerUnavailable(result);
            return false;
        }

        LogEqualizerAvailable();
        return true;
    }

    /// <summary>
    /// Logs what went wrong in the equalizer effect since the last check, at the start of each track (#169):
    /// it leaves a buffer as it is when it fails and has no log of its own, and Media Foundation leaves an
    /// optional effect out without a word.
    /// </summary>
    private void CheckEffectStatus()
    {
        if (!IsAvailable)
        {
            return;
        }

        EqualizerEffectStatus status;
        try
        {
            status = EqualizerEffectLibrary.GetStatus();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return;
        }

        EqualizerEffectStatus? last;
        bool active;
        lock (_gate)
        {
            last = _effectStatus;
            active = _equalizerActive;
            _effectStatus = status;
        }

        if (last is not { } previous)
        {
            return;
        }

        if (status.Errors > previous.Errors)
        {
            LogEqualizerErrors(status.Errors - previous.Errors, status.LastError, status.ProcessedBuffers, status.Buffers);
        }
        else if (active && status.Buffers == previous.Buffers)
        {
            LogEqualizerUnused();
        }
    }

    /// <summary>
    /// The safety net for the equalizer (#167): when a track fails with an exception from .NET code (HRESULT
    /// facility URT, 0x8013xxxx) while the effect is in the audio path, the effect is the one .NET code in
    /// Media Foundation that can have thrown it. The effect leaves both players and the track starts again
    /// without it, once; Settings no longer offers the equalizer. Any other error (a bad file, no audio
    /// output) is the track's and is reported as usual.
    /// </summary>
    /// <returns>Whether the track was started again (do not report the failure).</returns>
    private bool RetryWithoutEffect(Deck deck, int? result, string error)
    {
        Deck[] decks;
        lock (_gate)
        {
            if (_effectDropped && ReferenceEquals(deck, _retriedDeck) && Environment.TickCount64 - _retriedAt < 2000)
            {
                // The same failure reported a second time (item and player): the retry is already running.
                return true;
            }

            if (!_equalizerAdded || _effectDropped || !IsActive(deck) || result is not { } hr || (hr & 0x1FFF0000) != 0x00130000)
            {
                return false;
            }

            _effectDropped = true;
            _retriedAt = Environment.TickCount64;
            _retriedDeck = deck;
            decks = [.. new[] { _primary, _active, _spare, _fading }.OfType<Deck>().Distinct()];
        }

        LogEqualizerFailed(error);
        foreach (var d in decks)
        {
            d.Player.RemoveAllEffects();
        }

        deck.Reload();
        return true;
    }

    /// <summary>Gets a value indicating whether <paramref name="deck"/> is the one that reports; call with the gate held.</summary>
    private bool IsActive(Deck deck) => ReferenceEquals(deck, _active);

    /// <summary>A source and the playlist item made for it.</summary>
    private sealed record Entry(PlaybackSource Source, MediaPlaybackItem Item)
    {
        public static Entry For(PlaybackSource source)
        {
            var media = MediaSource.CreateFromUri(source.Url);

            // The item starts at the position itself: a seek right after loading is not reliable.
            return new(source, source.StartPosition > TimeSpan.Zero ? new MediaPlaybackItem(media, source.StartPosition) : new MediaPlaybackItem(media));
        }
    }

    /// <summary>
    /// A player with its list: the current item and the queued next one. Only the active deck passes its
    /// player's events on; the list and items are guarded by the engine's gate.
    /// </summary>
    private sealed class Deck : IDisposable
    {
        private readonly MediaPlayerEngine _engine;
        private MediaPlaybackList? _list;
        private Entry? _current;
        private Entry? _next;

        public Deck(MediaPlayerEngine engine)
        {
            _engine = engine;
            Player = new MediaPlayer
            {
                AutoPlay = false,
                AudioCategory = MediaPlayerAudioCategory.Media,
            };

            // SystemMediaControls drives the transport controls itself: the queue, not MediaPlayer, knows
            // what next and previous mean, and what to show.
            Player.CommandManager.IsEnabled = false;

            Player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            Player.PlaybackSession.PositionChanged += OnPositionChanged;
            Player.PlaybackSession.NaturalDurationChanged += OnPositionChanged;
            Player.MediaEnded += OnMediaEnded;
            Player.MediaFailed += OnMediaFailed;
            if (engine.IsAvailable)
            {
                Player.AddAudioEffect(EqualizerEffectContract.ClassId, effectOptional: true, engine._equalizer);
            }
        }

        public MediaPlayer Player { get; }

        /// <summary>Gets the source this deck reports about: its current one while it is the active deck.</summary>
        private PlaybackSource? ReportedSource
        {
            get
            {
                lock (_engine._gate)
                {
                    return _engine.IsActive(this) ? _current?.Source : null;
                }
            }
        }

        public void Load(PlaybackSource source, bool autoPlay, double? volume)
        {
            var list = new MediaPlaybackList { AutoRepeatEnabled = false, MaxPrefetchTime = PrefetchTime };
            var entry = Entry.For(source);
            list.Items.Add(entry.Item);

            lock (_engine._gate)
            {
                DetachList();
                _list = list;
                _current = entry;
                _next = null;
                list.CurrentItemChanged += OnCurrentItemChanged;
                list.ItemFailed += OnItemFailed;
            }

            if (volume is { } v)
            {
                Player.Volume = v;
            }

            Player.Source = list;
            if (autoPlay)
            {
                Player.Play();
            }
        }

        public void SetNext(PlaybackSource? next)
        {
            lock (_engine._gate)
            {
                if (_list is null)
                {
                    return;
                }

                // Everything but the current item goes: the replaced next one and items already played.
                for (var i = _list.Items.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(_list.Items[i], _current?.Item))
                    {
                        _list.Items.RemoveAt(i);
                    }
                }

                _next = next is null ? null : Entry.For(next);
                if (_next is not null)
                {
                    _list.Items.Add(_next.Item);
                }
            }
        }

        /// <summary>A new list with the restarted stream as the current item; the queued next one is kept.</summary>
        public void Restart(Uri url)
        {
            var playing = Player.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering or MediaPlaybackState.Opening;
            var list = new MediaPlaybackList { AutoRepeatEnabled = false, MaxPrefetchTime = PrefetchTime };
            lock (_engine._gate)
            {
                if (_current is not { } current)
                {
                    return;
                }

                var next = _next;
                DetachList();

                // The stream now starts at the target itself; StartPosition only applies to a first load.
                _current = new Entry(current.Source, new MediaPlaybackItem(MediaSource.CreateFromUri(url)));
                list.Items.Add(_current.Item);
                if (next is not null)
                {
                    _next = Entry.For(next.Source);
                    list.Items.Add(_next.Item);
                }

                _list = list;
                list.CurrentItemChanged += OnCurrentItemChanged;
                list.ItemFailed += OnItemFailed;
            }

            Player.Source = list;
            if (playing)
            {
                Player.Play();
            }
        }

        /// <summary>The current and the queued next source again, in a new list: their items start afresh.</summary>
        public void Reload()
        {
            var list = new MediaPlaybackList { AutoRepeatEnabled = false, MaxPrefetchTime = PrefetchTime };
            lock (_engine._gate)
            {
                if (_current is not { } current)
                {
                    return;
                }

                var next = _next;
                DetachList();
                _current = Entry.For(current.Source);
                list.Items.Add(_current.Item);
                if (next is not null)
                {
                    _next = Entry.For(next.Source);
                    list.Items.Add(_next.Item);
                }

                _list = list;
                list.CurrentItemChanged += OnCurrentItemChanged;
                list.ItemFailed += OnItemFailed;
            }

            Player.Source = list;
            Player.Play();
        }

        public void Stop()
        {
            lock (_engine._gate)
            {
                DetachList();
            }

            Player.Pause();
            Player.Source = null;
        }

        public void Dispose()
        {
            DetachList();
            Player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
            Player.PlaybackSession.PositionChanged -= OnPositionChanged;
            Player.PlaybackSession.NaturalDurationChanged -= OnPositionChanged;
            Player.MediaEnded -= OnMediaEnded;
            Player.MediaFailed -= OnMediaFailed;
            Player.Dispose();
        }

        /// <summary>Unhooks the list; call with the gate held.</summary>
        private void DetachList()
        {
            if (_list is not null)
            {
                _list.CurrentItemChanged -= OnCurrentItemChanged;
                _list.ItemFailed -= OnItemFailed;
            }

            _list = null;
            _current = null;
            _next = null;
        }

        private void OnCurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            _engine.CheckEffectStatus();
            PlaybackSource? advanced = null;
            lock (_engine._gate)
            {
                if (ReferenceEquals(sender, _list) && _next is { } next && ReferenceEquals(args.NewItem, next.Item))
                {
                    _current = next;
                    _next = null;
                    advanced = _engine.IsActive(this) ? next.Source : null;
                }
            }

            if (advanced is not null)
            {
                _engine.ReportAdvanced(advanced);
            }
        }

        private void OnItemFailed(MediaPlaybackList sender, MediaPlaybackItemFailedEventArgs args)
        {
            PlaybackSource? failedCurrent = null;
            PlaybackSource? failedNext = null;
            lock (_engine._gate)
            {
                if (!ReferenceEquals(sender, _list) || !_engine.IsActive(this))
                {
                    return;
                }

                if (_current is { } current && ReferenceEquals(args.Item, current.Item))
                {
                    failedCurrent = current.Source;
                }
                else if (_next is { } next && ReferenceEquals(args.Item, next.Item))
                {
                    failedNext = next.Source;
                }
            }

            if (failedCurrent is not null)
            {
                var result = args.Error.ExtendedError?.HResult;
                if (!_engine.RetryWithoutEffect(this, result, $"{args.Error.ErrorCode} (0x{result:X8})"))
                {
                    _engine.ReportFailed(failedCurrent, args.Error.ErrorCode.ToString(), args.Error.ExtendedError);
                }
            }
            else if (failedNext is not null)
            {
                _engine.ReportNextFailed(failedNext);
            }
        }

        private void OnPlaybackStateChanged(MediaPlaybackSession session, object args)
        {
            if (ReportedSource is not null)
            {
                _engine.ReportState(Map(session.PlaybackState));
            }
        }

        // A transcode's length is unknown (MaxValue) until it has arrived: report none.
        private void OnPositionChanged(MediaPlaybackSession session, object args)
        {
            if (ReportedSource is not null)
            {
                _engine.ReportPosition(session.Position, session.NaturalDuration == TimeSpan.MaxValue ? TimeSpan.Zero : session.NaturalDuration);
            }
        }

        private void OnMediaEnded(MediaPlayer sender, object args)
        {
            if (ReportedSource is { } source)
            {
                _engine.ReportEnded(source);
            }
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            if (ReportedSource is { } source
                && !_engine.RetryWithoutEffect(this, args.ExtendedErrorCode?.HResult, $"{args.Error} (0x{args.ExtendedErrorCode?.HResult:X8})"))
            {
                _engine.ReportFailed(source, $"{args.Error}: {args.ErrorMessage}", args.ExtendedErrorCode);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Equalizer effect is available")]
    private partial void LogEqualizerAvailable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Equalizer effect is not available (0x{Result:X8}); playing without it")]
    private partial void LogEqualizerUnavailable(int result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Equalizer effect failed: a track did not play with it ({Error}). Playing without it from now on")]
    private partial void LogEqualizerFailed(string error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Equalizer set to {Parameters}")]
    private partial void LogEqualizerApplied(string parameters);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Equalizer settings did not reach the effect")]
    private partial void LogEqualizerNotApplied(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Equalizer effect left {Errors} buffers unequalized after errors (last 0x{LastError:X8}); {Processed} of {Buffers} buffers equalized in all")]
    private partial void LogEqualizerErrors(long errors, int lastError, long processed, long buffers);

    [LoggerMessage(Level = LogLevel.Information, Message = "The equalizer is on, but no audio went through its effect during the last track")]
    private partial void LogEqualizerUnused();
}
