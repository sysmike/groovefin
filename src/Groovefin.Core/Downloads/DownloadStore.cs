using Groovefin.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Downloads;

/// <summary>Where downloads are kept: a folder per server and user (#327), in a folder of the user's choice (#399).</summary>
/// <param name="Directory">Absolute path of the default <c>Downloads</c> folder, in the app's local data.</param>
public sealed record DownloadStoreOptions(string Directory)
{
    /// <summary>
    /// The folder the downloads get in a folder chosen in Settings (#399). That folder may hold other files, and a
    /// sync deletes the folders among the downloads that belong to no server in the list.
    /// </summary>
    public const string FolderName = "Groovefin Downloads";

    /// <summary>Gets the folder chosen in Settings → Downloads, or <see langword="null"/> for <see cref="Directory"/>. Read when the store is first used.</summary>
    public Func<string?> ChosenFolder { get; init; } = static () => null;

    /// <summary>Gets the free bytes on the drive of a folder; the drive's own by default. Tests replace it.</summary>
    public Func<string, long> FreeSpace { get; init; } = static path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;

    /// <summary>Gets whether a folder's drive is there: a USB drive may not be. Tests replace it.</summary>
    public Func<string, bool> DriveReady { get; init; } = static path =>
        Path.GetPathRoot(Path.GetFullPath(path)) is { Length: > 0 } root && System.IO.Directory.Exists(root);

    /// <summary>Gets whether two folders are on the same drive, where a file moves without copying. Tests replace it.</summary>
    public Func<string, string, bool> SameDrive { get; init; } = static (a, b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether a folder is on a drive of this PC. A network share is not: the downloads are there for when the
    /// network is not. Tests replace it.
    /// </summary>
    public Func<string, bool> IsLocal { get; init; } = static path =>
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || Path.GetPathRoot(full) is not { Length: > 0 } root)
        {
            return false;
        }

        try
        {
            return new DriveInfo(root).DriveType is not DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    };

    /// <summary>Gets the folder that holds the downloads for a folder chosen in Settings.</summary>
    public string RootOf(string? chosenFolder) =>
        string.IsNullOrWhiteSpace(chosenFolder) ? Directory : Path.Join(Path.GetFullPath(chosenFolder), FolderName);
}

/// <summary>How far a move of the downloads is (#399).</summary>
public sealed record DownloadMoveProgress(long Moved, long Total);

/// <summary>How a move of the downloads ended (#399).</summary>
public enum DownloadMoveOutcome
{
    Moved,

    /// <summary>The downloads are there already.</summary>
    Same,

    /// <summary>The folder is on a network share or a network drive.</summary>
    NotLocal,

    /// <summary>The folder is inside the downloads, or the downloads inside it.</summary>
    Inside,

    NotEnoughSpace,

    /// <summary>A file could not be copied or moved: the downloads stay where they were.</summary>
    Failed,

    Cancelled,
}

/// <summary>How a move of the downloads ended; for <see cref="DownloadMoveOutcome.NotEnoughSpace"/> with the bytes needed and free.</summary>
public sealed record DownloadMoveResult(DownloadMoveOutcome Outcome, long Needed = 0, long Free = 0);

/// <summary>The download folders and their indexes, by a key that names the server and user.</summary>
public interface IDownloadStore
{
    /// <summary>Gets the folder that holds every user's folder.</summary>
    string Root { get; }

    /// <summary>Gets whether the drive of <see cref="Root"/> is there.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets the folder that holds the downloads for a folder chosen in Settings (#399).</summary>
    string RootOf(string? chosenFolder);

    /// <summary>
    /// Moves every user's folder to <paramref name="root"/> and uses it (#399): on the same drive file by file, to
    /// another by copying, then deleting the old files. A failure or cancellation puts back what was moved and
    /// keeps the old folder. Nothing may write to the folders meanwhile.
    /// </summary>
    Task<DownloadMoveResult> MoveAsync(string root, IProgress<DownloadMoveProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Deletes the old files a move could not delete, such as the song that played meanwhile.</summary>
    void RemoveLeftovers();

    /// <summary>Gets the folder of the user's files. It may not exist yet.</summary>
    string FolderOf(string key);

    /// <summary>Gets the free bytes on the folder's drive.</summary>
    long FreeSpace(string key);

    /// <summary>
    /// Gets the saved index, or an empty one when there is none or the file is damaged beyond its backup: the next
    /// sync then finds the files again that are the server's.
    /// </summary>
    /// <exception cref="IOException">
    /// The index is there but could not be read, or a newer version of the app wrote it (#428). It is not an
    /// empty one: no file may be deleted for it, and nothing saved over it.
    /// </exception>
    Task<DownloadIndex> LoadAsync(string key, CancellationToken cancellationToken = default);

    Task SaveAsync(string key, DownloadIndex index, CancellationToken cancellationToken = default);

    /// <summary>Deletes the user's folder with all files and the index.</summary>
    void DeleteAll(string key);

    /// <summary>
    /// Gives the folder of <paramref name="fromKey"/> to <paramref name="toKey"/> (#429), when there is one and
    /// <paramref name="toKey"/> has none yet. A folder that cannot be moved now stays for the next time.
    /// </summary>
    void Rename(string fromKey, string toKey);

    /// <summary>Gets the keys that have a folder: every server and user with downloads.</summary>
    IReadOnlyList<string> Keys();

    /// <summary>Gets when the user's index was last saved, or <see langword="null"/> when there is none.</summary>
    DateTime? IndexSavedAt(string key);

    /// <summary>Gets the folder of the covers kept with the user's downloads (#335). It may not exist yet.</summary>
    string CoversOf(string key);
}

/// <summary>
/// <see cref="IDownloadStore"/> with the index as <c>downloads.json</c> in the user's folder. Writes go to a
/// temporary file that then replaces the index, so a crash never leaves half a file; the index before stays as
/// <c>downloads.json.bak</c> and is read when the index is gone or damaged (#428).
/// </summary>
public sealed partial class JsonDownloadStore(DownloadStoreOptions options, ILogger<JsonDownloadStore> logger) : IDownloadStore
{
    public const string IndexFileName = "downloads.json";

    /// <summary>A copy to another drive reads and writes this much at a time, and reports after each.</summary>
    private const int CopyBufferSize = 1 << 20;

    // The index files the last load could not read (#428): not saved over until a load succeeds.
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _leftoversLock = new();
    private readonly List<(string File, string OldRoot)> _leftovers = [];
    private string? _root;

    public string Root
    {
        get => Volatile.Read(ref _root) ?? InitialRoot();
        private set => Volatile.Write(ref _root, value);
    }

    public bool IsAvailable => options.DriveReady(Root);

    public string RootOf(string? chosenFolder) => options.RootOf(chosenFolder);

    public string FolderOf(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key is "." or "..")
        {
            throw new ArgumentException("The key is not a folder name.", nameof(key));
        }

        return Path.Join(Root, key);
    }

    public long FreeSpace(string key)
    {
        var folder = FolderOf(key);
        return options.FreeSpace(Directory.Exists(folder) ? folder : Root);
    }

    public async Task<DownloadIndex> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Path.Join(FolderOf(key), IndexFileName);
        var result = await JsonFile.ReadAsync(path, DownloadJsonContext.Default.DownloadIndex, cancellationToken).ConfigureAwait(false);
        var unreadable = result.State == JsonFileState.Unreadable
            || (result.State == JsonFileState.Loaded && result.Value!.Version > DownloadIndex.CurrentVersion);
        lock (_unreadable)
        {
            _ = unreadable ? _unreadable.Add(path) : _unreadable.Remove(path);
        }

        switch (result.State)
        {
            case JsonFileState.Loaded when result.Value!.Version > DownloadIndex.CurrentVersion:
                // A newer Groovefin's: this one would drop what it does not know.
                LogNewerVersion(path, result.Value.Version);
                throw new IOException($"The download index is of version {result.Value.Version}; this app reads up to {DownloadIndex.CurrentVersion}.");

            case JsonFileState.Loaded:
                if (result.FromBackup)
                {
                    LogBackupUsed(result.Error, path);
                }

                return DownloadIndex.Migrate(result.Value!);

            case JsonFileState.Unreadable:
                LogReadFailed(result.Error, path);
                throw new IOException("The download index could not be read.", result.Error);

            case JsonFileState.Corrupt:
                // The next sync keeps the files that are the server's and downloads the others again.
                LogCorrupt(result.Error, path);
                return new DownloadIndex();

            default:
                return new DownloadIndex();
        }
    }

    public async Task SaveAsync(string key, DownloadIndex index, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        var folder = FolderOf(key);
        var path = Path.Join(folder, IndexFileName);
        lock (_unreadable)
        {
            if (_unreadable.Contains(path))
            {
                // The last read failed (#428): the file may be fine, and this is not what it holds.
                LogWriteSkipped(path);
                return;
            }
        }

        try
        {
            await JsonFile.WriteAsync(path, index, DownloadJsonContext.Default.DownloadIndex, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next change saves again.
            LogWriteFailed(ex, path);
        }
    }

    public void DeleteAll(string key)
    {
        var folder = FolderOf(key);
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(ex, folder);
            throw;
        }
    }

    public void Rename(string fromKey, string toKey)
    {
        var from = FolderOf(fromKey);
        var to = FolderOf(toKey);
        try
        {
            if (Directory.Exists(from) && !Directory.Exists(to))
            {
                Directory.Move(from, to);
                LogRenamed(fromKey, toKey);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogRenameFailed(ex, from, to);
        }
    }

    public DateTime? IndexSavedAt(string key)
    {
        var path = Path.Join(FolderOf(key), IndexFileName);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    public string CoversOf(string key) => Path.Join(FolderOf(key), "covers");

    public IReadOnlyList<string> Keys()
    {
        var root = Root;
        try
        {
            return Directory.Exists(root)
                ? [.. Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>()]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogListFailed(ex, root);
            return [];
        }
    }

    public async Task<DownloadMoveResult> MoveAsync(string root, IProgress<DownloadMoveProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        var source = Root;
        var target = Path.GetFullPath(root);
        if (SamePath(source, target))
        {
            return new DownloadMoveResult(DownloadMoveOutcome.Same);
        }

        if (!options.IsLocal(target))
        {
            return new DownloadMoveResult(DownloadMoveOutcome.NotLocal);
        }

        if (IsInside(target, source) || IsInside(source, target))
        {
            return new DownloadMoveResult(DownloadMoveOutcome.Inside);
        }

        if (!options.DriveReady(target))
        {
            return new DownloadMoveResult(DownloadMoveOutcome.Failed);
        }

        // The old drive is gone (a USB drive): nothing can be moved, the songs download again in the new folder.
        if (!options.DriveReady(source) || !Directory.Exists(source))
        {
            Root = target;
            LogUsing(target);
            return new DownloadMoveResult(DownloadMoveOutcome.Moved);
        }

        List<(string From, string To, long Size)> files;
        long free;
        var sameDrive = options.SameDrive(source, target);
        try
        {
            files = [.. Keys().SelectMany(key => Directory.EnumerateFiles(FolderOf(key), "*", SearchOption.AllDirectories))
                .Select(file => (file, Path.Join(target, Path.GetRelativePath(source, file)), new FileInfo(file).Length))];
            free = sameDrive ? long.MaxValue : options.FreeSpace(ExistingAncestor(target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMoveFailed(ex, source, target);
            return new DownloadMoveResult(DownloadMoveOutcome.Failed);
        }

        var total = files.Sum(file => file.Size);
        if (free < total)
        {
            return new DownloadMoveResult(DownloadMoveOutcome.NotEnoughSpace, total, free);
        }

        // Each file moved (same drive) or copied: a copy's original is deleted once all are there.
        var done = new List<(string From, string To, bool Copied)>(files.Count);
        long moved = 0;
        progress?.Report(new DownloadMoveProgress(0, total));
        try
        {
            foreach (var (from, to, size) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (sameDrive && TryMove(from, to))
                {
                    done.Add((from, to, false));
                }
                else
                {
                    // On the same drive too for a file that is open: the song playing can be read, not moved.
                    done.Add((from, to, true));
                    var before = moved;
                    await CopyAsync(from, to, copied => progress?.Report(new DownloadMoveProgress(before + copied, total)), cancellationToken).ConfigureAwait(false);
                }

                moved += size;
                progress?.Report(new DownloadMoveProgress(moved, total));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (ex is not OperationCanceledException)
            {
                LogMoveFailed(ex, source, target);
            }

            PutBack(done);
            DeleteEmptyFolders(target);
            return new DownloadMoveResult(ex is OperationCanceledException ? DownloadMoveOutcome.Cancelled : DownloadMoveOutcome.Failed);
        }

        Root = target;
        LogMoved(files.Count, source, target);
        foreach (var (from, _, _) in done.Where(file => file.Copied))
        {
            TryDeleteOld(from, source);
        }

        DeleteEmptyFolders(source);
        return new DownloadMoveResult(DownloadMoveOutcome.Moved);
    }

    public void RemoveLeftovers()
    {
        (string File, string OldRoot)[] leftovers;
        lock (_leftoversLock)
        {
            leftovers = [.. _leftovers];
            _leftovers.Clear();
        }

        foreach (var (file, oldRoot) in leftovers)
        {
            TryDeleteOld(file, oldRoot);
        }

        foreach (var oldRoot in leftovers.Select(l => l.OldRoot).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DeleteEmptyFolders(oldRoot);
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets whether <paramref name="path"/> is inside <paramref name="folder"/>.</summary>
    private static bool IsInside(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder itself, or the nearest one above it that exists: the target does not exist yet.</summary>
    private static string ExistingAncestor(string folder)
    {
        var current = folder;
        while (!Directory.Exists(current) && Path.GetDirectoryName(current) is { } parent)
        {
            current = parent;
        }

        return current;
    }

    private static async Task CopyAsync(string from, string to, Action<long> copied, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, CopyBufferSize, useAsync: true);
        await using var output = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            copied(total);
        }
    }

    private string InitialRoot()
    {
        var root = options.RootOf(options.ChosenFolder());
        return Interlocked.CompareExchange(ref _root, root, null) ?? root;
    }

    private static bool TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Puts back what a failed move did: moved files move back, copies are deleted.</summary>
    private void PutBack(List<(string From, string To, bool Copied)> done)
    {
        foreach (var (from, to, copied) in done)
        {
            try
            {
                if (copied)
                {
                    File.Delete(to);
                }
                else
                {
                    File.Move(to, from, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogPutBackFailed(ex, to);
            }
        }
    }

    /// <summary>Deletes an old file after a move; one that is open (the song playing) is tried again later.</summary>
    private void TryDeleteOld(string file, string oldRoot)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogOldFileKept(ex, file);
            lock (_leftoversLock)
            {
                _leftovers.Add((file, oldRoot));
            }
        }
    }

    /// <summary>Deletes the empty folders in and including <paramref name="folder"/>; never one with a file in it.</summary>
    private void DeleteEmptyFolders(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var child in Directory.EnumerateDirectories(folder))
            {
                DeleteEmptyFolders(child);
            }

            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left as it is: an empty folder does no harm.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Moved {Count} download files from {Source} to {Target}")]
    private partial void LogMoved(int count, string source, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "No downloads to move, or their drive is not there; using {Target}")]
    private partial void LogUsing(string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moving the downloads from {Source} to {Target} failed")]
    private partial void LogMoveFailed(Exception exception, string source, string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Putting back {Path} after a failed move failed")]
    private partial void LogPutBackFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "The old download {Path} is in use; deleting it later")]
    private partial void LogOldFileKept(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listing the downloads in {Folder} failed")]
    private partial void LogListFailed(Exception exception, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "The downloads {FromKey} are now {ToKey}")]
    private partial void LogRenamed(string fromKey, string toKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moving the downloads {Source} to {Target} failed; they stay for the next time")]
    private partial void LogRenameFailed(Exception exception, string source, string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The download index {Path} is not saved: it could not be read, and is left as it is")]
    private partial void LogWriteSkipped(string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The download index {Path} could not be read; it is left as it is and nothing is synced")]
    private partial void LogReadFailed(Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The download index {Path} is damaged and has no usable backup; a copy is kept as .corrupt")]
    private partial void LogCorrupt(Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The download index {Path} was gone or damaged; its backup is used")]
    private partial void LogBackupUsed(Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The download index {Path} has version {Version}, written by a newer Groovefin; it is left as it is and nothing is synced")]
    private partial void LogNewerVersion(string path, int version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing the download index {Path} failed")]
    private partial void LogWriteFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting the downloads in {Folder} failed")]
    private partial void LogDeleteFailed(Exception exception, string folder);
}
