using Groovefin.Core.Images;
using Groovefin.Core.ViewModels;

namespace Groovefin.App.Converters;

/// <summary>x:Bind helpers for cover image requests.</summary>
internal static class Covers
{
    /// <summary>List thumbnail size in device-independent pixels.</summary>
    public const double ThumbnailWidth = 40;

    /// <summary>
    /// Gets a list-thumbnail variant of a tile cover request. The tile request already contains the DPI
    /// scale, so the thumbnail keeps the same ratio.
    /// </summary>
    public static ImageRequest? Thumbnail(ImageRequest? cover) =>
        cover is null ? null : cover with { Width = (int)Math.Ceiling(cover.Width * ThumbnailWidth / AlbumsViewModel.TileWidth) };
}
