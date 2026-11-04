using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Downloads;

/// <summary>Where downloads are kept: a folder per server and user (#327).</summary>
/// <param name="Directory">Absolute path of the <c>Downloads</c> folder.</param>
public sealed record DownloadStoreOptions(string Directory)
{
    /// <summary>Gets the free bytes on the drive of a folder; the drive's own by default. Tests replace it.</summary>
    public Func<string, long> FreeSpace { get; init; } = static path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;
}

/// <summary>The download folders and their indexes, by a key that names the server and user.</summary>
public interface IDownloadStore
{
    /// <summary>Gets the folder of the user's files. It may not exist yet.</summary>
    string FolderOf(string key);

    /// <summary>Gets the free bytes on the folder's drive.</summary>
    long FreeSpace(string key);

    /// <summary>Gets the saved index, or an empty one when there is none or it cannot be read.</summary>
    Task<DownloadIndex> LoadAsync(string key, CancellationToken cancellationToken = default);

    Task SaveAsync(string key, DownloadIndex index, CancellationToken cancellationToken = default);

    /// <summary>Deletes the user's folder with all files and the index.</summary>
    void DeleteAll(string key);

    /// <summary>Gets the keys that have a folder: every server and user with downloads.</summary>
    IReadOnlyList<string> Keys();

    /// <summary>Gets when the user's index was last saved, or <see langword="null"/> when there is none.</summary>
    DateTime? IndexSavedAt(string key);

    /// <summary>Gets the folder of the covers kept with the user's downloads (#335). It may not exist yet.</summary>
    string CoversOf(string key);
}

/// <summary>
/// <see cref="IDownloadStore"/> with the index as <c>downloads.json</c> in the user's folder. Writes go to a
/// temporary file that then replaces the index, so a crash never leaves half a file.
/// </summary>
public sealed partial class JsonDownloadStore(DownloadStoreOptions options, ILogger<JsonDownloadStore> logger) : IDownloadStore
{
    public const string IndexFileName = "downloads.json";

    public string FolderOf(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || key is "." or "..")
        {
            throw new ArgumentException("The key is not a folder name.", nameof(key));
        }

        return Path.Join(options.Directory, key);
    }

    public long FreeSpace(string key)
    {
        var folder = FolderOf(key);
        return options.FreeSpace(Directory.Exists(folder) ? folder : options.Directory);
    }

    public async Task<DownloadIndex> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Path.Join(FolderOf(key), IndexFileName);
        try
        {
            if (!File.Exists(path))
            {
                return new DownloadIndex();
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var index = await JsonSerializer.DeserializeAsync(stream, DownloadJsonContext.Default.DownloadIndex, cancellationToken).ConfigureAwait(false);
            return index?.Version == DownloadIndex.CurrentVersion ? index : new DownloadIndex();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The files stay; the next sync finds them missing from the index and downloads them again.
            LogReadFailed(ex, path);
            return new DownloadIndex();
        }
    }

    public async Task SaveAsync(string key, DownloadIndex index, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        var folder = FolderOf(key);
        var path = Path.Join(folder, IndexFileName);
        try
        {
            Directory.CreateDirectory(folder);
            var temp = path + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, index, DownloadJsonContext.Default.DownloadIndex, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
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

    public DateTime? IndexSavedAt(string key)
    {
        var path = Path.Join(FolderOf(key), IndexFileName);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    public string CoversOf(string key) => Path.Join(FolderOf(key), "covers");

    public IReadOnlyList<string> Keys()
    {
        try
        {
            return Directory.Exists(options.Directory)
                ? [.. Directory.EnumerateDirectories(options.Directory).Select(Path.GetFileName).OfType<string>()]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogListFailed(ex, options.Directory);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listing the downloads in {Folder} failed")]
    private partial void LogListFailed(Exception exception, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the download index {Path} failed")]
    private partial void LogReadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing the download index {Path} failed")]
    private partial void LogWriteFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting the downloads in {Folder} failed")]
    private partial void LogDeleteFailed(Exception exception, string folder);
}
