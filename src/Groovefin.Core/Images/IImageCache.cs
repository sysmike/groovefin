namespace Groovefin.Core.Images;

/// <summary>
/// Disk cache for artwork. Returns local file paths that views can load directly.
/// </summary>
public interface IImageCache
{
    /// <summary>
    /// Gets a local file for <paramref name="request"/>, downloading it on a miss. Returns
    /// <see langword="null"/> when the image cannot be loaded; views then show a placeholder.
    /// Cancelling <paramref name="cancellationToken"/> tells the cache the caller no longer shows the image (a
    /// list tile that was recycled): a download nobody waits for any more is dropped (#430).
    /// </summary>
    Task<string?> GetAsync(ImageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets the current size of the cache in bytes.</summary>
    Task<long> GetSizeAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes all cached images.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Location and size limit of the image cache.</summary>
/// <param name="Directory">Cache folder in local app data.</param>
/// <param name="MaxBytes">Current size limit, read on every eviction so a changed setting applies at once.
/// Above it, the least recently used images are deleted.</param>
public sealed record ImageCacheOptions(string Directory, Func<long> MaxBytes)
{
    /// <summary>
    /// Gets the images downloaded at a time (#430). A page of covers asks for all of them at once; the others
    /// wait their turn, so the server resizes a handful at a time and browsing stays responsive.
    /// </summary>
    public int MaxConcurrentDownloads { get; init; } = 6;
}
