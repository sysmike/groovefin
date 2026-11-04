using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Models;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Downloads;

/// <summary>Why the downloads wait (#327).</summary>
public enum DownloadStop
{
    /// <summary>Nothing stops them: all is downloaded, or a sync runs.</summary>
    None,

    NotSignedIn,

    /// <summary>The PC has no network.</summary>
    Offline,

    /// <summary>The server could not be reached.</summary>
    Unreachable,

    /// <summary>The connection is metered, and downloading there is off.</summary>
    Metered,

    /// <summary>Less than <see cref="DownloadManager.MinFreeBytes"/> are free on the drive.</summary>
    LowDisk,

    /// <summary>The server refused to list the favourites or playlists.</summary>
    LoadFailed,
}

/// <summary>A song that failed to download, why (not localized: an HTTP status or the error), and how often.</summary>
public sealed record DownloadFailure(string TrackId, string TrackName, string Reason, int Attempts);

/// <summary>Where the downloads are: for the Settings page (#327).</summary>
public sealed record DownloadStatus
{
    public static readonly DownloadStatus Empty = new();

    public bool IsSyncing { get; init; }

    /// <summary>Gets the number of wanted songs that have their file in the current format.</summary>
    public int Downloaded { get; init; }

    /// <summary>Gets the number of songs the collections that are on hold, at the last sync.</summary>
    public int Wanted { get; init; }

    /// <summary>Gets the size of all downloaded files.</summary>
    public long Bytes { get; init; }

    /// <summary>Gets the name of a song downloading now, or <see langword="null"/>.</summary>
    public string? Current { get; init; }

    public IReadOnlyList<DownloadFailure> Failures { get; init; } = [];

    public DownloadStop Stopped { get; init; }
}

/// <summary>
/// Keeps the collections that are on in Settings → Downloads on disk (#58, #327): favourite songs and all
/// playlists. A sync reads the wanted songs from the server, deletes the files no collection needs any more,
/// and downloads the missing ones, <see cref="Parallel"/> at a time, each to a <c>.part</c> file that is renamed
/// when complete. An interrupted original resumes where it stopped. A song shared by several collections is
/// kept once.
/// <para>
/// Syncs run one after another in the background: <see cref="FirstSyncDelay"/> after start-up, every
/// <see cref="SyncInterval"/>, shortly after a change to favourites, playlists or the settings, when the network
/// comes back, after a server or user change, and on <see cref="SyncNowAsync"/>. Nothing downloads while
/// offline, on a metered connection unless allowed, or with less than <see cref="MinFreeBytes"/> free. A song
/// that failed <see cref="MaxAttempts"/> times waits for <see cref="SyncNowAsync"/>.
/// </para>
/// </summary>
public sealed partial class DownloadManager : IDisposable
{
    /// <summary>The favourite songs read per sync, at most.</summary>
    public const int MaxFavouriteSongs = 10_000;

    /// <summary>Songs downloading at a time.</summary>
    public const int Parallel = 2;

    /// <summary>A song that failed this often is tried again only on <see cref="SyncNowAsync"/>.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Downloads stop when less is free on the drive (1 GB).</summary>
    public const long MinFreeBytes = 1L << 30;

    public static readonly TimeSpan FirstSyncDelay = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(30);

    /// <summary>A change syncs this long after it, so a run of changes syncs once.</summary>
    public static readonly TimeSpan ChangeDelay = TimeSpan.FromSeconds(3);

    /// <summary>A download that receives nothing for this long fails.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(1);

    private const string PartExtension = ".part";

    private readonly IJellyfinClient _client;
    private readonly ILibraryService _library;
    private readonly IPlaylistService _playlists;
    private readonly IDownloadSource _source;
    private readonly IDownloadStore _store;
    private readonly ISettingsService _settings;
    private readonly INetworkStatus _network;
    private readonly IServerManager _servers;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _time;
    private readonly ILogger<DownloadManager> _logger;
    private readonly ITimer _interval;
    private readonly ITimer _soon;

    private readonly Lock _gate = new();
    private CancellationTokenSource _cancel = new();
    private Task _worker = Task.CompletedTask;
    private bool _running;
    private bool _pending;
    private bool _pendingManual;
    private bool _pendingAllowMetered;
    private bool _disposed;

    // The loaded index and whose it is: changed and saved only under _indexGate, one at a time; each change
    // also takes _indexLock, so Publish can count on any thread meanwhile.
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private readonly Lock _indexLock = new();
    private string? _key;
    private DownloadIndex _index = new();

    private readonly ConcurrentDictionary<string, DownloadFailure> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _current = new(StringComparer.Ordinal);
    private DownloadStatus _status = DownloadStatus.Empty;
    private volatile bool _syncing;
    private volatile DownloadStop _stopped;

    public DownloadManager(
        IJellyfinClient client,
        ILibraryService library,
        IPlaylistService playlists,
        IDownloadSource source,
        IDownloadStore store,
        ISettingsService settings,
        INetworkStatus network,
        IServerManager servers,
        IMessenger messenger,
        TimeProvider time,
        ILogger<DownloadManager> logger)
    {
        _client = client;
        _library = library;
        _playlists = playlists;
        _source = source;
        _store = store;
        _settings = settings;
        _network = network;
        _servers = servers;
        _messenger = messenger;
        _time = time;
        _logger = logger;
        _interval = time.CreateTimer(_ => _ = RequestAsync(manual: false), null, FirstSyncDelay, SyncInterval);
        _soon = time.CreateTimer(_ => _ = RequestAsync(manual: false), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _settings.Changed += OnSettingsChanged;
        _network.Changed += OnNetworkChanged;
        _servers.Changed += OnServersChanged;
        messenger.Register<DownloadManager, FavouriteChangedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: true));
        messenger.Register<DownloadManager, PlaylistChangedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));
        messenger.Register<DownloadManager, PlaylistTracksAddedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));
        messenger.Register<DownloadManager, PlaylistDeletedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));
    }

    /// <summary>Raised on any thread after <see cref="Status"/> changed.</summary>
    public event EventHandler? StatusChanged;

    public DownloadStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>Syncs at once, as the timer does: a song that failed <see cref="MaxAttempts"/> times waits.</summary>
    public Task SyncAsync() => RequestAsync(manual: false);

    /// <summary>
    /// Syncs at once and tries every failed song again (Settings → Sync now); <paramref name="allowMetered"/>
    /// downloads on a metered connection this time although the setting is off.
    /// </summary>
    public Task SyncNowAsync(bool allowMetered = false) => RequestAsync(manual: true, allowMetered);

    /// <summary>Reads the signed-in user's index, so <see cref="Status"/> shows what is on disk before the first sync.</summary>
    public async Task RefreshStatusAsync()
    {
        if (_client.Connection?.UserDataKey is not { } key)
        {
            Publish(null, DownloadStop.NotSignedIn);
            return;
        }

        await _indexGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await LoadIndexAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _indexGate.Release();
        }

        Publish(key, _stopped);
    }

    /// <summary>Stops the sync that runs and deletes all of the signed-in user's downloads.</summary>
    public async Task RemoveAllAsync()
    {
        await StopRunningAsync().ConfigureAwait(false);
        if (_client.Connection?.UserDataKey is not { } key)
        {
            return;
        }

        await _indexGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _store.DeleteAll(key);
            Reset(key);
        }
        finally
        {
            _indexGate.Release();
        }

        LogRemovedAll();
        Publish(key, DownloadStop.None);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _cancel.Cancel();
        }

        _interval.Dispose();
        _soon.Dispose();
        _settings.Changed -= OnSettingsChanged;
        _network.Changed -= OnNetworkChanged;
        _servers.Changed -= OnServersChanged;
        _messenger.UnregisterAll(this);
    }

    private void OnCollectionChanged(bool favourites)
    {
        var downloads = _settings.Current.Downloads;
        if (favourites ? downloads.FavouriteSongs : downloads.Playlists)
        {
            SyncSoon();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Downloads != e.NewSettings.Downloads)
        {
            // A switch turned off stops its downloads, a new format starts over.
            _ = StopRunningAsync();
            SyncSoon();
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (_network.IsOnline)
        {
            SyncSoon();
        }
    }

    private void OnServersChanged(object? sender, EventArgs e)
    {
        _ = StopRunningAsync();
        SyncSoon();
    }

    private void SyncSoon()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _soon.Change(ChangeDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Asks for a sync. One runs at a time; a request while one runs syncs again after it.</summary>
    private Task RequestAsync(bool manual, bool allowMetered = false)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _pending = true;
            _pendingManual |= manual;
            _pendingAllowMetered |= allowMetered;
            if (!_running)
            {
                _running = true;
                _worker = Task.Run(WorkAsync);
            }

            return _worker;
        }
    }

    private async Task WorkAsync()
    {
        while (true)
        {
            bool manual, allowMetered;
            CancellationToken cancellationToken;
            lock (_gate)
            {
                if (!_pending || _disposed)
                {
                    _running = false;
                    return;
                }

                _pending = false;
                manual = _pendingManual;
                allowMetered = _pendingAllowMetered;
                _pendingManual = false;
                _pendingAllowMetered = false;
                cancellationToken = _cancel.Token;
            }

            try
            {
                await RunAsync(manual, allowMetered, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stopped for a change: the next request syncs with it.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogSyncFailed(ex);
            }
            finally
            {
                _current.Clear();
                _syncing = false;
            }

            Publish(_client.Connection?.UserDataKey, _stopped);
        }
    }

    /// <summary>Cancels the sync that runs and those asked for, and waits until it has stopped.</summary>
    private async Task StopRunningAsync()
    {
        Task worker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var cancel = _cancel;
            _cancel = new CancellationTokenSource();
            cancel.Cancel();
            _pending = false;
            _pendingManual = false;
            _pendingAllowMetered = false;
            worker = _worker;
        }

        await worker.ConfigureAwait(false);
    }

    private async Task RunAsync(bool manual, bool allowMetered, CancellationToken cancellationToken)
    {
        if (_client.Connection?.UserDataKey is not { } key)
        {
            _stopped = DownloadStop.NotSignedIn;
            return;
        }

        if (manual)
        {
            _failures.Clear();
        }

        var settings = _settings.Current.Downloads;
        _stopped = DownloadStop.None;
        if (!settings.AnyCollection)
        {
            // Everything off: nothing is wanted any more, offline too.
            await RemoveFolderAsync(key, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_network.IsOnline)
        {
            _stopped = DownloadStop.Offline;
            return;
        }

        _syncing = true;
        Publish(key, DownloadStop.None);
        Wanted wanted;
        try
        {
            wanted = await LoadWantedAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (JellyfinException ex)
        {
            LogLoadFailed(ex);
            _stopped = ex.StatusCode is null ? DownloadStop.Unreachable : DownloadStop.LoadFailed;
            return;
        }

        var format = DownloadFormat.From(settings);
        var index = await UpdateIndexAsync(key, wanted, cancellationToken).ConfigureAwait(false);
        var missing = new ConcurrentQueue<Track>(wanted.Tracks.Values.Where(track =>
            (!index.Tracks.TryGetValue(track.Id, out var downloaded) || downloaded.Format != format.Key)
            && !(_failures.TryGetValue(track.Id, out var failure) && failure.Attempts >= MaxAttempts)));
        Publish(key, DownloadStop.None);
        if (missing.IsEmpty)
        {
            return;
        }

        if (_network.IsMetered && !settings.AllowMetered && !allowMetered)
        {
            _stopped = DownloadStop.Metered;
            return;
        }

        LogDownloading(missing.Count, format.Key);
        var stop = DownloadStop.None;
        async Task DownloadAllAsync()
        {
            while (stop == DownloadStop.None && missing.TryDequeue(out var track))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_store.FreeSpace(key) < MinFreeBytes)
                {
                    stop = DownloadStop.LowDisk;
                }
                else if (!_network.IsOnline)
                {
                    stop = DownloadStop.Offline;
                }
                else if (!await DownloadAsync(key, index, track, format, cancellationToken).ConfigureAwait(false))
                {
                    stop = DownloadStop.Unreachable;
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => DownloadAllAsync())).ConfigureAwait(false);
        _stopped = stop;
    }

    private async Task<Wanted> LoadWantedAsync(DownloadSettings settings, CancellationToken cancellationToken)
    {
        var tracks = new Dictionary<string, Track>(StringComparer.Ordinal);
        List<string> favourites = [];
        List<DownloadedPlaylist> playlists = [];
        if (settings.FavouriteSongs)
        {
            foreach (var track in await _library.GetFavouriteTracksAsync(MaxFavouriteSongs, cancellationToken).ConfigureAwait(false))
            {
                tracks.TryAdd(track.Id, track);
                favourites.Add(track.Id);
            }
        }

        if (settings.Playlists)
        {
            foreach (var playlist in await _playlists.GetPlaylistsAsync(cancellationToken).ConfigureAwait(false))
            {
                var entries = await _playlists.GetEntriesAsync(playlist.Id, cancellationToken).ConfigureAwait(false);
                foreach (var entry in entries)
                {
                    tracks.TryAdd(entry.Track.Id, entry.Track);
                }

                playlists.Add(new DownloadedPlaylist { Playlist = playlist, TrackIds = [.. entries.Select(e => e.Track.Id)] });
            }
        }

        return new Wanted(tracks, favourites, playlists);
    }

    /// <summary>
    /// Saves the wanted collections, gives the kept songs the server's metadata, and deletes the files nothing
    /// wants any more, also ones the index does not know (left from a lost index).
    /// </summary>
    private async Task<DownloadIndex> UpdateIndexAsync(string key, Wanted wanted, CancellationToken cancellationToken)
    {
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadIndexAsync(key, cancellationToken).ConfigureAwait(false);
            var folder = _store.FolderOf(key);
            foreach (var (id, downloaded) in index.Tracks.ToList())
            {
                if (wanted.Tracks.TryGetValue(id, out var track))
                {
                    lock (_indexLock)
                    {
                        index.Tracks[id] = downloaded with { Track = track };
                    }
                }
                else
                {
                    TryDelete(Path.Join(folder, downloaded.FileName));
                    lock (_indexLock)
                    {
                        index.Tracks.Remove(id);
                    }
                }
            }

            if (Directory.Exists(folder))
            {
                var kept = index.Tracks.Values.Select(t => t.FileName)
                    .Concat(wanted.Tracks.Keys.Select(id => id + PartExtension))
                    .Append(JsonDownloadStore.IndexFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(folder).Where(f => !kept.Contains(Path.GetFileName(f))))
                {
                    TryDelete(file);
                }
            }

            lock (_indexLock)
            {
                index.FavouriteSongs = wanted.Favourites;
                index.Playlists = wanted.Playlists;
                index.LastSync = _time.GetUtcNow();
            }
            await _store.SaveAsync(key, index, cancellationToken).ConfigureAwait(false);
            return index;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>Downloads one song; <see langword="false"/> when the server could not be reached, which stops the sync.</summary>
    private async Task<bool> DownloadAsync(string key, DownloadIndex index, Track track, DownloadFormat format, CancellationToken cancellationToken)
    {
        var folder = _store.FolderOf(key);
        var part = Path.Join(folder, track.Id + PartExtension);
        _current[track.Id] = track.Name;
        Publish(key, DownloadStop.None);
        try
        {
            Directory.CreateDirectory(folder);
            var offset = format.Kind == DownloadFormatKind.Original && File.Exists(part) ? new FileInfo(part).Length : 0;
            using var response = await _source.OpenAsync(track.Id, format, offset, cancellationToken).ConfigureAwait(false);
            var resumed = offset > 0 && response.Resumed;
            await using (var file = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await CopyAsync(response.Content, file, cancellationToken).ConfigureAwait(false);
            }

            var size = new FileInfo(part).Length;
            if (response.Length is { } length && size != (resumed ? offset : 0) + length)
            {
                // The .part stays: the next try resumes it.
                throw new IOException($"The download ended after {size} bytes.");
            }

            var fileName = track.Id + response.Extension;
            File.Move(part, Path.Join(folder, fileName), overwrite: true);
            await _indexGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (index.Tracks.TryGetValue(track.Id, out var old) && !string.Equals(old.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(Path.Join(folder, old.FileName));
                }

                lock (_indexLock)
                {
                    index.Tracks[track.Id] = new DownloadedTrack
                    {
                        Track = track,
                        FileName = fileName,
                        Size = size,
                        Format = format.Key,
                        DownloadedAt = _time.GetUtcNow(),
                    };
                }

                await _store.SaveAsync(key, index, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _indexGate.Release();
            }

            _failures.TryRemove(track.Id, out _);
            LogDownloaded(track.Id, size);
            return true;
        }
        catch (JellyfinException ex) when (ex.StatusCode is null)
        {
            // Not the song's fault: it is not counted, and the next sync tries again.
            LogUnreachable(ex, track.Id);
            return false;
        }
        catch (Exception ex) when (ex is JellyfinException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            var reason = ex is JellyfinException { StatusCode: { } status } ? $"HTTP {(int)status}" : ex.Message;
            var failure = _failures.AddOrUpdate(
                track.Id,
                _ => new DownloadFailure(track.Id, track.Name, reason, 1),
                (_, previous) => previous with { Reason = reason, Attempts = previous.Attempts + 1 });
            LogDownloadFailed(ex, track.Id, failure.Attempts);
            return true;
        }
        finally
        {
            _current.TryRemove(track.Id, out _);
            Publish(key, DownloadStop.None);
        }
    }

    /// <summary>Copies the download; one that receives nothing for <see cref="IdleTimeout"/> fails.</summary>
    private static async Task CopyAsync(Stream from, Stream to, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (true)
        {
            idle.CancelAfter(IdleTimeout);
            int read;
            try
            {
                read = await from.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException("The download received nothing for a minute.");
            }

            if (read == 0)
            {
                return;
            }

            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Gets the user's index, loading it when another user's (or none) is loaded. Call under <see cref="_indexGate"/>.</summary>
    private async Task<DownloadIndex> LoadIndexAsync(string key, CancellationToken cancellationToken)
    {
        if (_key != key)
        {
            var index = await _store.LoadAsync(key, cancellationToken).ConfigureAwait(false);
            lock (_indexLock)
            {
                _index = index;
                _key = key;
            }

            _failures.Clear();
        }

        return _index;
    }

    /// <summary>Deletes the user's folder, when there is one (nothing is on). Takes <see cref="_indexGate"/> itself.</summary>
    private async Task RemoveFolderAsync(string key, CancellationToken cancellationToken)
    {
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(_store.FolderOf(key)))
            {
                _store.DeleteAll(key);
                LogRemovedAll();
            }

            Reset(key);
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>Makes the loaded index the user's empty one. Call under <see cref="_indexGate"/>.</summary>
    private void Reset(string key)
    {
        lock (_indexLock)
        {
            _key = key;
            _index = new DownloadIndex();
        }

        _failures.Clear();
    }

    /// <summary>Computes <see cref="Status"/> from the loaded index and raises <see cref="StatusChanged"/>.</summary>
    private void Publish(string? key, DownloadStop stopped)
    {
        var format = DownloadFormat.From(_settings.Current.Downloads).Key;
        bool loaded;
        int wanted = 0, downloaded = 0;
        long bytes = 0;
        lock (_indexLock)
        {
            loaded = key is not null && key == _key;
            if (loaded)
            {
                var index = _index;
                var ids = index.FavouriteSongs.Concat(index.Playlists.SelectMany(p => p.TrackIds)).ToHashSet(StringComparer.Ordinal);
                wanted = ids.Count;
                downloaded = ids.Count(id => index.Tracks.TryGetValue(id, out var t) && t.Format == format);
                bytes = index.Tracks.Values.Sum(t => t.Size);
            }
        }

        DownloadStatus status;
        if (!loaded)
        {
            status = DownloadStatus.Empty with { IsSyncing = _syncing, Stopped = stopped };
        }
        else
        {
            status = new DownloadStatus
            {
                IsSyncing = _syncing,
                Wanted = wanted,
                Downloaded = downloaded,
                Bytes = bytes,
                Current = _current.Values.FirstOrDefault(),
                Failures = [.. _failures.Values.OrderBy(f => f.TrackName, StringComparer.CurrentCulture)],
                Stopped = stopped,
            };
        }

        lock (_gate)
        {
            if (status == _status)
            {
                return;
            }

            _status = status;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or locked: the next sync deletes it.
            LogDeleteFailed(ex, Path.GetFileName(path));
        }
    }

    private sealed record Wanted(Dictionary<string, Track> Tracks, IReadOnlyList<string> Favourites, IReadOnlyList<DownloadedPlaylist> Playlists);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading {Count} songs as {Format}")]
    private partial void LogDownloading(int count, string format);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded {TrackId} ({Bytes} bytes)")]
    private partial void LogDownloaded(string trackId, long bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloading {TrackId} failed (attempt {Attempts})")]
    private partial void LogDownloadFailed(Exception exception, string trackId, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "The server was unreachable while downloading {TrackId}")]
    private partial void LogUnreachable(Exception exception, string trackId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the songs to download failed")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A download sync failed")]
    private partial void LogSyncFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "All downloads removed")]
    private partial void LogRemovedAll();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting the download {FileName} failed")]
    private partial void LogDeleteFailed(Exception exception, string fileName);
}
