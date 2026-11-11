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

    /// <summary>The drive of the download folder is not there (#399): a USB drive that is not plugged in.</summary>
    FolderMissing,
}

/// <summary>A song that failed to download, why (not localized: an HTTP status or the error), and how often.</summary>
public sealed record DownloadFailure(string TrackId, string TrackName, string Reason, int Attempts);

/// <summary>How far a download of the Downloads page is (#388).</summary>
public enum DownloadItemState
{
    /// <summary>Every song is on disk in the chosen format.</summary>
    Downloaded,

    /// <summary>Songs are missing and wait for the next sync, or for what stops it.</summary>
    Waiting,

    Downloading,

    /// <summary>A song failed; the failures say why.</summary>
    Failed,
}

/// <summary>
/// One download of the Downloads page (#388): the favourite songs together, or a playlist, favourite album or
/// favourite artist, with its songs on disk and their size. A song several have counts in each.
/// </summary>
public sealed record DownloadItem(DownloadItemKind Kind, string Id, string Name)
{
    /// <summary>The <see cref="Id"/> of the favourite songs, which have none of their own.</summary>
    public const string FavouriteSongsId = "favourite-songs";

    /// <summary>Gets an album's artist; <see langword="null"/> for the others.</summary>
    public string? Artist { get; init; }

    public IReadOnlyList<string> TrackIds { get; init; } = [];

    /// <summary>Gets the songs on disk in the chosen format.</summary>
    public int Downloaded { get; init; }

    /// <summary>Gets the size of its songs on disk.</summary>
    public long Bytes { get; init; }

    public DownloadItemState State { get; init; }

    public IReadOnlyList<DownloadFailure> Failures { get; init; } = [];
}

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
/// song of the favourite albums and artists, and all playlists; and the playlists, albums and artists chosen one by
/// one with their Download toggle (#387, <see cref="IDownloadChoices"/>). A sync reads the wanted songs from the server, deletes the files no collection needs any more,
/// and downloads the missing ones, <see cref="Parallel"/> at a time, each to a <c>.part</c> file that is renamed
/// when complete. An interrupted original resumes where it stopped: the <c>.part</c> file's name tells the format
/// and the server's file it was started from, so another one starts over (#427). A song shared by several collections is
/// kept once; one whose file changed on the server (another container or size) is downloaded again. The
/// downloads of a server that left the list are deleted. Each sync also keeps one cover per downloaded album,
/// artist and playlist (<see cref="CoverWidth"/> wide), which the image cache shows when the server cannot be
/// reached (#335).
/// <para>
/// Syncs run one after another in the background: <see cref="FirstSyncDelay"/> after start-up, every
/// <see cref="SyncInterval"/> (those take the songs of what is downloaded and looks unchanged from the index, and
/// ask the server for everything only every <see cref="DownloadSettings.FullReadHours"/> hours, #431), shortly after a change to favourites, playlists or the settings, when the network
/// comes back, after a server or user change, and on <see cref="SyncNowAsync"/>. Nothing downloads while
/// offline, on a metered connection unless allowed, or with less than <see cref="MinFreeBytes"/> free. A song
/// that failed <see cref="MaxAttempts"/> times waits for <see cref="SyncNowAsync"/>.
/// </para>
/// </summary>
public sealed partial class DownloadManager : IOfflineLibrary, IDownloadChoices, IImageFallback, IDisposable
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

    /// <summary>A sync saves the index after this many downloaded songs, not after each (#431), and when it ends.</summary>
    public const int SaveEverySongs = 25;

    /// <summary>A sync saves the index when the last save is this long ago, however few songs came since (#431).</summary>
    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

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
    private bool _pendingFull;
    private bool _disposed;

    // A move of the downloads runs (#399): syncs wait for it.
    private bool _moving;

    // The loaded index and whose it is: changed and saved only under _indexGate, one at a time; each change
    // also takes _indexLock, so Publish can count on any thread meanwhile.
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private readonly Lock _indexLock = new();
    private string? _key;
    private DownloadIndex _index = new();

    // Songs added to the index since it was last saved, and when that was (#431). Under _indexGate.
    private int _unsaved;
    private long _savedAt;

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
        _interval = time.CreateTimer(_ => _ = SyncOnTimerAsync(), null, FirstSyncDelay, SyncInterval);
        _soon = time.CreateTimer(_ => _ = RequestAsync(manual: false), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _settings.Changed += OnSettingsChanged;
        _network.Changed += OnNetworkChanged;
        _servers.Changed += OnServersChanged;
        messenger.Register<DownloadManager, FavouriteChangedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: true));
        messenger.Register<DownloadManager, PlaylistChangedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));
        messenger.Register<DownloadManager, PlaylistTracksAddedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));
        messenger.Register<DownloadManager, PlaylistDeletedMessage>(this, static (r, _) => r.OnCollectionChanged(favourites: false));

        // The player asks for files from the first song on (#333).
        MoveLegacyChoices();
        _ = RefreshStatusAsync();
    }

    /// <summary>Raised on any thread after <see cref="Status"/> changed.</summary>
    public event EventHandler? StatusChanged;

    public event EventHandler? Changed;

    /// <summary>Gets the folder that holds the downloads (#399).</summary>
    public string Folder => _store.Root;

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

    /// <summary>Syncs at once, as a change does: a song that failed <see cref="MaxAttempts"/> times waits.</summary>
    public Task SyncAsync() => RequestAsync(manual: false);

    /// <summary>
    /// The sync of the timer (#431): nothing has asked for it, so it takes the songs of the playlists, albums and
    /// artists it knows from the index instead of asking the server for each, unless the last sync that read
    /// everything is <see cref="DownloadSettings.FullReadHours"/> hours ago.
    /// </summary>
    internal Task SyncOnTimerAsync() => RequestAsync(manual: false, full: false);

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The index could not be read (#428; the store logged it): no downloads for now, and the next use reads again.
        }
        finally
        {
            _indexGate.Release();
        }

        Publish(key, _store.IsAvailable ? _stopped : DownloadStop.FolderMissing);
    }

    /// <summary>
    /// Moves every download to the folder chosen in Settings (#399), or to the app's local data for
    /// <see langword="null"/>, and saves the choice. Syncs wait meanwhile; nothing downloads again. Runs the
    /// copying on the thread pool and returns on the caller's context, which saves the setting.
    /// </summary>
    public async Task<DownloadMoveResult> MoveAsync(string? folder, IProgress<DownloadMoveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder);
        lock (_gate)
        {
            if (_disposed || _moving)
            {
                return new DownloadMoveResult(DownloadMoveOutcome.Failed);
            }

            _moving = true;
        }

        try
        {
            await StopRunningAsync().ConfigureAwait(true);
            await _indexGate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
            try
            {
                var root = _store.RootOf(folder);
                var result = await Task.Run(() => _store.MoveAsync(root, progress, cancellationToken), CancellationToken.None).ConfigureAwait(true);
                if (result.Outcome is DownloadMoveOutcome.Moved or DownloadMoveOutcome.Same)
                {
                    // The index stays as it is: the files keep their names.
                    _settings.Update(s => s with { Downloads = s.Downloads with { Folder = folder } });
                }

                return result;
            }
            finally
            {
                _indexGate.Release();
            }
        }
        finally
        {
            lock (_gate)
            {
                _moving = false;
            }

            Publish(_client.Connection?.UserDataKey, _store.IsAvailable ? _stopped : DownloadStop.FolderMissing);
            SyncSoon();
        }
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

        var wanted = await LoadWantedAsync(settings, key, known: null, cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The index could not be read (#428): as if nothing were on disk.
            missing = [.. wanted.Tracks.Values];
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

    /// <summary>
    /// Gets the signed-in user's downloads for the Downloads page (#388), from the last sync: the favourite songs,
    /// then the playlists, albums and artists, each by name. Empty before the index is read.
    /// </summary>
    public IReadOnlyList<DownloadItem> GetItems()
    {
        if (_client.Connection?.UserDataKey is not { } key)
        {
            return [];
        }

        var format = DownloadFormat.From(_settings.Current.Downloads).Key;
        var current = _current.Keys.ToHashSet(StringComparer.Ordinal);
        var failures = _failures.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        lock (_indexLock)
        {
            if (_key != key)
            {
                return [];
            }

            var index = _index;
            DownloadItem Item(DownloadItemKind kind, string id, string name, IReadOnlyList<string> trackIds, string? artist = null)
            {
                var onDisk = trackIds.Distinct(StringComparer.Ordinal)
                    .Select(t => index.Tracks.GetValueOrDefault(t))
                    .OfType<DownloadedTrack>()
                    .Where(t => t.Format == format)
                    .ToList();
                List<DownloadFailure> failed = [.. trackIds.Distinct(StringComparer.Ordinal).Select(t => failures.GetValueOrDefault(t)).OfType<DownloadFailure>()];
                var songs = trackIds.Distinct(StringComparer.Ordinal).Count();
                return new DownloadItem(kind, id, name)
                {
                    Artist = artist,
                    TrackIds = trackIds,
                    Downloaded = onDisk.Count,
                    Bytes = onDisk.Sum(t => t.Size),
                    Failures = failed,
                    State = trackIds.Any(current.Contains) ? DownloadItemState.Downloading
                        : failed.Count > 0 ? DownloadItemState.Failed
                        : onDisk.Count < songs ? DownloadItemState.Waiting
                        : DownloadItemState.Downloaded,
                };
            }

            List<DownloadItem> items = [];
            if (index.FavouriteSongs.Count > 0)
            {
                items.Add(Item(DownloadItemKind.FavouriteSongs, DownloadItem.FavouriteSongsId, string.Empty, index.FavouriteSongs));
            }

            items.AddRange(index.Playlists.OrderBy(p => p.Playlist.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => Item(DownloadItemKind.Playlist, p.Playlist.Id, p.Playlist.Name, p.TrackIds)));
            items.AddRange(index.Albums.OrderBy(a => a.Album.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => Item(DownloadItemKind.Album, a.Album.Id, a.Album.Name, a.TrackIds, a.Album.AlbumArtist)));
            items.AddRange(index.Artists.OrderBy(a => a.Artist.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => Item(DownloadItemKind.Artist, a.Artist.Id, a.Artist.Name, a.TrackIds)));
            return items;
        }
    }

    /// <summary>Gets what the signed-in user removed from the downloads (#388), by name.</summary>
    public IReadOnlyList<DownloadExclusion> Removed =>
        _client.Connection?.UserDataKey is { } key && _settings.Current.Downloads.Removed.GetValueOrDefault(key) is { } removed
            ? [.. removed.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)]
            : [];

    /// <summary>Gets the free bytes on the download folder's drive, or <see langword="null"/> without it.</summary>
    public long? FreeBytes()
    {
        if (_client.Connection?.UserDataKey is not { } key || !_store.IsAvailable)
        {
            return null;
        }

        try
        {
            return _store.FreeSpace(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool IsChosen(DownloadItemKind kind, string id) =>
        _client.Connection?.UserDataKey is { } key
        && (_settings.Current.Downloads.Chosen.GetValueOrDefault(key) ?? []).Any(c => c.Kind == kind && c.Id == id);

    public void SetChosen(DownloadItemKind kind, string id, string name, bool chosen)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_client.Connection?.UserDataKey is not { } key || IsChosen(kind, id) == chosen)
        {
            return;
        }

        _settings.Update(s => s with
        {
            Downloads = s.Downloads with
            {
                Chosen = WithList(s.Downloads.Chosen, key, list => chosen
                    ? [.. list, new DownloadChoice(kind, id, name)]
                    : [.. list.Where(c => (c.Kind, c.Id) != (kind, id))]),

                // Chosen again after it was removed on the Downloads page: it downloads.
                Removed = chosen
                    ? WithList(s.Downloads.Removed, key, list => [.. list.Where(r => (r.Kind, r.Id) != (kind, id))])
                    : s.Downloads.Removed,
            },
        });
        _messenger.Send(new DownloadChoiceChangedMessage(kind, id, chosen));
    }

    /// <summary>
    /// Removes one download (#388): the favourite songs turn their switch off; a chosen playlist, album or artist is
    /// no longer chosen (#387), and one its collection switch downloads is kept from downloading. The next sync,
    /// shortly after, deletes the files nothing else wants.
    /// </summary>
    public void Remove(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind == DownloadItemKind.FavouriteSongs)
        {
            _settings.Update(s => s with { Downloads = s.Downloads with { FavouriteSongs = false } });
            return;
        }

        if (_client.Connection?.UserDataKey is not { } key)
        {
            return;
        }

        SetChosen(item.Kind, item.Id, item.Name, chosen: false);
        var downloads = _settings.Current.Downloads;
        var bySwitch = item.Kind switch
        {
            DownloadItemKind.Playlist => downloads.Playlists,
            DownloadItemKind.Album => downloads.FavouriteAlbums,
            DownloadItemKind.Artist => downloads.FavouriteArtists,
            _ => false,
        };
        if (bySwitch)
        {
            var exclusion = new DownloadExclusion(item.Kind, item.Id, item.Name);
            _settings.Update(s => s with { Downloads = s.Downloads with { Removed = WithList(s.Downloads.Removed, key, list => [.. list.Where(r => r != exclusion), exclusion]) } });
        }
    }

    /// <summary>Downloads a removed playlist, album or artist again, while its collection is on.</summary>
    public void Restore(DownloadExclusion removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        if (_client.Connection?.UserDataKey is not { } key)
        {
            return;
        }

        _settings.Update(s => s with
        {
            Downloads = s.Downloads with { Removed = WithList(s.Downloads.Removed, key, list => [.. list.Where(r => (r.Kind, r.Id) != (removed.Kind, removed.Id))]) },
        });
    }

    /// <summary>Tries the failed songs of one download again (#388), at once, also those given up on.</summary>
    public Task RetryAsync(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach (var id in item.TrackIds)
        {
            _failures.TryRemove(id, out _);
        }

        Publish(_client.Connection?.UserDataKey, _stopped);
        return SyncAsync();
    }

    /// <summary>The list of one server and user changed; an empty one goes.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<T>> WithList<T>(
        IReadOnlyDictionary<string, IReadOnlyList<T>> all, string key, Func<IReadOnlyList<T>, IReadOnlyList<T>> change)
    {
        var copy = all.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var list = change(copy.GetValueOrDefault(key) ?? []);
        if (list.Count == 0)
        {
            copy.Remove(key);
        }
        else
        {
            copy[key] = list;
        }

        return copy;
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
        var anyChosen = _client.Connection?.UserDataKey is { } key && downloads.Chosen.GetValueOrDefault(key) is { Count: > 0 };
        if (favourites ? downloads.AnyFavourites : downloads.Playlists || anyChosen)
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

    /// <summary>
    /// Gives what the signed-in user chose and removed one by one to their key of today (#429): versions before
    /// kept both under a key that named the server by its address.
    /// </summary>
    private void MoveLegacyChoices()
    {
        if (_client.Connection is not { LegacyUserDataKey: { } legacy } connection || legacy == connection.UserDataKey)
        {
            return;
        }

        var key = connection.UserDataKey;
        static IReadOnlyDictionary<string, T> Moved<T>(IReadOnlyDictionary<string, T> items, string from, string to)
        {
            if (!items.TryGetValue(from, out var item))
            {
                return items;
            }

            var moved = items.Where(i => i.Key != from).ToDictionary(i => i.Key, i => i.Value, StringComparer.Ordinal);
            moved.TryAdd(to, item);
            return moved;
        }

        var downloads = _settings.Current.Downloads;
        if (downloads.Chosen.ContainsKey(legacy) || downloads.Removed.ContainsKey(legacy))
        {
            _settings.Update(s => s with
            {
                Downloads = s.Downloads with
                {
                    Chosen = Moved(s.Downloads.Chosen, legacy, key),
                    Removed = Moved(s.Downloads.Removed, legacy, key),
                },
            });
        }
    }

    private void OnServersChanged(object? sender, EventArgs e)
    {
        MoveLegacyChoices();
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The index could not be read (#428; the store logged it): the song streams.
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>
    /// Deletes the downloads of servers no longer in the list. A server at another address now keeps its
    /// downloads: their folder is named after the server's ID (#429). A folder still named after the address of a
    /// server in the list, by a version before that, stays until its user signs in and it is renamed.
    /// </summary>
    private async Task RemoveOtherServersAsync()
    {
        var known = _servers.Servers
            .SelectMany(p => new[] { ServerConnection.ServerKeyOf(p.Id), ServerConnection.ServerKeyOf(p.Address) })
            .ToList();
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
    private Task RequestAsync(bool manual, bool allowMetered = false, bool full = true)
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
            _pendingFull |= full;
            if (_moving)
            {
                // The move syncs when it is done.
                return Task.CompletedTask;
            }

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
            bool manual, allowMetered, full;
            CancellationToken cancellationToken;
            lock (_gate)
            {
                if (!_pending || _disposed || _moving)
                {
                    _running = false;
                    return;
                }

                _pending = false;
                manual = _pendingManual;
                allowMetered = _pendingAllowMetered;
                full = _pendingFull;
                _pendingManual = false;
                _pendingAllowMetered = false;
                _pendingFull = false;
                cancellationToken = _cancel.Token;
            }

            try
            {
                await RunAsync(manual, allowMetered, full, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stopped for a change: the next request syncs with it.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogSyncFailed(ex);
            }
            catch (Exception ex) when (Unexpected(ex))
            {
                // Logged by the filter. Left uncaught, it would end this loop with _running still set, and no
                // sync would start again until the app restarts (#433).
            }
            finally
            {
                _current.Clear();
                _syncing = false;
            }

            try
            {
                Publish(_client.Connection?.UserDataKey, _stopped);
            }
            catch (Exception ex) when (Unexpected(ex))
            {
                // A subscriber of the status failed: the loop goes on all the same (#433).
            }
        }
    }

    /// <summary>Logs an error a sync was not prepared for; always <see langword="true"/>, for an exception filter.</summary>
    private bool Unexpected(Exception exception)
    {
        LogSyncFailedUnexpectedly(exception);
        return true;
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
            _pendingFull = false;
            worker = _worker;
        }

        await worker.ConfigureAwait(false);
    }

    private async Task RunAsync(bool manual, bool allowMetered, bool full, CancellationToken cancellationToken)
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

        // Without its drive the folder looks empty: nothing may be deleted or downloaded again meanwhile.
        if (!_store.IsAvailable)
        {
            _stopped = DownloadStop.FolderMissing;
            return;
        }

        _store.RemoveLeftovers();
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
            var known = full ? null : await KnownAsync(key, cancellationToken).ConfigureAwait(false);
            wanted = await LoadWantedAsync(settings, key, known, cancellationToken).ConfigureAwait(false);
        }
        catch (JellyfinException ex)
        {
            LogLoadFailed(ex);
            _stopped = ex.StatusCode is null ? DownloadStop.Unreachable : DownloadStop.LoadFailed;
            return;
        }

        var format = DownloadFormat.From(settings);
        var index = await UpdateIndexAsync(key, wanted, format, cancellationToken).ConfigureAwait(false);
        try
        {
            await DownloadMissingAsync(key, index, wanted, format, settings, allowMetered, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Also when stopped or failed: what was downloaded is in the index (#431).
            await SaveIndexAsync(key, index, onlyWhenDue: false).ConfigureAwait(false);
        }
    }

    /// <summary>Downloads the wanted songs the index does not have, then the covers.</summary>
    private async Task DownloadMissingAsync(
        string key, DownloadIndex index, Wanted wanted, DownloadFormat format, DownloadSettings settings, bool allowMetered, CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// What the index knows of the collections, for a sync of the timer (#431); or <see langword="null"/> when
    /// everything is to be read from the server: nothing was yet, or <see cref="DownloadSettings.FullReadHours"/> hours ago.
    /// </summary>
    private async Task<Known?> KnownAsync(string key, CancellationToken cancellationToken)
    {
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadIndexAsync(key, cancellationToken).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            var interval = TimeSpan.FromHours(Math.Max(0, _settings.Current.Downloads.FullReadHours));
            lock (_indexLock)
            {
                return index.LastFullRead is { } last && last <= now && now - last < interval
                    ? new Known(
                        index.Tracks.ToDictionary(t => t.Key, t => t.Value.Track, StringComparer.Ordinal),
                        index.Playlists.ToDictionary(p => p.Playlist.Id, StringComparer.Ordinal),
                        index.Albums.ToDictionary(a => a.Album.Id, StringComparer.Ordinal),
                        index.Artists.ToDictionary(a => a.Artist.Id, StringComparer.Ordinal))
                    : null;
            }
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>
    /// Reads the wanted songs from the server. With <paramref name="known"/>, a playlist, album or artist whose
    /// songs are all downloaded, and that looks unchanged, keeps the songs the index has for it (#431): the server
    /// is asked for the lists of playlists and favourites, but not for the songs of each item.
    /// </summary>
    private async Task<Wanted> LoadWantedAsync(DownloadSettings settings, string key, Known? known, CancellationToken cancellationToken)
    {
        // What was removed on the Downloads page (#388) stays in its collection, but is not downloaded.
        var removed = (settings.Removed.GetValueOrDefault(key) ?? []).Select(r => (r.Kind, r.Id)).ToHashSet();
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

        async Task AddPlaylistAsync(Playlist playlist)
        {
            if (known?.SongsOf(playlist) is { } kept)
            {
                Add(kept.Select(id => known.Tracks[id]));
                playlists.Add(new DownloadedPlaylist { Playlist = playlist, TrackIds = kept });
                return;
            }

            var entries = await _playlists.GetEntriesAsync(playlist.Id, cancellationToken).ConfigureAwait(false);
            Add(entries.Select(e => e.Track));
            playlists.Add(new DownloadedPlaylist { Playlist = playlist, TrackIds = [.. entries.Select(e => e.Track.Id)] });
        }

        async Task AddAlbumAsync(Album album)
        {
            if (known?.SongsOf(album) is { } kept)
            {
                Add(kept.Select(id => known.Tracks[id]));
                albums.Add(new DownloadedAlbum { Album = album, TrackIds = kept });
                return;
            }

            var songs = await _library.GetAlbumTracksAsync(album.Id, cancellationToken).ConfigureAwait(false);
            Add(songs);
            albums.Add(new DownloadedAlbum { Album = album, TrackIds = [.. songs.Select(t => t.Id)] });
        }

        async Task AddArtistAsync(Artist artist)
        {
            if (known?.SongsOf(artist) is { } kept)
            {
                Add(kept.Select(id => known.Tracks[id]));
                artists.Add(new DownloadedArtist { Artist = artist, TrackIds = kept });
                return;
            }

            var songs = await _library.GetArtistTracksAsync(artist.Id, cancellationToken).ConfigureAwait(false);
            Add(songs);
            artists.Add(new DownloadedArtist { Artist = artist, TrackIds = [.. songs.Select(t => t.Id)] });
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
            foreach (var playlist in (await _playlists.GetPlaylistsAsync(cancellationToken).ConfigureAwait(false))
                .Where(p => !removed.Contains((DownloadItemKind.Playlist, p.Id))))
            {
                await AddPlaylistAsync(playlist).ConfigureAwait(false);
            }
        }

        if (settings.FavouriteAlbums)
        {
            var favourite = await _library.GetAlbumsAsync(new AlbumQuery { FavouritesOnly = true, Limit = MaxFavouriteItems }, cancellationToken).ConfigureAwait(false);
            foreach (var album in favourite.Items.Where(a => !removed.Contains((DownloadItemKind.Album, a.Id))))
            {
                await AddAlbumAsync(album).ConfigureAwait(false);
            }
        }

        if (settings.FavouriteArtists)
        {
            // Every artist, not only album artists: a heart on a featured artist counts too.
            var query = new ArtistQuery { FavouritesOnly = true, AlbumArtistsOnly = false, Limit = MaxFavouriteItems };
            var favourite = await _library.GetArtistsAsync(query, cancellationToken).ConfigureAwait(false);
            foreach (var artist in favourite.Items.Where(a => !removed.Contains((DownloadItemKind.Artist, a.Id))))
            {
                await AddArtistAsync(artist).ConfigureAwait(false);
            }
        }

        // Chosen one by one (#387): what a collection already has comes once.
        foreach (var choice in settings.Chosen.GetValueOrDefault(key) ?? [])
        {
            if (choice.Kind == DownloadItemKind.Playlist && playlists.All(p => p.Playlist.Id != choice.Id)
                && await ChosenAsync(choice, () => _playlists.GetPlaylistAsync(choice.Id, cancellationToken)).ConfigureAwait(false) is { } playlist)
            {
                await AddPlaylistAsync(playlist).ConfigureAwait(false);
            }
            else if (choice.Kind == DownloadItemKind.Album && albums.All(a => a.Album.Id != choice.Id)
                && await ChosenAsync(choice, () => _library.GetAlbumAsync(choice.Id, cancellationToken)).ConfigureAwait(false) is { } album)
            {
                await AddAlbumAsync(album).ConfigureAwait(false);
            }
            else if (choice.Kind == DownloadItemKind.Artist && artists.All(a => a.Artist.Id != choice.Id)
                && await ChosenAsync(choice, () => _library.GetArtistAsync(choice.Id, cancellationToken)).ConfigureAwait(false) is { } artist)
            {
                await AddArtistAsync(artist).ConfigureAwait(false);
            }
        }

        // The files on the server: a changed one downloads the song again, and their sizes tell how much a
        // collection downloads.
        var files = tracks.Count > 0
            ? await _library.GetTrackFilesAsync([.. tracks.Keys], cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, TrackFile>();
        return new Wanted(tracks, favourites, playlists, albums, artists, files, Full: known is null);
    }

    /// <summary>
    /// Reads a chosen item from the server, or <see langword="null"/> when the server no longer has it: the choice
    /// stays, and the item downloads again should it come back.
    /// </summary>
    private async Task<T?> ChosenAsync<T>(DownloadChoice choice, Func<Task<T>> load)
        where T : class
    {
        try
        {
            return await load().ConfigureAwait(false);
        }
        catch (JellyfinException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LogChosenGone(choice.Kind, choice.Name);
            return null;
        }
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
    /// wants any more, also ones the index does not know, unless they are the server's (<see cref="Adopt"/>).
    /// </summary>
    /// <exception cref="IOException">The index could not be read (#428): nothing was deleted or saved.</exception>
    private async Task<DownloadIndex> UpdateIndexAsync(string key, Wanted wanted, DownloadFormat format, CancellationToken cancellationToken)
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
                Adopt(index, wanted, format, folder);

                // A part file of another format, or of a file the server has since replaced, goes too (#427).
                var kept = index.Tracks.Values.Select(t => t.FileName)
                    .Concat(wanted.Tracks.Keys.Select(id => PartName(id, format.For(wanted.Files.GetValueOrDefault(id)), wanted.Files.GetValueOrDefault(id))))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(folder).Where(f => !IsIndexFile(f) && !kept.Contains(Path.GetFileName(f))))
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
                if (wanted.Full)
                {
                    index.LastFullRead = index.LastSync;
                }

                index.FlacFilesCompacted = compacted;
                index.AacFilesChecked = !renameFailed;
            }

            await _store.SaveAsync(key, index, cancellationToken).ConfigureAwait(false);
            _unsaved = 0;
            _savedAt = _time.GetTimestamp();
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
        var part = Path.Join(folder, PartName(track.Id, source, serverFile));
        _current[track.Id] = track.Name;
        Publish(key, DownloadStop.None);
        try
        {
            Directory.CreateDirectory(folder);
            var offset = source.Kind == DownloadFormatKind.Original && File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset > 0 && serverFile is not null && offset >= serverFile.Size)
            {
                // As long as the server's file or longer: nothing is left to resume, and the server would answer 416.
                offset = 0;
            }

            using var response = await OpenAsync(track.Id, source, offset, part, cancellationToken).ConfigureAwait(false);
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

                _unsaved++;
                await SaveIndexUnderGateAsync(key, index, onlyWhenDue: true).ConfigureAwait(false);
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
    /// Saves the index when songs were added since the last save: with <paramref name="onlyWhenDue"/> after
    /// <see cref="SaveEverySongs"/> songs or <see cref="SaveInterval"/>, so a long sync does not write the whole
    /// index once per song (#431). Not when the index is no longer the loaded one: its folder may be deleted.
    /// </summary>
    private async Task SaveIndexAsync(string key, DownloadIndex index, bool onlyWhenDue)
    {
        await _indexGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await SaveIndexUnderGateAsync(key, index, onlyWhenDue).ConfigureAwait(false);
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary><see cref="SaveIndexAsync"/> for a caller that holds <see cref="_indexGate"/>.</summary>
    private async Task SaveIndexUnderGateAsync(string key, DownloadIndex index, bool onlyWhenDue)
    {
        if (_unsaved == 0 || _key != key || !ReferenceEquals(_index, index)
            || (onlyWhenDue && _unsaved < SaveEverySongs && _time.GetElapsedTime(_savedAt) < SaveInterval))
        {
            return;
        }

        await _store.SaveAsync(key, index, CancellationToken.None).ConfigureAwait(false);
        _unsaved = 0;
        _savedAt = _time.GetTimestamp();
    }

    /// <summary>Whether a file in a user's folder is the index, its backup or another copy of it the store keeps.</summary>
    private static bool IsIndexFile(string path) =>
        Path.GetFileName(path).StartsWith(JsonDownloadStore.IndexFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Takes the files the index does not know back in (#428): after the index was lost, or when the app ended
    /// before a download got into it. Only an original that is as long as the server's file is certain to be that
    /// file; anything else is deleted and comes again.
    /// </summary>
    private void Adopt(DownloadIndex index, Wanted wanted, DownloadFormat format, string folder)
    {
        if (format.Kind != DownloadFormatKind.Original)
        {
            return;
        }

        foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
        {
            var dot = file.Name.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0 || IsIndexFile(file.Name) || file.Name.EndsWith(PartExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = file.Name[..dot];
            if (index.Tracks.ContainsKey(id)
                || !wanted.Tracks.TryGetValue(id, out var track)
                || !wanted.Files.TryGetValue(id, out var serverFile)
                || format.For(serverFile).Kind != DownloadFormatKind.Original
                || serverFile.Size <= 0
                || file.Length != serverFile.Size)
            {
                continue;
            }

            lock (_indexLock)
            {
                index.Tracks[id] = new DownloadedTrack
                {
                    Track = track,
                    FileName = file.Name,
                    Size = file.Length,
                    Format = format.Key,
                    DownloadedAt = file.LastWriteTimeUtc,
                    FileVersion = serverFile.Mark,
                };
            }

            LogAdopted(id);
        }
    }

    /// <summary>
    /// The name of a song's unfinished download (#427): <c>{id}.{tag}.part</c>, the tag a hash of the format and of
    /// the server's file (<see cref="TrackFile.Mark"/>). Only a part file of the same name is resumed, so bytes of
    /// another format or of a replaced file never end up in one song.
    /// </summary>
    private static string PartName(string trackId, DownloadFormat source, TrackFile? serverFile)
    {
        var tag = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{source.Key}|{serverFile?.Mark}"));
        return $"{trackId}.{Convert.ToHexStringLower(tag, 0, 4)}{PartExtension}";
    }

    /// <summary>
    /// Opens the song at <paramref name="offset"/>. A server that has nothing from there on (HTTP 416: the part
    /// file is complete, or not of this file) is asked for the whole file, which replaces the part file (#427).
    /// </summary>
    private async Task<DownloadResponse> OpenAsync(string trackId, DownloadFormat source, long offset, string part, CancellationToken cancellationToken)
    {
        try
        {
            return await _source.OpenAsync(trackId, source, offset, cancellationToken).ConfigureAwait(false);
        }
        catch (JellyfinException ex) when (offset > 0 && ex.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            LogPartDiscarded(trackId, offset);
            TryDelete(part);
            return await _source.OpenAsync(trackId, source, 0, cancellationToken).ConfigureAwait(false);
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
            // Without its drive, an empty index for now: kept, it would delete the files when the drive is back.
            if (!_store.IsAvailable)
            {
                return new DownloadIndex();
            }

            // Named after the server's address by versions before #429.
            if (_client.Connection is { LegacyUserDataKey: { } legacy } connection && connection.UserDataKey == key && legacy != key)
            {
                _store.Rename(legacy, key);
            }

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
        IReadOnlyDictionary<string, TrackFile> Files,
        bool Full);

    /// <summary>
    /// What the index has of the collections at a sync of the timer (#431): the downloaded songs, and each
    /// playlist, album and artist with its songs at the last sync.
    /// </summary>
    private sealed record Known(
        Dictionary<string, Track> Tracks,
        Dictionary<string, DownloadedPlaylist> Playlists,
        Dictionary<string, DownloadedAlbum> Albums,
        Dictionary<string, DownloadedArtist> Artists)
    {
        /// <summary>The playlist's songs, when it has as many and is as long as at the last sync and all are downloaded.</summary>
        public IReadOnlyList<string>? SongsOf(Playlist playlist) =>
            Playlists.TryGetValue(playlist.Id, out var known)
            && playlist.TrackCount is { } count && count == known.Playlist.TrackCount && count == known.TrackIds.Count
            && playlist.Duration == known.Playlist.Duration
                ? Downloaded(known.TrackIds)
                : null;

        /// <summary>The album's songs, when it is as long as at the last sync and all are downloaded.</summary>
        public IReadOnlyList<string>? SongsOf(Album album) =>
            Albums.TryGetValue(album.Id, out var known) && album.Duration is { } duration && duration == known.Album.Duration
                ? Downloaded(known.TrackIds)
                : null;

        /// <summary>
        /// The artist's songs when all are downloaded. The server tells nothing that shows a new song of an artist:
        /// it comes with the next sync that reads everything.
        /// </summary>
        public IReadOnlyList<string>? SongsOf(Artist artist) =>
            Artists.TryGetValue(artist.Id, out var known) ? Downloaded(known.TrackIds) : null;

        private IReadOnlyList<string>? Downloaded(IReadOnlyList<string> trackIds) =>
            trackIds.Count > 0 && trackIds.All(Tracks.ContainsKey) ? trackIds : null;
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "The {Kind} {Name} chosen to download is no longer on the server")]
    private partial void LogChosenGone(DownloadItemKind kind, string name);

    [LoggerMessage(Level = LogLevel.Error, Message = "A download sync failed unexpectedly; the next one runs as usual")]
    private partial void LogSyncFailedUnexpectedly(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A download sync failed")]
    private partial void LogSyncFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed the downloads {Key} of a server no longer in the list")]
    private partial void LogServerRemoved(string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keeping the cover of {ItemId} failed")]
    private partial void LogCoverFailed(Exception exception, string itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The unfinished download of {TrackId} could not be resumed at {Offset} bytes; it starts over")]
    private partial void LogPartDiscarded(string trackId, long offset);

    [LoggerMessage(Level = LogLevel.Information, Message = "The file of {TrackId} was on disk but not in the index; it is kept")]
    private partial void LogAdopted(string trackId);

    [LoggerMessage(Level = LogLevel.Information, Message = "All downloads removed")]
    private partial void LogRemovedAll();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting the download {FileName} failed")]
    private partial void LogDeleteFailed(Exception exception, string fileName);
}
