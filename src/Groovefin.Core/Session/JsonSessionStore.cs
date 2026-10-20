using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Session;

/// <summary>
/// <see cref="ISessionStore"/> as a JSON file. Writes go to a temporary file that then replaces the
/// session file, so a crash never leaves half a file; an unreadable file is treated as no session.
/// </summary>
public sealed partial class JsonSessionStore(SessionStoreOptions options, ILogger<JsonSessionStore> logger) : ISessionStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string FilePath => options.FilePath;

    public async Task<SavedSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var session = await JsonSerializer.DeserializeAsync(stream, SessionJsonContext.Default.SavedSession, cancellationToken).ConfigureAwait(false);
            return session?.Version == SavedSession.CurrentVersion ? session : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LogReadFailed(ex, FilePath);
            return null;
        }
    }

    public async Task SaveAsync(SavedSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, session, SessionJsonContext.Default.SavedSession, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth bothering the user: the next save tries again.
            LogWriteFailed(ex, FilePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(ex, FilePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the session file {Path}; starting with an empty queue")]
    private partial void LogReadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the session file {Path}")]
    private partial void LogWriteFailed(Exception exception, string path);
}
