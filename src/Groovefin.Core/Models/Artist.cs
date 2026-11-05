namespace Groovefin.Core.Models;

/// <summary>
/// A music artist or album artist.
/// Saved in the downloads index and the session: the properties with a default have setters, so a file without
/// one keeps the default (#355).
/// </summary>
public sealed record Artist
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Gets the cache tag of the primary image, or <see langword="null"/> when the artist has no image.</summary>
    public string? PrimaryImageTag { get; init; }

    public bool IsFavorite { get; init; }

    /// <summary>Gets the biography as plain text, or <see langword="null"/> when the server has none.</summary>
    public string? Biography { get; init; }

    /// <summary>Gets the genres; the artist page links to their pages.</summary>
    public IReadOnlyList<Genre> Genres { get; set; } = [];

    /// <summary>Gets the MusicBrainz artist ID, if the server knows it (from the files' tags).</summary>
    public string? MusicBrainzArtistId { get; init; }
}
