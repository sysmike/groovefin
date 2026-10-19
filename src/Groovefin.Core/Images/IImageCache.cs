namespace Groovefin.Core.Images;

/// <summary>
/// Disk cache for artwork. Returns local file paths that views can load directly.
/// </summary>
public interface IImageCache
{
    /// <summary>
    /// Gets a local file for <paramref name="request"/>, downloading it on a miss. Returns
    /// <see langword="null"/> when the image cannot be loaded; views then show a placeholder.
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
public sealed record ImageCacheOptions(string Directory, Func<long> MaxBytes);
