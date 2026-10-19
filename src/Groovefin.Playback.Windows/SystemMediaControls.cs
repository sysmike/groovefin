using Groovefin.Core.Playback;
using Microsoft.Extensions.Logging;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Groovefin.Playback.Windows;

/// <summary>
/// <see cref="ISystemMediaControls"/> on the System Media Transport Controls of
/// <see cref="MediaPlayerEngine"/>'s player: media keys, the media flyout and the lock screen.
/// </summary>
public sealed partial class SystemMediaControls : ISystemMediaControls, IDisposable
{
    private readonly SystemMediaTransportControls _controls;
    private readonly ILogger<SystemMediaControls> _logger;
    private int _displayVersion;

    public SystemMediaControls(MediaPlayerEngine engine, ILogger<SystemMediaControls> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _logger = logger;
        _controls = engine.TransportControls;
        _controls.IsEnabled = true;
        _controls.IsPlayEnabled = true;
        _controls.IsPauseEnabled = true;
        _controls.IsNextEnabled = true;
        _controls.IsPreviousEnabled = true;
        _controls.ButtonPressed += OnButtonPressed;
    }

    public event EventHandler<MediaControlsButtonEventArgs>? ButtonPressed;

    public void Dispose() => _controls.ButtonPressed -= OnButtonPressed;

    public void SetStatus(PlaybackState state) => _controls.PlaybackStatus = state switch
    {
        PlaybackState.Opening or PlaybackState.Buffering => MediaPlaybackStatus.Changing,
        PlaybackState.Playing => MediaPlaybackStatus.Playing,
        PlaybackState.Paused => MediaPlaybackStatus.Paused,
        _ => MediaPlaybackStatus.Closed,
    };

    public void SetTimeline(TimeSpan position, TimeSpan duration) =>
        _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = position,
            MaxSeekTime = duration,
            EndTime = duration,
        });

    public async Task SetDisplayAsync(MediaControlsDisplay? display)
    {
        var version = Interlocked.Increment(ref _displayVersion);
        var thumbnail = display?.ArtworkPath is { } path ? await OpenArtworkAsync(path).ConfigureAwait(false) : null;
        if (version != Volatile.Read(ref _displayVersion))
        {
            // A newer call replaced this one while the artwork was opened.
            return;
        }

        var updater = _controls.DisplayUpdater;
        updater.ClearAll();
        if (display is not null)
        {
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = display.Title;
            updater.MusicProperties.Artist = display.Artist;
            updater.MusicProperties.AlbumTitle = display.Album;
            updater.Thumbnail = thumbnail;
        }

        updater.Update();
    }

    private async Task<RandomAccessStreamReference?> OpenArtworkAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
            return RandomAccessStreamReference.CreateFromFile(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Evicted from the cache meanwhile, or unreadable: show the track without a cover.
            LogArtworkFailed(ex);
            return null;
        }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        MediaControlsButton? button = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => MediaControlsButton.Play,
            SystemMediaTransportControlsButton.Pause => MediaControlsButton.Pause,
            SystemMediaTransportControlsButton.Next => MediaControlsButton.Next,
            SystemMediaTransportControlsButton.Previous => MediaControlsButton.Previous,
            _ => null,
        };

        if (button is { } pressed)
        {
            ButtonPressed?.Invoke(this, new MediaControlsButtonEventArgs(pressed));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The cover for the media controls could not be opened")]
    private partial void LogArtworkFailed(Exception exception);
}
