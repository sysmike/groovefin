namespace Groovefin.Core.Models;

/// <summary>
/// A music album.
/// Saved in the downloads index and the session: the properties with a default have setters, so a file without
/// one keeps the default (#355).
/// </summary>
public sealed record Album
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Gets the display text for the album artists, e.g. "Daft Punk" or "Various Artists".</summary>
    public string? AlbumArtist { get; init; }

    public IReadOnlyList<ArtistRef> AlbumArtists { get; set; } = [];

    public int? Year { get; init; }

    public TimeSpan? Duration { get; init; }

    /// <summary>Gets the cache tag of the primary image, or <see langword="null"/> when the album has no cover.</summary>
    public string? PrimaryImageTag { get; init; }

    public bool IsFavorite { get; init; }

    public int PlayCount { get; init; }

    public DateTimeOffset? DateAdded { get; init; }

    /// <summary>Gets the genres; the album page links to their pages.</summary>
    public IReadOnlyList<Genre> Genres { get; set; } = [];

    /// <summary>Gets the MusicBrainz release group ID, if the server knows it.</summary>
    public string? MusicBrainzReleaseGroupId { get; init; }

    /// <summary>Gets the MusicBrainz release ID, if the server knows it.</summary>
    public string? MusicBrainzReleaseId { get; init; }
}

/// <summary>A link to an artist.</summary>
public sealed record ArtistRef(string Id, string Name);
