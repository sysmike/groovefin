using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Images;
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

/// <summary>What turning on a collection would download beyond the files on disk: songs, and about how many bytes (#331).</summary>
public sealed record DownloadEstimate(int Songs, long Bytes);

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
/// Keeps the collections that are on in Settings → Downloads on disk (#58, #327, #331): favourite songs, every
/// song of the favourite albums and artists, and all playlists. A sync reads the wanted songs from the server, deletes the files no collection needs any more,
/// and downloads the missing ones, <see cref="Parallel"/> at a time, each to a <c>.part</c> file that is renamed
/// when complete. An interrupted original resumes where it stopped. A song shared by several collections is
/// kept once; one whose file changed on the server (another container or size) is downloaded again. The
/// downloads of a server that left the list are deleted. Each sync also keeps one cover per downloaded album,
/// artist and playlist (<see cref="CoverWidth"/> wide), which the image cache shows when the server cannot be
/// reached (#335).
/// <para>
/// Syncs run one after another in the background: <see cref="FirstSyncDelay"/> after start-up, every
/// <see cref="SyncInterval"/>, shortly after a change to favourites, playlists or the settings, when the network
/// comes back, after a server or user change, and on <see cref="SyncNowAsync"/>. Nothing downloads while
/// offline, on a metered connection unless allowed, or with less than <see cref="MinFreeBytes"/> free. A song
/// that failed <see cref="MaxAttempts"/> times waits for <see cref="SyncNowAsync"/>.
/// </para>
/// </summary>
public sealed partial class DownloadManager : IOfflineLibrary, IImageFallback, IDisposable
{
    /// <summary>The favourite songs read per sync, at most.</summary>
    public const int MaxFavouriteSongs = 10_000;

    /// <summary>The favourite albums, and the favourite artists, read per sync, at most.</summary>
    public const int MaxFavouriteItems = 2_000;

    /// <summary>Turning on a collection that downloads more asks first (1 GB).</summary>
    public const long LargeDownloadBytes = 1L << 30;

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

    /// <summary>How long the first song after start-up waits for the index (see <see cref="FileOf"/>).</summary>
    public static readonly TimeSpan IndexWait = TimeSpan.FromSeconds(2);

    /// <summary>The width of the covers kept with the downloads (#335); the pages scale them to their size.</summary>
    public const int CoverWidth = 512;

    /// <summary>A download that receives nothing for this long fails.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(1);

    private const string PartExtension = ".part";

    private const string CoverExtension = ".img";

    private const string AacExtension = ".aac";

    private const string Mp4Extension = ".m4a";

    /// <summary>A song's size per second without a size from the server: CD quality, 1,411 kbit/s.</summary>
    private const double CdBytesPerSecond = 176_400;

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
    private readonly IImageSource? _images;
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
    private (string? Key, int Songs, long Bytes) _downloads;
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
        ILogger<DownloadManager> logger,
        IImageSource? images = null)
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
        _images = images;
        _interval = time.CreateTimer(_ => _ = RequestAsync(manual: false), null, FirstSyncDelay, SyncInterval);
        _soon = time.CreateTimer(_ => _ = RequestAsync(manual: false), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _settings.Changed += OnSettingsChanged;
        _network.Changed += OnNetworkChanged;
        _servers.Changed += OnServersChanged;
        messenger.Register<DownloadManager, FavouriteChangedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: true));
        messenger.Register<DownloadManager, PlaylistChangedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));
        messenger.Register<DownloadManager, PlaylistTracksAddedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));
        messenger.Register<DownloadManager, PlaylistDeletedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));

        // The player asks for files from the first song on (#333).
        _ = RefreshStatusAsync();
    }

    /// <summary>Raised on any thread after <see cref="Status"/> changed.</summary>
    public event EventHandler? StatusChanged;

    public event EventHandler? Changed;

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

    /// <summary>
    /// Gets the song's downloaded file (#333): any format, as long as it is on disk. Reads the user's index first
    /// when none is loaded yet (the first song after start-up).
    /// </summary>
    public string? FileOf(string trackId)
    {
        if (_client.Connection?.UserDataKey is not { } key)
        {
            return null;
        }

        EnsureLoaded(key);
        string? fileName;
        lock (_indexLock)
        {
            fileName = _key == key && _index.Tracks.TryGetValue(trackId, out var downloaded) ? downloaded.FileName : null;
        }

        if (fileName is null)
        {
            return null;
        }

        var path = Path.Join(_store.FolderOf(key), fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The cover kept with the downloads (#335): the item's, or with its tag changed meanwhile its older one.</summary>
    public string? Find(ImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != ImageKind.Primary || _client.Connection?.UserDataKey is not { } key)
        {
            return null;
        }

        var folder = _store.CoversOf(key);
        var exact = Path.Join(folder, CoverName(request.ItemId, request.Tag));
        if (File.Exists(exact))
        {
            return exact;
        }

        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, request.ItemId + "_*" + CoverExtension).FirstOrDefault() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool IsDownloaded(string trackId)
    {
        if (_client.Connection?.UserDataKey is not { } key)
        {
            return false;
        }

        EnsureLoaded(key);
        lock (_indexLock)
        {
            return _key == key && _index.Tracks.ContainsKey(trackId);
        }
    }

    public bool IsComplete(string itemId)
    {
        if (_client.Connection?.UserDataKey is not { } key)
        {
            return false;
        }

        EnsureLoaded(key);
        lock (_indexLock)
        {
            if (_key != key)
            {
                return false;
            }

            var songs = _index.Albums.FirstOrDefault(a => a.Album.Id == itemId)?.TrackIds
                ?? _index.Artists.FirstOrDefault(a => a.Artist.Id == itemId)?.TrackIds
                ?? _index.Playlists.FirstOrDefault(p => p.Playlist.Id == itemId)?.TrackIds;
            return songs is { Count: > 0 } && songs.All(_index.Tracks.ContainsKey);
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

    /// <summary>
    /// Reads what <paramref name="settings"/> would download beyond the files on disk now (#331): the songs, and
    /// their size from the server's files (the original) or from their length at the bitrate (AAC).
    /// </summary>
    /// <exception cref="JellyfinException">The server could not list the songs.</exception>
    public async Task<DownloadEstimate> EstimateAsync(DownloadSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_client.Connection?.UserDataKey is not { } key)
        {
            return new DownloadEstimate(0, 0);
        }

        var wanted = await LoadWantedAsync(settings, cancellationToken).ConfigureAwait(false);
        var format = DownloadFormat.From(settings);
        List<Track> missing;
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadIndexAsync(key, cancellationToken).ConfigureAwait(false);
            lock (_indexLock)
            {
                missing = [.. wanted.Tracks.Values.Where(track => NeedsDownload(index, wanted, track.Id, format))];
            }
        }
        finally
        {
            _indexGate.Release();
        }

        return new DownloadEstimate(missing.Count, missing.Sum(track => EstimatedSize(track, wanted.Files.GetValueOrDefault(track.Id), format)));
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
        if (favourites ? downloads.AnyFavourites : downloads.Playlists)
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
        _ = RemoveOtherServersAsync();
        _ = RefreshStatusAsync();
        SyncSoon();
    }

    /// <summary>
    /// Loads the user's index at once when none is (only the first song after start-up or a user change gets here
    /// before <see cref="RefreshStatusAsync"/> is done). The gate is held only briefly, by loads and saves that never
    /// wait for the UI thread, so the wait is short; after <see cref="IndexWait"/> the song streams instead.
    /// </summary>
    private void EnsureLoaded(string key)
    {
        lock (_indexLock)
        {
            if (_key == key)
            {
                return;
            }
        }

        if (!_indexGate.Wait(IndexWait))
        {
            return;
        }

        try
        {
            // Every await below runs without the caller's context, so waiting here cannot deadlock.
            LoadIndexAsync(key, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>
    /// Deletes the downloads of servers no longer in the list: removed, or at another address now (their songs then
    /// download again under the new one).
    /// </summary>
    private async Task RemoveOtherServersAsync()
    {
        var known = _servers.Servers.Select(p => ServerConnection.ServerKeyOf(p.Address)).ToList();
        await _indexGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var key in _store.Keys().Where(k => !known.Any(server => k.EndsWith("-" + server, StringComparison.Ordinal))))
            {
                try
                {
                    _store.DeleteAll(key);
                    LogServerRemoved(key);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Logged by the store; the next change tries again.
                }

                if (key == _key)
                {
                    lock (_indexLock)
                    {
                        _key = null;
                        _index = new DownloadIndex();
                    }
                }
            }
        }
        finally
        {
            _indexGate.Release();
        }
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
        var missing = new ConcurrentQueue<Track>(wanted.Tracks.Values.Where(track => NeedsDownload(index, wanted, track.Id, format) && !GaveUp(track.Id)));
        Publish(key, DownloadStop.None);
        if (missing.IsEmpty)
        {
            await KeepCoversAsync(key, index, cancellationToken).ConfigureAwait(false);
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
                else if (!await DownloadAsync(key, index, track, wanted.Files.GetValueOrDefault(track.Id), format, cancellationToken).ConfigureAwait(false))
                {
                    stop = DownloadStop.Unreachable;
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => DownloadAllAsync())).ConfigureAwait(false);
        _stopped = stop;
        if (stop == DownloadStop.None)
        {
            await KeepCoversAsync(key, index, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Wanted> LoadWantedAsync(DownloadSettings settings, CancellationToken cancellationToken)
    {
        var tracks = new Dictionary<string, Track>(StringComparer.Ordinal);
        List<string> favourites = [];
        List<DownloadedPlaylist> playlists = [];
        List<DownloadedAlbum> albums = [];
        List<DownloadedArtist> artists = [];
        void Add(IEnumerable<Track> songs)
        {
            foreach (var song in songs)
            {
                tracks.TryAdd(song.Id, song);
            }
        }

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

        if (settings.FavouriteAlbums)
        {
            var favourite = await _library.GetAlbumsAsync(new AlbumQuery { FavouritesOnly = true, Limit = MaxFavouriteItems }, cancellationToken).ConfigureAwait(false);
            foreach (var album in favourite.Items)
            {
                var songs = await _library.GetAlbumTracksAsync(album.Id, cancellationToken).ConfigureAwait(false);
                Add(songs);
                albums.Add(new DownloadedAlbum { Album = album, TrackIds = [.. songs.Select(t => t.Id)] });
            }
        }

        if (settings.FavouriteArtists)
        {
            // Every artist, not only album artists: a heart on a featured artist counts too.
            var query = new ArtistQuery { FavouritesOnly = true, AlbumArtistsOnly = false, Limit = MaxFavouriteItems };
            var favourite = await _library.GetArtistsAsync(query, cancellationToken).ConfigureAwait(false);
            foreach (var artist in favourite.Items)
            {
                var songs = await _library.GetArtistTracksAsync(artist.Id, cancellationToken).ConfigureAwait(false);
                Add(songs);
                artists.Add(new DownloadedArtist { Artist = artist, TrackIds = [.. songs.Select(t => t.Id)] });
            }
        }

        // The files on the server: a changed one downloads the song again, and their sizes tell how much a
        // collection downloads.
        var files = tracks.Count > 0
            ? await _library.GetTrackFilesAsync([.. tracks.Keys], cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, TrackFile>();
        return new Wanted(tracks, favourites, playlists, albums, artists, files);
    }

    /// <summary>
    /// Whether the song has no file yet, one in another format, or one the server has since replaced. An original
    /// Windows cannot play, kept as it was before #362, comes again as AAC.
    /// </summary>
    private static bool NeedsDownload(DownloadIndex index, Wanted wanted, string trackId, DownloadFormat format)
    {
        if (!index.Tracks.TryGetValue(trackId, out var downloaded) || downloaded.Format != format.Key)
        {
            return true;
        }

        var file = wanted.Files.GetValueOrDefault(trackId);
        if (format.For(file).Kind != format.Kind && !downloaded.FileName.EndsWith(AacExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return downloaded.FileVersion is { } old && file is not null && file.Mark != old;
    }

    /// <summary>A song's download size: its file's for the original, its length at the bitrate for AAC.</summary>
    private static long EstimatedSize(Track track, TrackFile? file, DownloadFormat format)
    {
        var seconds = track.Duration?.TotalSeconds ?? 0;
        format = format.For(file);
        return format.Kind == DownloadFormatKind.Aac
            ? (long)(seconds * format.AacBitrateKbps * 125)
            : file?.Size ?? (long)(seconds * CdBytesPerSecond);
    }

    /// <summary>Whether the song failed <see cref="MaxAttempts"/> times: it waits for <see cref="SyncNowAsync"/>.</summary>
    private bool GaveUp(string trackId) => _failures.TryGetValue(trackId, out var failure) && failure.Attempts >= MaxAttempts;

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

            // Downloads from before #365 may have their audio far in: once, until every file could be read.
            var compacted = index.FlacFilesCompacted;
            if (!compacted)
            {
                compacted = true;
                foreach (var (id, song) in index.Tracks.ToList())
                {
                    compacted &= TryCompact(Path.Join(folder, song.FileName), out var size);
                    if (size is { } newSize)
                    {
                        lock (_indexLock)
                        {
                            index.Tracks[id] = song with { Size = newSize };
                        }
                    }
                }
            }

            var renameFailed = false;
            foreach (var (id, downloaded) in index.Tracks.ToList())
            {
                if (wanted.Tracks.TryGetValue(id, out var track))
                {
                    // A download from before the marks takes the server's: its file is as good as any.
                    var kept = downloaded with { Track = track, FileVersion = downloaded.FileVersion ?? wanted.Files.GetValueOrDefault(id)?.Mark };
                    if (!index.AacFilesChecked && IsMp4Named(kept.FileName, Path.Join(folder, kept.FileName)))
                    {
                        kept = TryRename(folder, kept, ref renameFailed);
                    }

                    lock (_indexLock)
                    {
                        index.Tracks[id] = kept;
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
                index.Albums = wanted.Albums;
                index.Artists = wanted.Artists;
                index.LastSync = _time.GetUtcNow();
                index.FlacFilesCompacted = compacted;
                index.AacFilesChecked = !renameFailed;
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
    private async Task<bool> DownloadAsync(string key, DownloadIndex index, Track track, TrackFile? serverFile, DownloadFormat format, CancellationToken cancellationToken)
    {
        // The index keeps the chosen format; an original Windows cannot play arrives as AAC (#362).
        var source = format.For(serverFile);
        var folder = _store.FolderOf(key);
        var part = Path.Join(folder, track.Id + PartExtension);
        _current[track.Id] = track.Name;
        Publish(key, DownloadStop.None);
        try
        {
            Directory.CreateDirectory(folder);
            var offset = source.Kind == DownloadFormatKind.Original && File.Exists(part) ? new FileInfo(part).Length : 0;
            using var response = await _source.OpenAsync(track.Id, source, offset, cancellationToken).ConfigureAwait(false);
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

            if (TryCompact(part, out var compactedSize) && compactedSize is { } newSize)
            {
                size = newSize;
            }

            var fileName = track.Id + (IsMp4Named(response.Extension, part) ? Mp4Extension : response.Extension);
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
                        FileVersion = serverFile?.Mark,
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

    /// <summary>
    /// Downloads the covers the downloaded songs, albums, artists and playlists need and deletes the others (#335).
    /// A cover that fails is left for the next sync; an unreachable server stops them.
    /// </summary>
    private async Task KeepCoversAsync(string key, DownloadIndex index, CancellationToken cancellationToken)
    {
        if (_images is null)
        {
            return;
        }

        Dictionary<string, string> wanted;
        lock (_indexLock)
        {
            wanted = WantedCovers(index);
        }

        var folder = _store.CoversOf(key);
        var names = wanted.Select(c => CoverName(c.Key, c.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (names.Contains(Path.GetFileName(file)))
                {
                    have.Add(Path.GetFileName(file));
                }
                else
                {
                    TryDelete(file);
                }
            }
        }

        foreach (var (itemId, tag) in wanted.Where(c => !have.Contains(CoverName(c.Key, c.Value))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Join(folder, CoverName(itemId, tag));
            try
            {
                Directory.CreateDirectory(folder);
                var temp = path + PartExtension;
                await using (var image = await _images.OpenAsync(new ImageRequest(itemId, ImageKind.Primary, tag, CoverWidth), cancellationToken).ConfigureAwait(false))
                await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await image.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                }

                File.Move(temp, path, overwrite: true);
            }
            catch (JellyfinException ex) when (ex.StatusCode is null)
            {
                LogCoverFailed(ex, itemId);
                return;
            }
            catch (Exception ex) when (ex is JellyfinException or IOException or UnauthorizedAccessException)
            {
                LogCoverFailed(ex, itemId);
            }
        }
    }

    /// <summary>The covers by item ID with their image tags: of each downloaded song's album, and of the collections.</summary>
    private static Dictionary<string, string> WantedCovers(DownloadIndex index)
    {
        var covers = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string? itemId, string? tag)
        {
            // Both become a file name: Jellyfin's are hexadecimal.
            if (itemId is not null && tag is not null && itemId.All(char.IsAsciiLetterOrDigit) && tag.All(char.IsAsciiLetterOrDigit))
            {
                covers.TryAdd(itemId, tag);
            }
        }

        foreach (var downloaded in index.Tracks.Values)
        {
            Add(downloaded.Track.AlbumId, downloaded.Track.AlbumImageTag);
        }

        foreach (var album in index.Albums)
        {
            Add(album.Album.Id, album.Album.PrimaryImageTag);
        }

        foreach (var artist in index.Artists)
        {
            Add(artist.Artist.Id, artist.Artist.PrimaryImageTag);
        }

        foreach (var playlist in index.Playlists)
        {
            Add(playlist.Playlist.Id, playlist.Playlist.PrimaryImageTag);
        }

        return covers;
    }

    private static string CoverName(string itemId, string tag) => $"{itemId}_{tag}{CoverExtension}";

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
        int songs = 0;
        lock (_indexLock)
        {
            loaded = key is not null && key == _key;
            if (loaded)
            {
                var index = _index;
                songs = index.Tracks.Count;
                var ids = index.WantedTrackIds().ToHashSet(StringComparer.Ordinal);
                wanted = ids.Count;
                downloaded = ids.Count(id => index.Tracks.TryGetValue(id, out var t) && t.Format == format);
                bytes = index.Tracks.Values.Sum(t => t.Size);
            }
        }

        var status = loaded
            ? new DownloadStatus
            {
                IsSyncing = _syncing,
                Wanted = wanted,
                Downloaded = downloaded,
                Bytes = bytes,
                Current = _current.Values.FirstOrDefault(),
                Failures = [.. _failures.Values.OrderBy(f => f.TrackName, StringComparer.CurrentCulture)],
                Stopped = stopped,
            }
            : DownloadStatus.Empty with { IsSyncing = _syncing, Stopped = stopped };

        // The marks in the lists follow what is on disk: another user, or songs downloaded or deleted.
        var downloads = (loaded ? key : null, songs, bytes);
        bool statusChanged, downloadsChanged;
        lock (_gate)
        {
            statusChanged = status != _status;
            downloadsChanged = downloads != _downloads;
            _status = status;
            _downloads = downloads;
        }

        if (statusChanged)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        if (downloadsChanged)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Moves the audio of a downloaded FLAC file to the front when it starts far in (<see cref="FlacMetadata"/>,
    /// #365). A file that is gone needs nothing; one in use is tried again at the next sync.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="size">The file's new size when it was rewritten.</param>
    /// <returns>Whether the file is done.</returns>
    private bool TryCompact(string path, out long? size)
    {
        size = null;
        try
        {
            size = FlacMetadata.Compact(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCompactFailed(ex, Path.GetFileName(path));
            return false;
        }
    }

    /// <summary>
    /// Whether a file named <c>.aac</c> is an MP4 file (its first box is <c>ftyp</c>): the server sends an AAC file
    /// within the bitrate as it is, often an <c>.m4a</c>. Windows reads a <c>.aac</c> as a raw AAC stream and
    /// cannot play it (#362).
    /// </summary>
    private static bool IsMp4Named(string extensionOrName, string path)
    {
        if (!extensionOrName.EndsWith(AacExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            Span<byte> head = stackalloc byte[8];
            using var stream = File.OpenRead(path);
            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length && head[4..].SequenceEqual("ftyp"u8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Gives an MP4 file named <c>.aac</c> the <c>.m4a</c> it needs (#362); in use, the next sync tries again.</summary>
    private DownloadedTrack TryRename(string folder, DownloadedTrack downloaded, ref bool failed)
    {
        var fileName = Path.ChangeExtension(downloaded.FileName, Mp4Extension);
        try
        {
            File.Move(Path.Join(folder, downloaded.FileName), Path.Join(folder, fileName), overwrite: true);
            return downloaded with { FileName = fileName };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogRenameFailed(ex, downloaded.FileName);
            failed = true;
            return downloaded;
        }
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

    private sealed record Wanted(
        Dictionary<string, Track> Tracks,
        IReadOnlyList<string> Favourites,
        IReadOnlyList<DownloadedPlaylist> Playlists,
        IReadOnlyList<DownloadedAlbum> Albums,
        IReadOnlyList<DownloadedArtist> Artists,
        IReadOnlyDictionary<string, TrackFile> Files);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moving the audio of {FileName} to the front failed")]
    private partial void LogCompactFailed(Exception exception, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Renaming {FileName} to .m4a failed")]
    private partial void LogRenameFailed(Exception exception, string fileName);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed the downloads {Key} of a server no longer in the list")]
    private partial void LogServerRemoved(string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keeping the cover of {ItemId} failed")]
    private partial void LogCoverFailed(Exception exception, string itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "All downloads removed")]
    private partial void LogRemovedAll();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting the download {FileName} failed")]
    private partial void LogDeleteFailed(Exception exception, string fileName);
}
