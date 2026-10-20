using Groovefin.Core.Playback;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Session;

/// <summary>
/// Saves the queue and the position while the app runs and restores them on the next start, paused,
/// when <see cref="PlaybackSettings.ResumeSession"/> is on and the same user is signed in to the same
/// server. Queue changes are saved after <see cref="SaveDelay"/>, the position at most every
/// <see cref="PositionInterval"/>, and everything once more when the app closes.
/// </summary>
public sealed class SessionPersistence : IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan PositionInterval = TimeSpan.FromSeconds(15);

    private readonly IPlayer _player;
    private readonly IServerManager _servers;
    private readonly ISettingsService _settings;
    private readonly ISessionStore _store;
    private readonly IDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private long _lastSave;
    private bool _started;

    public SessionPersistence(IPlayer player, IServerManager servers, ISettingsService settings, ISessionStore store, IDispatcher dispatcher, TimeProvider time)
    {
        _player = player;
        _servers = servers;
        _settings = settings;
        _store = store;
        _dispatcher = dispatcher;
        _time = time;
        _timer = time.CreateTimer(_ => _ = _dispatcher.EnqueueAsync(() => _ = SaveAsync()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _lastSave = time.GetTimestamp();
    }

    private bool IsEnabled => _settings.Current.Playback.ResumeSession;

    /// <summary>Restores the saved session, if any and allowed, then starts saving. Call once on the UI thread after start-up sign-in.</summary>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsEnabled && Owner() is { } owner && _player.Queue.Items.Count == 0
            && await _store.LoadAsync(cancellationToken) is { } saved
            && saved.ServerId == owner.ServerId && saved.UserId == owner.UserId
            && _player.Queue.Restore(saved.Queue) && _player.Queue.Current is not null)
        {
            _player.Resume(saved.Position);
        }

        Start();
    }

    /// <summary>Saves now (when the app closes). Without a queue, or with resuming off, the saved session is removed.</summary>
    public Task SaveAsync()
    {
        _lastSave = _time.GetTimestamp();
        if (!IsEnabled || Owner() is not { } owner || _player.Queue.Items.Count == 0)
        {
            return _store.ClearAsync();
        }

        return _store.SaveAsync(new SavedSession
        {
            ServerId = owner.ServerId,
            UserId = owner.UserId,
            Queue = _player.Queue.Snapshot(),
            Position = _player.Engine.Current is null ? TimeSpan.Zero : _player.Engine.Position,
        });
    }

    public void Dispose()
    {
        if (_started)
        {
            _player.Queue.Changed -= OnQueueChanged;
            _player.Engine.PositionChanged -= OnPositionChanged;
            _settings.Changed -= OnSettingsChanged;
        }

        _timer.Dispose();
    }

    private void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _player.Queue.Changed += OnQueueChanged;
        _player.Engine.PositionChanged += OnPositionChanged;
        _settings.Changed += OnSettingsChanged;
    }

    private (string ServerId, string UserId)? Owner() =>
        _servers.IsSignedIn && _servers.ActiveServer is { UserId: { } userId } server ? (server.Id, userId) : null;

    private void OnQueueChanged(object? sender, EventArgs e) => _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (_time.GetElapsedTime(_lastSave) >= PositionInterval)
        {
            _ = SaveAsync();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Playback.ResumeSession && !e.NewSettings.Playback.ResumeSession)
        {
            _ = _store.ClearAsync();
        }
    }
}
