using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Groovefin.Core.Storage;

/// <summary>What reading a <see cref="JsonFile"/> found.</summary>
internal enum JsonFileState
{
    /// <summary>There is no file, and no backup of one.</summary>
    Missing,

    /// <summary>The file was read, or its backup when the file itself was gone or damaged.</summary>
    Loaded,

    /// <summary>The file is not valid JSON and has no usable backup. It was kept as <c>.corrupt</c>.</summary>
    Corrupt,

    /// <summary>
    /// The file is there but could not be opened or read (in use, no access, a failing drive). It may be fine:
    /// nothing may be saved over it.
    /// </summary>
    Unreadable,
}

/// <summary>The result of <see cref="JsonFile.ReadAsync"/>.</summary>
internal readonly record struct JsonFileResult<T>(JsonFileState State, T? Value = null, Exception? Error = null, bool FromBackup = false)
    where T : class;

/// <summary>
/// A JSON file the app keeps its own data in (#428). A write goes to a temporary file that is flushed to disk and
/// then replaces the file, whose previous content stays as <c>.bak</c>; so neither a crash nor a power loss leaves
/// the app without a readable copy. A read tells a missing file from a damaged one and from one that could not be
/// read, so a caller never takes a file it failed to read for an empty one and saves over it.
/// </summary>
internal static class JsonFile
{
    public const string BackupSuffix = ".bak";

    public const string CorruptSuffix = ".corrupt";

    public const string TempSuffix = ".tmp";

    /// <summary>A file in use is tried again after these waits: a scanner or a backup holds it only briefly.</summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(150)];

    public static async Task<JsonFileResult<T>> ReadAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken cancellationToken = default)
        where T : class
    {
        var file = await ReadOneAsync(path, type, cancellationToken).ConfigureAwait(false);
        if (file.State is JsonFileState.Loaded or JsonFileState.Unreadable)
        {
            return file;
        }

        // Gone (a crash between the two renames of a write) or damaged: the backup is the last good content.
        var backup = await ReadOneAsync(path + BackupSuffix, type, cancellationToken).ConfigureAwait(false);
        if (file.State == JsonFileState.Corrupt && backup.State != JsonFileState.Unreadable)
        {
            // Out of the way, so the next write does not make the damaged file the backup.
            TryMove(path, path + CorruptSuffix);
        }

        return backup.State switch
        {
            JsonFileState.Loaded => backup with { FromBackup = true, Error = file.Error },
            JsonFileState.Unreadable => backup,
            _ => file,
        };
    }

    /// <summary>
    /// Writes <paramref name="value"/> to a temporary file, flushes it to disk, keeps the file there is as
    /// <c>.bak</c> and puts the new one in its place.
    /// </summary>
    /// <exception cref="IOException">The file could not be written. The file and its backup are as before.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be written.</exception>
    public static async Task WriteAsync<T>(string path, T value, JsonTypeInfo<T> type, CancellationToken cancellationToken = default)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + TempSuffix;
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, type, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            File.Move(path, path + BackupSuffix, overwrite: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static async Task<JsonFileResult<T>> ReadOneAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new(JsonFileState.Missing);
                }

                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var value = await JsonSerializer.DeserializeAsync(stream, type, cancellationToken).ConfigureAwait(false);
                return value is null
                    ? new(JsonFileState.Corrupt, Error: new JsonException("The file holds no object."))
                    : new(JsonFileState.Loaded, value);
            }
            catch (JsonException ex)
            {
                return new(JsonFileState.Corrupt, Error: ex);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return new(JsonFileState.Missing, Error: ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (ex is not IOException || attempt >= RetryDelays.Length)
                {
                    return new(JsonFileState.Unreadable, Error: ex);
                }

                await Task.Delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It stays where it is; a write then keeps it as the backup, which a read skips again.
        }
    }
}
