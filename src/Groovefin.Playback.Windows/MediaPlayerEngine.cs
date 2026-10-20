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
public sealed partial class MediaPlayerEngine : PlaybackEngineBase, IDisposable, IAudioEqualizer
{
    /// <summary>How long before the end of the current item the next one starts buffering.</summary>
    public static readonly TimeSpan PrefetchTime = TimeSpan.FromSeconds(30);

    private readonly MediaPlayer _player;

    /// <summary>The equalizer effect's configuration; the effect follows its changes.</summary>
    private readonly PropertySet _equalizer = new();
    private readonly ILogger<MediaPlayerEngine> _logger;

    /// <summary>Guards the list and its items below, which MediaPlayer's event threads read.</summary>
    private readonly Lock _gate = new();
    private MediaPlaybackList? _list;
    private Entry? _current;
    private Entry? _next;
    private bool _disposed;

    public MediaPlayerEngine(IDispatcher dispatcher, TimeProvider time, ILogger<MediaPlayerEngine> logger)
        : base(dispatcher, time)
    {
        _logger = logger;
        _player = new MediaPlayer
        {
            AutoPlay = false,
            AudioCategory = MediaPlayerAudioCategory.Media,
        };

        // SystemMediaControls drives the transport controls itself: the queue, not MediaPlayer, knows
        // what next and previous mean, and what to show.
        _player.CommandManager.IsEnabled = false;

        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.PlaybackSession.PositionChanged += OnPositionChanged;
        _player.PlaybackSession.NaturalDurationChanged += OnPositionChanged;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        IsAvailable = AddEqualizer();
    }

    /// <summary>Gets a value indicating whether the equalizer effect is in the audio path.</summary>
    public bool IsAvailable { get; }

    /// <summary>Gets the System Media Transport Controls of the player (media keys, the media flyout).</summary>
    public SystemMediaTransportControls TransportControls => _player.SystemMediaTransportControls;

    /// <summary>Gets the source the player is on; MediaPlayer's event threads read it.</summary>
    private PlaybackSource? CurrentSource
    {
        get
        {
            lock (_gate)
            {
                return _current?.Source;
            }
        }
    }

    /// <summary>
    /// Releases the player. Safe to call twice: the container disposes it once for each of its
    /// registrations (itself and <see cref="IPlaybackEngine"/>), and the closed player throws E_ABORT (#145).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            DetachList();
        }

        _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        _player.PlaybackSession.PositionChanged -= OnPositionChanged;
        _player.PlaybackSession.NaturalDurationChanged -= OnPositionChanged;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.Dispose();
    }

    /// <summary>Passes new parameters to the effect, which applies them from its next buffer.</summary>
    public void Apply(EqualizerParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (IsAvailable)
        {
            _equalizer[EqualizerEffectContract.ParametersKey] = parameters.Format();
        }
    }

    protected override void LoadCore(PlaybackSource source, bool autoPlay)
    {
        var list = new MediaPlaybackList { AutoRepeatEnabled = false, MaxPrefetchTime = PrefetchTime };
        var entry = Entry.For(source);
        list.Items.Add(entry.Item);

        lock (_gate)
        {
            DetachList();
            _list = list;
            _current = entry;
            _next = null;
            list.CurrentItemChanged += OnCurrentItemChanged;
            list.ItemFailed += OnItemFailed;
        }

        _player.Source = list;
        if (autoPlay)
        {
            _player.Play();
        }
    }

    protected override void SetNextCore(PlaybackSource? next)
    {
        lock (_gate)
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

    protected override void PlayCore() => _player.Play();

    protected override void PauseCore() => _player.Pause();

    protected override void SeekCore(TimeSpan position) => _player.PlaybackSession.Position = position;

    /// <summary>
    /// A file the server plays as it is has a length and supports range requests: any position works. A
    /// transcode arrives as one stream of unknown length (MediaPlayer reports <see cref="TimeSpan.MaxValue"/>)
    /// and can only be seeked within what has arrived; anywhere else MediaPlayer would end the item (#154).
    /// </summary>
    protected override bool CanSeekCore(TimeSpan position)
    {
        var session = _player.PlaybackSession;
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

    /// <summary>A new list with the restarted stream as the current item; the queued next one is kept.</summary>
    protected override void RestartCore(Uri url)
    {
        var playing = _player.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering or MediaPlaybackState.Opening;
        var list = new MediaPlaybackList { AutoRepeatEnabled = false, MaxPrefetchTime = PrefetchTime };
        lock (_gate)
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

        _player.Source = list;
        if (playing)
        {
            _player.Play();
        }
    }

    protected override void StopCore()
    {
        lock (_gate)
        {
            DetachList();
        }

        _player.Pause();
        _player.Source = null;
    }

    protected override void SetVolumeCore(double volume) => _player.Volume = volume;

    protected override void SetMutedCore(bool isMuted) => _player.IsMuted = isMuted;

    /// <summary>
    /// Adds the equalizer effect, if Windows can create it: with a missing DLL or registration MediaPlayer
    /// would only skip it without a word, so this asks first and logs the answer. The effect is optional
    /// either way: if it fails later, the music plays without it.
    /// </summary>
    private bool AddEqualizer()
    {
        var result = AudioEffectRegistration.Check(EqualizerEffectContract.ClassId);
        if (result < 0)
        {
            LogEqualizerUnavailable(result);
            return false;
        }

        _equalizer[EqualizerEffectContract.ParametersKey] = EqualizerParameters.Off.Format();
        _player.AddAudioEffect(EqualizerEffectContract.ClassId, effectOptional: true, _equalizer);
        LogEqualizerAvailable();
        return true;
    }

    private static PlaybackState Map(MediaPlaybackState state) => state switch
    {
        MediaPlaybackState.Opening => PlaybackState.Opening,
        MediaPlaybackState.Buffering => PlaybackState.Buffering,
        MediaPlaybackState.Playing => PlaybackState.Playing,
        MediaPlaybackState.Paused => PlaybackState.Paused,
        _ => PlaybackState.Idle,
    };

    /// <summary>Unhooks the current list; call with the gate held.</summary>
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
        PlaybackSource? advanced = null;
        lock (_gate)
        {
            if (ReferenceEquals(sender, _list) && _next is { } next && ReferenceEquals(args.NewItem, next.Item))
            {
                _current = next;
                _next = null;
                advanced = next.Source;
            }
        }

        if (advanced is not null)
        {
            ReportAdvanced(advanced);
        }
    }

    private void OnItemFailed(MediaPlaybackList sender, MediaPlaybackItemFailedEventArgs args)
    {
        PlaybackSource? failedCurrent = null;
        PlaybackSource? failedNext = null;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _list))
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
            ReportFailed(failedCurrent, args.Error.ErrorCode.ToString(), args.Error.ExtendedError);
        }
        else if (failedNext is not null)
        {
            ReportNextFailed(failedNext);
        }
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession session, object args)
    {
        if (CurrentSource is not null)
        {
            ReportState(Map(session.PlaybackState));
        }
    }

    // A transcode's length is unknown (MaxValue) until it has arrived: report none.
    private void OnPositionChanged(MediaPlaybackSession session, object args) =>
        ReportPosition(session.Position, session.NaturalDuration == TimeSpan.MaxValue ? TimeSpan.Zero : session.NaturalDuration);

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        if (CurrentSource is { } source)
        {
            ReportEnded(source);
        }
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (CurrentSource is { } source)
        {
            ReportFailed(source, $"{args.Error}: {args.ErrorMessage}", args.ExtendedErrorCode);
        }
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Equalizer effect is available")]
    private partial void LogEqualizerAvailable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Equalizer effect is not available (0x{Result:X8}); playing without it")]
    private partial void LogEqualizerUnavailable(int result);
}
