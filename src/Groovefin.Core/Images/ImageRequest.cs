namespace Groovefin.Core.Images;

/// <summary>Which image of an item to load.</summary>
public enum ImageKind
{
    Primary,
    Backdrop,
    Logo,
}

/// <summary>
/// An image of a library item at a given display width (in physical pixels, i.e. already multiplied by
/// the DPI scale). The <see cref="Tag"/> changes when the image changes on the server, so it is part of
/// the cache key.
/// </summary>
public sealed record ImageRequest(string ItemId, ImageKind Kind, string Tag, int Width)
{
    /// <summary>Widths requested from the server. Display widths are rounded up to the next one, so
    /// slightly different layouts share cache entries.</summary>
    public static IReadOnlyList<int> WidthBuckets { get; } = [64, 128, 256, 384, 512, 768, 1024, 1536, 2048];

    /// <summary>Gets the width actually requested from the server.</summary>
    public int BucketWidth
    {
        get
        {
            foreach (var bucket in WidthBuckets)
            {
                if (Width <= bucket)
                {
                    return bucket;
                }
            }

            return WidthBuckets[^1];
        }
    }

    /// <summary>Creates a request for an item's primary image, or <see langword="null"/> when it has none.</summary>
    public static ImageRequest? Primary(string itemId, string? tag, double displayWidth, double scale) =>
        string.IsNullOrEmpty(tag)
            ? null
            : new ImageRequest(itemId, ImageKind.Primary, tag, Math.Max(1, (int)Math.Ceiling(displayWidth * scale)));
}
