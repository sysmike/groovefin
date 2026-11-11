using Groovefin.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.History;

/// <summary>Where the play history files are: one per server and user (#251).</summary>
/// <param name="Directory">Absolute path of the <c>history</c> folder.</param>
public sealed record PlayHistoryStoreOptions(string Directory);

/// <summary>Loads and saves the play history of one server and user, by a key that names both.</summary>
public interface IPlayHistoryStore
{
    /// <summary>Gets the saved history, or <see langword="null"/> when there is none or the file is damaged beyond its backup.</summary>
    /// <exception cref="IOException">
    /// The history is there but could not be read, or a newer version of the app wrote it (#428): nothing may be
    /// saved over it.
    /// </exception>
    Task<PlayHistoryData?> LoadAsync(string key, CancellationToken cancellationToken = default);

    Task SaveAsync(string key, PlayHistoryData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the history of <paramref name="fromKey"/> to <paramref name="toKey"/> (#429), when there is one and
    /// <paramref name="toKey"/> has none yet. A file that cannot be moved now stays for the next time.
    /// </summary>
    void Rename(string fromKey, string toKey);
}

/// <summary>
/// <see cref="IPlayHistoryStore"/> as JSON files. Writes go to a temporary file that then replaces the history
/// file, so a crash never leaves half a file, and the history before stays as <c>.bak</c>. A damaged file gives
/// way to that backup and is kept as <c>.corrupt</c>; a file that cannot be read is not replaced (#428).
/// </summary>
public sealed partial class JsonPlayHistoryStore(PlayHistoryStoreOptions options, ILogger<JsonPlayHistoryStore> logger) : IPlayHistoryStore
{
    // The history files the last load could not read (#428): not saved over until a load succeeds.
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PlayHistoryData?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        var result = await JsonFile.ReadAsync(path, PlayHistoryJsonContext.Default.PlayHistoryData, cancellationToken).ConfigureAwait(false);
        var unreadable = result.State == JsonFileState.Unreadable
            || (result.State == JsonFileState.Loaded && result.Value!.Version > PlayHistoryData.CurrentVersion);
        lock (_unreadable)
        {
            _ = unreadable ? _unreadable.Add(path) : _unreadable.Remove(path);
        }

        switch (result.State)
        {
            case JsonFileState.Loaded when result.Value!.Version > PlayHistoryData.CurrentVersion:
                LogNewerVersion(path, result.Value.Version);
                throw new IOException($"The play history is of version {result.Value.Version}; this app reads up to {PlayHistoryData.CurrentVersion}.");

            case JsonFileState.Loaded:
                if (result.FromBackup)
                {
                    LogBackupUsed(result.Error, path);
                }

                return PlayHistoryData.Migrate(result.Value!);

            case JsonFileState.Unreadable:
                LogReadFailed(result.Error, path);
                throw new IOException("The play history could not be read.", result.Error);

            case JsonFileState.Corrupt:
                LogCorrupt(result.Error, path);
                return null;

            default:
                return null;
        }
    }

    public async Task SaveAsync(string key, PlayHistoryData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var path = PathOf(key);
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
            await JsonFile.WriteAsync(path, data, PlayHistoryJsonContext.Default.PlayHistoryData, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth bothering the user: the next play saves again.
            LogWriteFailed(ex, path);
        }
    }

    public void Rename(string fromKey, string toKey)
    {
        var from = PathOf(fromKey);
        var to = PathOf(toKey);
        try
        {
            if (File.Exists(to) || File.Exists(to + JsonFile.BackupSuffix))
            {
                return;
            }

            // The backup first: should the second move fail, the history is still read from it.
            foreach (var suffix in new[] { JsonFile.BackupSuffix, string.Empty })
            {
                if (File.Exists(from + suffix))
                {
                    File.Move(from + suffix, to + suffix);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogRenameFailed(ex, from, to);
        }
    }

    private string PathOf(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The key is not a file name.", nameof(key));
        }

        return Path.Join(options.Directory, key + ".json");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moving the play history {Source} to {Target} failed; it stays for the next time")]
    private partial void LogRenameFailed(Exception exception, string source, string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The play history {Path} is not saved: it could not be read, and is left as it is")]
    private partial void LogWriteSkipped(string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The play history {Path} could not be read; it is left as it is and plays are not recorded")]
    private partial void LogReadFailed(Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The play history {Path} is damaged and has no usable backup; a copy is kept as .corrupt")]
    private partial void LogCorrupt(Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The play history {Path} was gone or damaged; its backup is used")]
    private partial void LogBackupUsed(Exception? exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The play history {Path} has version {Version}, written by a newer Groovefin; it is left as it is")]
    private partial void LogNewerVersion(string path, int version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing the play history {Path} failed")]
    private partial void LogWriteFailed(Exception exception, string path);
}
