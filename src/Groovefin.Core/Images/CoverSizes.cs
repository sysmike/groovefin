using Groovefin.Core.Models;

namespace Groovefin.Core.Images;

/// <summary>The cover sizes every page shares (#452), in device-independent pixels.</summary>
public static class CoverSizes
{
    /// <summary>
    /// The least width of an album, artist or playlist tile; a grid stretches its tiles to fill a row. The app's
    /// <c>CoverTileWidth</c> resource has the same value.
    /// </summary>
    public const double Tile = 168;

    /// <summary>A song row's album cover, as the app's <c>ListThumbnailSize</c> has it in the standard density.</summary>
    public const double Row = 40;

    /// <summary>The cover of <paramref name="track"/>'s album for a song row, at <paramref name="scale"/>.</summary>
    public static ImageRequest? RowCover(Track track, double scale)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.AlbumId is { } albumId ? ImageRequest.Primary(albumId, track.AlbumImageTag, Row, scale) : null;
    }
}
