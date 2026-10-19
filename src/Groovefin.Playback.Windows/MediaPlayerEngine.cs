using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Groovefin.Playback.Windows;

/// <summary>
/// <see cref="IPlaybackEngine"/> on <c>Windows.Media.Playback.MediaPlayer</c>. MediaPlayer raises its
/// events on background threads; <see cref="PlaybackEngineBase"/> moves them to the UI thread.
/// </summary>
public sealed class MediaPlayerEngine : PlaybackEngineBase, IDisposable
{
    private readonly MediaPlayer _player;

    /// <summary>The source the player currently has; read from MediaPlayer's event threads.</summary>
    private volatile PlaybackSource? _playerSource;

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

    public void Dispose()
    {
        _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        _player.PlaybackSession.PositionChanged -= OnPositionChanged;
        _player.PlaybackSession.NaturalDurationChanged -= OnPositionChanged;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.Dispose();
    }

    protected override void LoadCore(PlaybackSource source, bool autoPlay)
    {
        _playerSource = source;
        _player.Source = MediaSource.CreateFromUri(source.Url);
        if (autoPlay)
        {
            _player.Play();
        }
    }

    protected override void PlayCore() => _player.Play();

    protected override void PauseCore() => _player.Pause();

    protected override void SeekCore(TimeSpan position) => _player.PlaybackSession.Position = position;

    protected override void StopCore()
    {
        _playerSource = null;
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

    private void OnPlaybackStateChanged(MediaPlaybackSession session, object args)
    {
        if (_playerSource is not null)
        {
            ReportState(Map(session.PlaybackState));
        }
    }

    private void OnPositionChanged(MediaPlaybackSession session, object args) =>
        ReportPosition(session.Position, session.NaturalDuration);

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        if (_playerSource is { } source)
        {
            ReportEnded(source);
        }
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (_playerSource is { } source)
        {
            ReportFailed(source, $"{args.Error}: {args.ErrorMessage}", args.ExtendedErrorCode);
        }
    }
}
