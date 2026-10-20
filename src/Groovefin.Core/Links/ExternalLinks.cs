using Groovefin.Core.Models;

namespace Groovefin.Core.Links;

/// <summary>Pages about an album on other sites.</summary>
public static class ExternalLinks
{
    /// <summary>
    /// Gets the album on MusicBrainz: its release group (all editions) when known, else the release, else
    /// <see langword="null"/>.
    /// </summary>
    public static Uri? MusicBrainz(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        if (album.MusicBrainzReleaseGroupId is { } group)
        {
            return new Uri($"https://musicbrainz.org/release-group/{Uri.EscapeDataString(group)}");
        }

        return album.MusicBrainzReleaseId is { } release ? new Uri($"https://musicbrainz.org/release/{Uri.EscapeDataString(release)}") : null;
    }

    /// <summary>Gets the album on Last.fm from artist and album name, or <see langword="null"/> without them.</summary>
    public static Uri? LastFm(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        if (string.IsNullOrWhiteSpace(album.AlbumArtist) || string.IsNullOrWhiteSpace(album.Name))
        {
            return null;
        }

        return new Uri($"https://www.last.fm/music/{Segment(album.AlbumArtist)}/{Segment(album.Name)}");
    }

    /// <summary>Last.fm writes spaces as "+" in its paths; everything else is escaped.</summary>
    private static string Segment(string name) => Uri.EscapeDataString(name.Trim()).Replace("%20", "+", StringComparison.Ordinal);
}
