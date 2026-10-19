using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Groovefin.Playback.Windows;

/// <summary>
/// <see cref="IPlaybackEngine"/> on <c>Windows.Media.Playback.MediaPlayer</c>. Every load gets a
/// <see cref="MediaPlaybackList"/> holding the current item and the queued next one, so the next one is
/// pre-buffered and follows without a gap. MediaPlayer raises its events on background threads;
/// <see cref="PlaybackEngineBase"/> moves them to the UI thread.
/// </summary>
public sealed class MediaPlayerEngine : PlaybackEngineBase, IDisposable
{
    /// <summary>How long before the end of the current item the next one starts buffering.</summary>
    public static readonly TimeSpan PrefetchTime = TimeSpan.FromSeconds(30);

    private readonly MediaPlayer _player;

    /// <summary>Guards the list and its items below, which MediaPlayer's event threads read.</summary>
    private readonly Lock _gate = new();
    private MediaPlaybackList? _list;
    private Entry? _current;
    private Entry? _next;

    public MediaPlayerEngine(IDispatcher dispatcher, TimeProvider time)
        : base(dispatcher, time)
    {
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
    }

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

    public void Dispose()
    {
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

    private void OnPositionChanged(MediaPlaybackSession session, object args) =>
        ReportPosition(session.Position, session.NaturalDuration);

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
        public static Entry For(PlaybackSource source) => new(source, new MediaPlaybackItem(MediaSource.CreateFromUri(source.Url)));
    }
}
