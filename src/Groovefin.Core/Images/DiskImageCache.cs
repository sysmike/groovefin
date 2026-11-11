using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Groovefin.Core.Api;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Images;

/// <summary>
/// <see cref="IImageCache"/> that stores one file per server, item, image kind, tag and width bucket.
/// The file's last-write time is its last use; above <see cref="ImageCacheOptions.MaxBytes"/> the least
/// recently used files are deleted. Concurrent requests for the same image share one download;
/// <see cref="ImageCacheOptions.MaxConcurrentDownloads"/> run at a time, and one whose callers all cancelled
/// (tiles scrolled out of view) is dropped, also while it waits its turn (#430).
/// Views ask for every visible cover on the UI thread, so even a hit (file check and timestamp) runs on
/// the thread pool. An image the server cannot give comes from the <c>fallback</c> instead, such as a cover
/// kept with the downloads (#335); it is not cached.
/// </summary>
public sealed partial class DiskImageCache(
    ImageCacheOptions options,
    IImageSource source,
    TimeProvider time,
    ILogger<DiskImageCache> logger,
    IImageFallback? fallback = null) : IImageCache
{
    /// <summary>A hit marks its file as used at most this often, so scrolling back and forth writes little.</summary>
    internal static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Download> _inFlight = new();
    private readonly SemaphoreSlim _downloadGate = new(options.MaxConcurrentDownloads, options.MaxConcurrentDownloads);
    private readonly SemaphoreSlim _evictionGate = new(1, 1);

    public Task<string?> GetAsync(ImageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (source.ServerKey is not { } serverKey)
        {
            return Task.FromResult(fallback?.Find(request));
        }

        var path = PathFor(serverKey, request);
        return Task.Run(() => LookUpAsync(request, path, cancellationToken), cancellationToken);
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

    private async Task<string?> LookUpAsync(ImageRequest request, string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            TouchIfStale(path);
            return path;
        }

        while (true)
        {
            // One download per file, shared by everyone who asks for it meanwhile.
            var download = _inFlight.GetOrAdd(path, static _ => new Download());
            Task<string?> task;
            lock (download)
            {
                if (download.Ended)
                {
                    // Dropped or done just now: it is on its way out, so start over.
                    _inFlight.TryRemove(new KeyValuePair<string, Download>(path, download));
                    continue;
                }

                download.Waiters++;
                task = download.Task ??= Task.Run(() => DownloadAsync(request, path, download), CancellationToken.None);
            }

            try
            {
                return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A single caller does not cancel the shared download; the last one to leave does.
                lock (download)
                {
                    if (--download.Waiters == 0 && !download.Ended)
                    {
                        download.Ended = true;
                        download.Cancel.Cancel();
                    }
                }

                throw;
            }
        }
    }

    private async Task<string?> DownloadAsync(ImageRequest request, string path, Download download)
    {
        var cancellationToken = download.Cancel.Token;
        string? temp = null;
        try
        {
            await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // A caller that missed the file just before the download that made it ended gets here: the file
                // is in place before that download leaves _inFlight, so nothing downloads twice.
                if (File.Exists(path))
                {
                    return path;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

                await using (var stream = await source.OpenAsync(request, cancellationToken).ConfigureAwait(false))
                await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                }

                File.Move(temp, path, overwrite: true);
                temp = null;
                Touch(path);
            }
            finally
            {
                _downloadGate.Release();
            }

            await EvictAsync().ConfigureAwait(false);
            return path;
        }
        catch (Exception ex) when (ex is JellyfinException or IOException or UnauthorizedAccessException)
        {
            // Missing artwork is shown as a placeholder; one InfoBar per image would flood the window.
            LogDownloadFailed(ex, request.ItemId, request.Kind);
            return fallback?.Find(request);
        }
        finally
        {
            if (temp is not null)
            {
                TryDelete(temp);
            }

            lock (download)
            {
                download.Ended = true;
                download.Cancel.Dispose();
            }

            _inFlight.TryRemove(new KeyValuePair<string, Download>(path, download));
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

    private void TouchIfStale(string path)
    {
        try
        {
            if (time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(path) >= TouchInterval)
            {
                Touch(path);
            }
        }
        catch (IOException)
        {
            // Only affects eviction order.
        }
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

    /// <summary>A download in flight and those who wait for it. Its members are used under a lock on it.</summary>
    private sealed class Download
    {
        public CancellationTokenSource Cancel { get; } = new();

        public Task<string?>? Task { get; set; }

        public int Waiters { get; set; }

        /// <summary>Gets or sets a value indicating whether it finished or was dropped: nobody joins it any more.</summary>
        public bool Ended { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load {Kind} image of item {ItemId}")]
    private partial void LogDownloadFailed(Exception exception, string itemId, ImageKind kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "Image cache: evicted {Count} file(s), {RemainingBytes} bytes remain")]
    private partial void LogEvicted(int count, long remainingBytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Image cache cleared")]
    private partial void LogCleared();
}
