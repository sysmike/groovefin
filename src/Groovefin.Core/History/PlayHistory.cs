using System.Collections.Concurrent;
using System.ComponentModel;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.History;

/// <summary>
/// The play history behind "On repeat" (#251), kept by Groovefin because Jellyfin keeps only a play count and
/// a last-played date per track:
/// <list type="bullet">
/// <item>Groovefin's own plays: a track counts once it was listened to for half its length or
/// <see cref="MaxListenTime"/>, whichever is less. Listened time counts, not the position, so seeking does
/// not; nothing is counted in privacy mode.</item>
/// <item>Other devices' plays: every <see cref="SyncInterval"/> the tracks played since the last sync are
/// read from the server. A track's gain in play count is its plays since then, less the starts Groovefin
/// reported itself, timed at its last play. The first sync takes each track played in the last
/// <see cref="Window"/> with its whole play count.</item>
/// <item>Plays while offline (#339): a start the server could not be reached for is kept, and sent with its time
/// (<see cref="IPlaybackReporter.MarkPlayedAsync"/>) once it can be: before each sync, when the network comes back,
/// and after a start reached the server. Each sent play then counts as one Groovefin reported.</item>
/// </list>
/// Each server and user has its own file; plays older than <see cref="Window"/> are dropped. Engine events
/// are handled on the UI thread; loading, saving and syncing run one after another in the background.
/// Create it once at startup and keep it alive.
/// </summary>
public sealed partial class PlayHistory : IDisposable
{
    /// <summary>How far back "On repeat" looks, and how long plays are kept.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>A track counts once listened to for half its length, or this long.</summary>
    public static readonly TimeSpan MaxListenTime = TimeSpan.FromMinutes(4);

    /// <summary>How often the play counts are read from the server.</summary>
    public static readonly TimeSpan SyncInterval = TimeSpan.FromHours(1);

    /// <summary>The first sync after startup waits this long, for the session to sign in.</summary>
    public static readonly TimeSpan FirstSyncDelay = TimeSpan.FromMinutes(1);

    /// <summary>Tracks per request of a sync.</summary>
    public const int SyncPageSize = 100;

    /// <summary>A sync reads at most this many pages; older plays are left to the next one.</summary>
    public const int MaxSyncPages = 10;

    private readonly IPlaybackEngine _engine;
    private readonly PlaybackReporting _reporting;
    private readonly IJellyfinClient _client;
    private readonly ILibraryService _library;
    private readonly IPlayHistoryStore _store;
    private readonly PrivacyMode _privacy;
    private readonly TimeProvider _time;
    private readonly ILogger<PlayHistory> _logger;
    private readonly IPlaybackReporter? _reporter;
    private readonly INetworkStatus? _network;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private Task _queue = Task.CompletedTask;

    // Starts the server accepted, from the reporting's background thread; queued work takes them over.
    private readonly ConcurrentQueue<(string Key, ReportedStart Start)> _reportedStarts = new();

    // Only touched by queued work, which runs one at a time.
    private string? _key;
    private PlayHistoryData _data = new();

    private Listen? _listen;

    public PlayHistory(
        IPlaybackEngine engine,
        PlaybackReporting reporting,
        IJellyfinClient client,
        ILibraryService library,
        IPlayHistoryStore store,
        PrivacyMode privacy,
        TimeProvider time,
        ILogger<PlayHistory> logger,
        IPlaybackReporter? reporter = null,
        INetworkStatus? network = null)
    {
        _engine = engine;
        _reporting = reporting;
        _client = client;
        _library = library;
        _store = store;
        _privacy = privacy;
        _time = time;
        _logger = logger;
        _reporter = reporter;
        _network = network;
        _timer = time.CreateTimer(_ => _ = SyncAsync(), null, FirstSyncDelay, SyncInterval);

        _engine.CurrentChanged += OnCurrentChanged;
        _engine.StateChanged += OnStateChanged;
        _engine.PositionChanged += OnPositionChanged;
        _reporting.StartReported += OnStartReported;
        _reporting.StartUnsent += OnStartUnsent;
        _privacy.PropertyChanged += OnPrivacyChanged;
        if (_network is not null)
        {
            _network.Changed += OnNetworkChanged;
        }
    }

    /// <summary>Gets the "On repeat" list of the signed-in user: empty when not signed in.</summary>
    public Task<IReadOnlyList<OnRepeatEntry>> GetOnRepeatAsync()
    {
        var key = CurrentKey();
        return Enqueue<IReadOnlyList<OnRepeatEntry>>(async () =>
        {
            if (key is null)
            {
                return [];
            }

            var data = await LoadAsync(key).ConfigureAwait(false);
            return OnRepeatRanking.Rank(data.Plays, _time.GetUtcNow(), Window);
        });
    }

    /// <summary>
    /// Reads the plays since the last sync from the server (see the class summary). A failed request is logged
    /// and changes nothing; the next sync tries again.
    /// </summary>
    public Task SyncAsync()
    {
        var key = CurrentKey();
        return Enqueue(async () =>
        {
            if (key is null)
            {
                return;
            }

            // The starts sent so far are in the server's play counts: they must be known before reading them,
            // and so must the plays from offline.
            await _reporting.FlushAsync().ConfigureAwait(false);
            var data = await LoadAsync(key).ConfigureAwait(false);
            if (await SendPendingAsync(key, data).ConfigureAwait(false))
            {
                // Saved at once: a failed read below must not send them again.
                await SaveAsync(key, data).ConfigureAwait(false);
            }

            var now = _time.GetUtcNow();
            var since = data.LastSync ?? now - Window;
            IReadOnlyList<Track> played;
            try
            {
                played = await ReadPlayedSinceAsync(since).ConfigureAwait(false);
            }
            catch (JellyfinException ex)
            {
                LogSyncFailed(ex);
                return;
            }

            Apply(data, played, since, now);
            await SaveAsync(key, data).ConfigureAwait(false);
        });
    }

    /// <summary>Waits until the work queued so far is done (tests, shutdown).</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _queue;
        }
    }

    public void Dispose()
    {
        _engine.CurrentChanged -= OnCurrentChanged;
        _engine.StateChanged -= OnStateChanged;
        _engine.PositionChanged -= OnPositionChanged;
        _reporting.StartReported -= OnStartReported;
        _reporting.StartUnsent -= OnStartUnsent;
        _privacy.PropertyChanged -= OnPrivacyChanged;
        if (_network is not null)
        {
            _network.Changed -= OnNetworkChanged;
        }

        _timer.Dispose();
    }

    /// <summary>The server's gains in play count since <paramref name="since"/>, less Groovefin's own starts.</summary>
    private static void Apply(PlayHistoryData data, IReadOnlyList<Track> played, DateTimeOffset since, DateTimeOffset now)
    {
        var first = data.LastSync is null;
        var reported = data.ReportedStarts.Where(s => s.StartedAt <= now).ToList();
        foreach (var track in played)
        {
            var lastPlayed = Clamp(track.LastPlayed!.Value, since, now);
            var own = reported.Count(s => s.TrackId == track.Id);
            var gained = first
                ? Math.Max(1, track.PlayCount)
                : data.Counts.TryGetValue(track.Id, out var before) ? track.PlayCount - before.PlayCount : 1;

            for (var i = own; i < gained; i++)
            {
                data.Plays.Add(new PlayRecord(track.Id, lastPlayed, PlaySource.Server));
            }

            data.Counts[track.Id] = new PlayCountRecord(track.PlayCount, lastPlayed);
            data.ReportedStarts.RemoveAll(s => s.TrackId == track.Id && reported.Contains(s));
        }

        data.LastSync = now;
        Prune(data, now);
    }

    private static DateTimeOffset Clamp(DateTimeOffset value, DateTimeOffset min, DateTimeOffset max) =>
        value < min ? min : value > max ? max : value;

    private static void Prune(PlayHistoryData data, DateTimeOffset now)
    {
        var cutoff = now - Window;
        data.Plays.RemoveAll(p => p.PlayedAt < cutoff);
        data.ReportedStarts.RemoveAll(s => s.StartedAt < cutoff);
        data.Pending.RemoveAll(p => p.PlayedAt < cutoff);
        foreach (var stale in data.Counts.Where(c => c.Value.LastPlayed < cutoff).Select(c => c.Key).ToList())
        {
            data.Counts.Remove(stale);
        }
    }

    /// <summary>A file name for the signed-in user on the server: their ID and a hash of the server's address.</summary>
    private string? CurrentKey() => _client.Connection?.UserDataKey;

    /// <summary>The tracks played since <paramref name="since"/>, newest first, from as many pages as that takes.</summary>
    private async Task<IReadOnlyList<Track>> ReadPlayedSinceAsync(DateTimeOffset since)
    {
        var played = new List<Track>();
        for (var page = 0; page < MaxSyncPages; page++)
        {
            var result = await _library.GetTracksAsync(new TrackQuery
            {
                StartIndex = page * SyncPageSize,
                Limit = SyncPageSize,
                SortBy = TrackSortField.DatePlayed,
                Descending = true,
            }).ConfigureAwait(false);

            foreach (var track in result.Items)
            {
                if (track.LastPlayed is not { } lastPlayed || lastPlayed < since)
                {
                    return played;
                }

                played.Add(track);
            }

            if (result.Items.Count < SyncPageSize)
            {
                break;
            }
        }

        return played;
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        Count();
        _listen = _engine.Current is { } source
            ? new Listen(source.Track.Id, Threshold(source.Track.Duration ?? _engine.Duration)) { IsPrivate = _privacy.IsOn }
            : null;
        OnStateChanged(sender, e);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_listen is not { } listen)
        {
            return;
        }

        var playing = _engine.State == PlaybackState.Playing;
        if (playing && listen.PlayingSince is null)
        {
            listen.PlayingSince = _time.GetTimestamp();
        }
        else if (!playing && listen.PlayingSince is { } since)
        {
            listen.Listened += _time.GetElapsedTime(since);
            listen.PlayingSince = null;
        }

        Count();
    }

    private void OnPositionChanged(object? sender, EventArgs e) => Count();

    /// <summary>Turned on: the track playing now is not counted, even if privacy mode goes off again.</summary>
    private void OnPrivacyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrivacyMode.IsOn) && _privacy.IsOn && _listen is { } listen)
        {
            listen.IsPrivate = true;
        }
    }

    /// <summary>Records the track playing once it has been listened to long enough.</summary>
    private void Count()
    {
        if (_listen is not { IsCounted: false, IsPrivate: false } listen)
        {
            return;
        }

        var listened = listen.Listened + (listen.PlayingSince is { } since ? _time.GetElapsedTime(since) : TimeSpan.Zero);
        if (listened < listen.Threshold)
        {
            return;
        }

        listen.IsCounted = true;
        var play = new PlayRecord(listen.TrackId, _time.GetUtcNow(), PlaySource.Groovefin);
        Record(data => data.Plays.Add(play));
    }

    /// <summary>
    /// The server counted a play for this start: the next sync must not count it again. It can be reached again,
    /// so the plays from offline go now.
    /// </summary>
    private void OnStartReported(object? sender, string trackId)
    {
        if (CurrentKey() is { } key)
        {
            _reportedStarts.Enqueue((key, new ReportedStart(trackId, _time.GetUtcNow())));
            _ = Enqueue(async () =>
            {
                var data = await LoadAsync(key).ConfigureAwait(false);
                await SendPendingAsync(key, data).ConfigureAwait(false);

                // Also without plays from offline: the reported start is saved.
                await SaveAsync(key, data).ConfigureAwait(false);
            });
        }
    }

    /// <summary>The server missed this start (offline): it is kept and sent later (#339).</summary>
    private void OnStartUnsent(object? sender, UnsentStart start) =>
        Record(data => data.Pending.Add(new PendingPlay(start.TrackId, start.StartedAt)));

    /// <summary>The network is back: the plays from offline go now.</summary>
    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (_network is { IsOnline: true } && CurrentKey() is { } key)
        {
            _ = Enqueue(async () =>
            {
                var data = await LoadAsync(key).ConfigureAwait(false);
                if (await SendPendingAsync(key, data).ConfigureAwait(false))
                {
                    await SaveAsync(key, data).ConfigureAwait(false);
                }
            });
        }
    }

    /// <summary>
    /// Sends the plays from offline, oldest first, and counts each as a start Groovefin reported. An unreachable server
    /// stops them for now; a play the server refuses (a song it no longer has) is dropped. Run it in the queue.
    /// Returns whether any left the list.
    /// </summary>
    private async Task<bool> SendPendingAsync(string key, PlayHistoryData data)
    {
        if (_reporter is null || data.Pending.Count == 0)
        {
            return false;
        }

        var before = data.Pending.Count;

        foreach (var play in data.Pending.ToList())
        {
            try
            {
                await _reporter.MarkPlayedAsync(play.TrackId, play.PlayedAt).ConfigureAwait(false);
                data.ReportedStarts.Add(new ReportedStart(play.TrackId, _time.GetUtcNow()));
            }
            catch (JellyfinException ex) when (ex.StatusCode is null)
            {
                LogPendingWaits(ex, data.Pending.Count);
                return data.Pending.Count < before;
            }
            catch (Exception ex) when (ex is JellyfinException or ArgumentException or InvalidOperationException)
            {
                LogPendingDropped(ex, play.TrackId);
            }

            data.Pending.Remove(play);
        }

        LogPendingSent(key);
        return true;
    }

    private static TimeSpan Threshold(TimeSpan duration) =>
        duration > TimeSpan.Zero && duration / 2 < MaxListenTime ? duration / 2 : MaxListenTime;

    /// <summary>Changes and saves the signed-in user's history in the background.</summary>
    private void Record(Action<PlayHistoryData> change)
    {
        var key = CurrentKey();
        if (key is null)
        {
            return;
        }

        _ = Enqueue(async () =>
        {
            var data = await LoadAsync(key).ConfigureAwait(false);
            change(data);
            await SaveAsync(key, data).ConfigureAwait(false);
        });
    }

    /// <summary>The history of <paramref name="key"/>, from the file the first time; older plays dropped.</summary>
    private async Task<PlayHistoryData> LoadAsync(string key)
    {
        if (_key != key)
        {
            _data = await _store.LoadAsync(key).ConfigureAwait(false) ?? new PlayHistoryData();
            _key = key;
        }

        // A start reported just before signing in to another server or user is dropped.
        while (_reportedStarts.TryDequeue(out var reported))
        {
            if (reported.Key == key)
            {
                _data.ReportedStarts.Add(reported.Start);
            }
        }

        Prune(_data, _time.GetUtcNow());
        return _data;
    }

    private Task SaveAsync(string key, PlayHistoryData data) => _store.SaveAsync(key, data);

    private Task Enqueue(Func<Task> work) => Enqueue(async () =>
    {
        await work().ConfigureAwait(false);
        return true;
    });

    /// <summary>Queues work behind the earlier work; failures are logged, never thrown into the queue.</summary>
    private Task<T> Enqueue<T>(Func<Task<T>> work)
    {
        lock (_gate)
        {
            var result = _queue.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            _queue = result.ContinueWith(
                t =>
                {
                    if (t.Exception is { } ex)
                    {
                        LogWorkFailed(ex.GetBaseException());
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            return result;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The server cannot be reached yet: {Count} plays from offline wait")]
    private partial void LogPendingWaits(Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The server refused the play of {TrackId} from offline; it is dropped")]
    private partial void LogPendingDropped(Exception exception, string trackId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent the plays from offline of {Key}")]
    private partial void LogPendingSent(string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the play counts from the server failed")]
    private partial void LogSyncFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Updating the play history failed")]
    private partial void LogWorkFailed(Exception exception);

    /// <summary>One load of a track: how long it has been listened to.</summary>
    private sealed class Listen(string trackId, TimeSpan threshold)
    {
        public string TrackId { get; } = trackId;

        public TimeSpan Threshold { get; } = threshold;

        public TimeSpan Listened { get; set; }

        /// <summary>Gets or sets the timestamp since which it has been playing, or <see langword="null"/> while it is not.</summary>
        public long? PlayingSince { get; set; }

        public bool IsCounted { get; set; }

        public bool IsPrivate { get; set; }
    }
}
