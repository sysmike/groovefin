using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Groovefin.Core.Api;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Images;

/// <summary>
/// <see cref="IImageCache"/> that stores one file per server, item, image kind, tag and width bucket.
/// The file's last-write time is its last use; above <see cref="ImageCacheOptions.MaxBytes"/> the least
/// recently used files are deleted. Concurrent requests for the same image share one download.
/// </summary>
public sealed partial class DiskImageCache(
    ImageCacheOptions options,
    IImageSource source,
    TimeProvider time,
    ILogger<DiskImageCache> logger) : IImageCache
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new();
    private readonly SemaphoreSlim _evictionGate = new(1, 1);

    public Task<string?> GetAsync(ImageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (source.ServerKey is not { } serverKey)
        {
            return Task.FromResult<string?>(null);
        }

        var path = PathFor(serverKey, request);
        if (File.Exists(path))
        {
            Touch(path);
            return Task.FromResult<string?>(path);
        }

        // One download per file; the shared download is not cancelled by a single caller.
        var lazy = _inFlight.GetOrAdd(path, p => new Lazy<Task<string?>>(() => DownloadAsync(request, p)));
        return lazy.Value.WaitAsync(cancellationToken);
    }

    public Task<long> GetSizeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => EnumerateFiles().Sum(f => f.Length), cancellationToken);

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _evictionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var file in EnumerateFiles())
            {
                TryDelete(file.FullName);
            }

            LogCleared();
        }
        finally
        {
            _evictionGate.Release();
        }
    }

    internal string PathFor(string serverKey, ImageRequest request)
    {
        var key = $"{serverKey}|{request.ItemId}|{request.Kind}|{request.Tag}|{request.BucketWidth}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        // Two-character sub-folders keep directories small for large libraries.
        return Path.Join(options.Directory, hash[..2], hash + ".img");
    }

    private async Task<string?> DownloadAsync(ImageRequest request, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            await using (var stream = await source.OpenAsync(request, CancellationToken.None).ConfigureAwait(false))
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.CopyToAsync(file).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
            Touch(path);
            await EvictAsync().ConfigureAwait(false);
            return path;
        }
        catch (Exception ex) when (ex is JellyfinException or IOException or UnauthorizedAccessException)
        {
            // Missing artwork is shown as a placeholder; one InfoBar per image would flood the window.
            LogDownloadFailed(ex, request.ItemId, request.Kind);
            return null;
        }
        finally
        {
            _inFlight.TryRemove(path, out _);
        }
    }

    private async Task EvictAsync()
    {
        if (!await _evictionGate.WaitAsync(0).ConfigureAwait(false))
        {
            // Another eviction is running and will see this file too.
            return;
        }

        try
        {
            var maxBytes = options.MaxBytes();
            var files = EnumerateFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            var removed = 0;

            foreach (var file in files)
            {
                if (total <= maxBytes)
                {
                    break;
                }

                if (TryDelete(file.FullName))
                {
                    total -= file.Length;
                    removed++;
                }
            }

            if (removed > 0)
            {
                LogEvicted(removed, total);
            }
        }
        finally
        {
            _evictionGate.Release();
        }
    }

    private IEnumerable<FileInfo> EnumerateFiles()
    {
        var root = new DirectoryInfo(options.Directory);
        return root.Exists
            ? root.EnumerateFiles("*.img", SearchOption.AllDirectories)
            : [];
    }

    private void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, time.GetUtcNow().UtcDateTime);
        }
        catch (IOException)
        {
            // Only affects eviction order.
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use by a view; it is deleted on a later eviction.
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load {Kind} image of item {ItemId}")]
    private partial void LogDownloadFailed(Exception exception, string itemId, ImageKind kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "Image cache: evicted {Count} file(s), {RemainingBytes} bytes remain")]
    private partial void LogEvicted(int count, long remainingBytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Image cache cleared")]
    private partial void LogCleared();
}
