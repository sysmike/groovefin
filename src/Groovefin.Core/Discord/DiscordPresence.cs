using Groovefin.Core.Playback;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Discord;

/// <summary>
/// Shows the playing track in Discord ("Listening to Groovefin") while the setting is on. Lives as long as
/// the app. Only the latest state is sent; while Discord is not running it retries now and then, and
/// problems only go to the log. Closing the app closes the pipe, which clears the status.
/// </summary>
public sealed partial class DiscordPresence : IDisposable
{
    /// <summary>How long to wait before trying again after Discord could not be reached.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>How far the progress may drift before Discord gets new timestamps (a seek).</summary>
    public static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(2);

    private readonly IPlaybackEngine _engine;
    private readonly ISettingsService _settings;
    private readonly IDiscordConnection _connection;
    private readonly DiscordApplication _application;
    private readonly TimeProvider _time;
    private readonly ILogger<DiscordPresence> _logger;
    private readonly Lock _gate = new();

    private DiscordActivity? _wanted;
    private DiscordActivity? _sent;
    private bool _dirty;
    private bool _sending;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;

    public DiscordPresence(
        IPlaybackEngine engine,
        ISettingsService settings,
        IDiscordConnection connection,
        DiscordApplication application,
        TimeProvider time,
        ILogger<DiscordPresence> logger)
    {
        _engine = engine;
        _settings = settings;
        _connection = connection;
        _application = application;
        _time = time;
        _logger = logger;

        _engine.CurrentChanged += OnChanged;
        _engine.StateChanged += OnChanged;
        _engine.PositionChanged += OnPositionChanged;
        _settings.Changed += OnSettingsChanged;
        _ = UpdateAsync();
    }

    /// <summary>Gets what Discord should show now, or <see langword="null"/> for nothing. Call on the UI thread.</summary>
    public DiscordActivity? Desired()
    {
        if (!_application.IsSet || !_settings.Current.Playback.DiscordPresence || _engine.Current is not { } source || _engine.State == PlaybackState.Idle)
        {
            return null;
        }

        var track = source.Track;
        var artists = track.Artists.Count > 0 ? string.Join(", ", track.Artists.Select(a => a.Name)) : track.AlbumArtist;
        DateTimeOffset? start = null;
        DateTimeOffset? end = null;
        if (_engine.State is PlaybackState.Playing or PlaybackState.Buffering or PlaybackState.Opening)
        {
            // Discord counts the progress itself from these; while paused it shows none.
            start = _time.GetUtcNow() - _engine.Position;
            end = _engine.Duration > TimeSpan.Zero ? start + _engine.Duration : null;
        }

        return new DiscordActivity(track.Name, artists, track.AlbumName, start, end);
    }

    /// <summary>Sends what Discord should show, unless the same was sent already. Call on the UI thread.</summary>
    public async Task UpdateAsync()
    {
        lock (_gate)
        {
            _wanted = Desired();
            _dirty = true;
            if (_sending)
            {
                // The running send picks it up when it is done.
                return;
            }

            _sending = true;
        }

        while (true)
        {
            DiscordActivity? next;
            lock (_gate)
            {
                if (!_dirty || Equals(_wanted, _sent))
                {
                    _dirty = false;
                    _sending = false;
                    return;
                }

                _dirty = false;
                next = _wanted;
            }

            await SendAsync(next).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _engine.CurrentChanged -= OnChanged;
        _engine.StateChanged -= OnChanged;
        _engine.PositionChanged -= OnPositionChanged;
        _settings.Changed -= OnSettingsChanged;
        _connection.Dispose();
    }

    private async Task SendAsync(DiscordActivity? activity)
    {
        if (!_connection.IsConnected)
        {
            if (activity is null)
            {
                // Nothing shown, nothing to clear.
                SetSent(null);
                return;
            }

            if (_time.GetUtcNow() < RetryAt)
            {
                return;
            }

            try
            {
                await _connection.ConnectAsync(_application.Id).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                RetryAt = _time.GetUtcNow() + RetryDelay;
                LogUnreachable(ex.Message);
                return;
            }
        }

        try
        {
            await _connection.SetActivityAsync(activity).ConfigureAwait(false);
            SetSent(activity);
            if (activity is null)
            {
                // Turned off or stopped: let go of Discord until there is something to show again.
                _connection.Disconnect();
            }
        }
        catch (IOException ex)
        {
            _connection.Disconnect();
            SetSent(null);
            RetryAt = _time.GetUtcNow() + RetryDelay;
            LogUnreachable(ex.Message);
        }
    }

    /// <summary>Gets or sets when to try Discord again; set from the UI thread and from sends on the thread pool.</summary>
    private DateTimeOffset RetryAt
    {
        get
        {
            lock (_gate)
            {
                return _retryAt;
            }
        }

        set
        {
            lock (_gate)
            {
                _retryAt = value;
            }
        }
    }

    private void SetSent(DiscordActivity? activity)
    {
        lock (_gate)
        {
            _sent = activity;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => _ = UpdateAsync();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Playback.DiscordPresence != e.NewSettings.Playback.DiscordPresence)
        {
            RetryAt = DateTimeOffset.MinValue;
            _ = UpdateAsync();
        }
    }

    /// <summary>Several times a second while playing: only a seek (or a failed earlier send) needs a new message.</summary>
    private void OnPositionChanged(object? sender, EventArgs e)
    {
        DiscordActivity? sent;
        lock (_gate)
        {
            sent = _sent;
        }

        var wanted = Desired();
        if (wanted is not null && sent is not null && OnlyDrifted(wanted, sent))
        {
            return;
        }

        _ = UpdateAsync();
    }

    /// <summary>The same track and state, and the progress within <see cref="SeekThreshold"/> of what Discord counts.</summary>
    private static bool OnlyDrifted(DiscordActivity wanted, DiscordActivity sent) =>
        wanted with { Start = sent.Start, End = sent.End } == sent
        && wanted.Start is { } start && sent.Start is { } sentStart
        && (start - sentStart).Duration() < SeekThreshold;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Discord is not reachable: {Reason}")]
    private partial void LogUnreachable(string reason);
}
