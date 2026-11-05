using Groovefin.Core.Images;
using Groovefin.Core.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace Groovefin.App.Converters;

/// <summary>x:Bind helpers for cover image requests.</summary>
internal static class Covers
{
    /// <summary>List thumbnail size in device-independent pixels.</summary>
    public const double ThumbnailWidth = 40;

    /// <summary>The start page's album card without a cover colour: a dark grey, as white text needs.</summary>
    private static readonly Windows.UI.Color PlainHighlight = ColorHelper.FromArgb(255, 0x2B, 0x2B, 0x2B);

    /// <summary>
    /// Gets a list-thumbnail variant of a tile cover request. The tile request already contains the DPI
    /// scale, so the thumbnail keeps the same ratio.
    /// </summary>
    public static ImageRequest? Thumbnail(ImageRequest? cover) =>
        cover is null ? null : cover with { Width = (int)Math.Ceiling(cover.Width * ThumbnailWidth / AlbumsViewModel.TileWidth) };

    /// <summary>Gets the background of the start page's album card: its cover's colour (#367).</summary>
    public static SolidColorBrush HighlightBackground(HomeHighlight? highlight) =>
        new(highlight?.Colour is { } c ? ColorHelper.FromArgb(255, c.R, c.G, c.B) : PlainHighlight);
}
