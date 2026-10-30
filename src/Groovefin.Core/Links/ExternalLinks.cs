using Groovefin.Core.Models;

namespace Groovefin.Core.Links;

/// <summary>Pages about an album or an artist on other sites.</summary>
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

    /// <summary>Gets the artist on MusicBrainz, or <see langword="null"/> when the server knows no MusicBrainz ID (#194).</summary>
    public static Uri? MusicBrainz(Artist artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        return artist.MusicBrainzArtistId is { } id ? new Uri($"https://musicbrainz.org/artist/{Uri.EscapeDataString(id)}") : null;
    }

    /// <summary>Gets the artist on Last.fm from the name, or <see langword="null"/> without one (#194).</summary>
    public static Uri? LastFm(Artist artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        return LastFmArtist(artist.Name);
    }

    /// <summary>Gets an artist on Last.fm by name, or <see langword="null"/> without one: the Discord status (#304).</summary>
    public static Uri? LastFmArtist(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : new Uri($"https://www.last.fm/music/{Segment(name)}");

    /// <summary>Last.fm writes spaces as "+" in its paths; everything else is escaped.</summary>
    private static string Segment(string name) => Uri.EscapeDataString(name.Trim()).Replace("%20", "+", StringComparison.Ordinal);
}
