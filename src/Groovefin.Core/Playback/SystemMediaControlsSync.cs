using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Services;

namespace Groovefin.Core.Playback;

/// <summary>
/// Keeps the system media controls in line with the player: title, artists, album and cover of the
/// playing track, the playback status and the timeline (on every track and state change, on seeks and
/// every <see cref="TimelineInterval"/> while playing). Buttons pressed there (media keys, the media
/// flyout) control the player on the UI thread. Create it once at startup and keep it alive.
/// </summary>
public sealed class SystemMediaControlsSync : IDisposable
{
    /// <summary>Cover width in pixels requested for the controls.</summary>
    public const double ArtworkWidth = 300;

    /// <summary>How often the timeline is refreshed while playing.</summary>
    public static readonly TimeSpan TimelineInterval = TimeSpan.FromSeconds(5);

    /// <summary>A position jump larger than this beyond the elapsed time counts as a seek.</summary>
    public static readonly TimeSpan SeekTolerance = TimeSpan.FromSeconds(1);

    private readonly IPlayer _player;
    private readonly IPlaybackEngine _engine;
    private readonly ISystemMediaControls _controls;
    private readonly IImageCache _images;
    private readonly IDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private int _displayVersion;

    private TimeSpan _timelinePosition;
    private TimeSpan _timelineDuration;
    private long _timelineAt;
    private bool _timelineRunning;

    public SystemMediaControlsSync(
        IPlayer player,
        ISystemMediaControls controls,
        IImageCache images,
        IDispatcher dispatcher,
        TimeProvider time)
    {
        _player = player;
        _engine = player.Engine;
        _controls = controls;
        _images = images;
        _dispatcher = dispatcher;
        _time = time;

        _engine.CurrentChanged += OnCurrentChanged;
        _engine.StateChanged += OnStateChanged;
        _engine.PositionChanged += OnPositionChanged;
        _controls.ButtonPressed += OnButtonPressed;

        OnCurrentChanged(this, EventArgs.Empty);
    }

    /// <summary>Gets the latest display update, including its cover (tests).</summary>
    internal Task DisplayUpdate { get; private set; } = Task.CompletedTask;

    public void Dispose()
    {
        _engine.CurrentChanged -= OnCurrentChanged;
        _engine.StateChanged -= OnStateChanged;
        _engine.PositionChanged -= OnPositionChanged;
        _controls.ButtonPressed -= OnButtonPressed;
    }

    private bool IsActive => _engine.State is PlaybackState.Playing or PlaybackState.Buffering or PlaybackState.Opening;

    private static MediaControlsDisplay DisplayFor(Track track) => new(
        track.Name,
        string.Join(", ", track.Artists.Select(a => a.Name)),
        track.AlbumName ?? string.Empty,
        null);

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        _controls.SetStatus(_engine.State);
        UpdateTimeline();

        var version = Interlocked.Increment(ref _displayVersion);
        DisplayUpdate = _engine.Current?.Track is { } track
            ? ShowAsync(DisplayFor(track), track, version)
            : _controls.SetDisplayAsync(null);
    }

    private async Task ShowAsync(MediaControlsDisplay display, Track track, int version)
    {
        // The text first, so the flyout is right at once; the cover follows when it is loaded.
        await _controls.SetDisplayAsync(display).ConfigureAwait(false);

        if (track.AlbumId is not { } albumId || ImageRequest.Primary(albumId, track.AlbumImageTag, ArtworkWidth, 1) is not { } request)
        {
            return;
        }

        var path = await _images.GetAsync(request).ConfigureAwait(false);
        if (path is not null && version == Volatile.Read(ref _displayVersion))
        {
            await _controls.SetDisplayAsync(display with { ArtworkPath = path }).ConfigureAwait(false);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        _controls.SetStatus(_engine.State);
        UpdateTimeline();
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (_engine.Current is null)
        {
            return;
        }

        var elapsed = _time.GetElapsedTime(_timelineAt);
        var expected = _timelinePosition + (_timelineRunning ? elapsed : TimeSpan.Zero);
        if (elapsed >= TimelineInterval
            || _engine.Duration != _timelineDuration
            || (_engine.Position - expected).Duration() > SeekTolerance)
        {
            UpdateTimeline();
        }
    }

    private void UpdateTimeline()
    {
        _timelinePosition = _engine.Position;
        _timelineDuration = _engine.Duration;
        _timelineAt = _time.GetTimestamp();
        _timelineRunning = _engine.State == PlaybackState.Playing;
        _controls.SetTimeline(_timelinePosition, _timelineDuration);
    }

    private void OnButtonPressed(object? sender, MediaControlsButtonEventArgs e) =>
        _ = _dispatcher.EnqueueAsync(() => Press(e.Button));

    private void Press(MediaControlsButton button)
    {
        switch (button)
        {
            case MediaControlsButton.Play when !IsActive:
            case MediaControlsButton.Pause when IsActive:
                _player.TogglePlayPause();
                break;
            case MediaControlsButton.Next:
                _player.Next();
                break;
            case MediaControlsButton.Previous:
                _player.Previous();
                break;
            default:
                break;
        }
    }
}
