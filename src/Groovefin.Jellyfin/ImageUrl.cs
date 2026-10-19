using System.Globalization;
using Groovefin.Core.Images;

namespace Groovefin.Jellyfin;

/// <summary>Builds Jellyfin image URLs for an <see cref="ImageRequest"/>.</summary>
internal static class ImageUrl
{
    public const int Quality = 90;

    public static Uri Build(Uri serverUrl, ImageRequest request)
    {
        // Keep a reverse-proxy base path such as https://host/jellyfin.
        var root = serverUrl.AbsoluteUri.EndsWith('/') ? serverUrl : new Uri(serverUrl.AbsoluteUri + "/");
        var relative = string.Create(
            CultureInfo.InvariantCulture,
            $"Items/{Uri.EscapeDataString(request.ItemId)}/Images/{request.Kind}?tag={Uri.EscapeDataString(request.Tag)}&maxWidth={request.BucketWidth}&quality={Quality}");
        return new Uri(root, relative);
    }
}
