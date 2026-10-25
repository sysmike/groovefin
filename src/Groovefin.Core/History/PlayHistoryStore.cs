using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.History;

/// <summary>Where the play history files are: one per server and user (#251).</summary>
/// <param name="Directory">Absolute path of the <c>history</c> folder.</param>
public sealed record PlayHistoryStoreOptions(string Directory);

/// <summary>Loads and saves the play history of one server and user, by a key that names both.</summary>
public interface IPlayHistoryStore
{
    /// <summary>Gets the saved history, or <see langword="null"/> when there is none or it cannot be read.</summary>
    Task<PlayHistoryData?> LoadAsync(string key, CancellationToken cancellationToken = default);

    Task SaveAsync(string key, PlayHistoryData data, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IPlayHistoryStore"/> as JSON files. Writes go to a temporary file that then replaces the history
/// file, so a crash never leaves half a file; an unreadable file is treated as no history.
/// </summary>
public sealed partial class JsonPlayHistoryStore(PlayHistoryStoreOptions options, ILogger<JsonPlayHistoryStore> logger) : IPlayHistoryStore
{
    public async Task<PlayHistoryData?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var data = await JsonSerializer.DeserializeAsync(stream, PlayHistoryJsonContext.Default.PlayHistoryData, cancellationToken).ConfigureAwait(false);
            return data?.Version == PlayHistoryData.CurrentVersion ? data : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LogReadFailed(ex, path);
            return null;
        }
    }

    public async Task SaveAsync(string key, PlayHistoryData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var path = PathOf(key);
        try
        {
            Directory.CreateDirectory(options.Directory);
            var temp = path + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, data, PlayHistoryJsonContext.Default.PlayHistoryData, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth bothering the user: the next play saves again.
            LogWriteFailed(ex, path);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the play history {Path} failed")]
    private partial void LogReadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing the play history {Path} failed")]
    private partial void LogWriteFailed(Exception exception, string path);
}
