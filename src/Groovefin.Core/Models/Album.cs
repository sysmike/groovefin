namespace Groovefin.Core.Models;

/// <summary>A music album.</summary>
public sealed record Album
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Gets the display text for the album artists, e.g. "Daft Punk" or "Various Artists".</summary>
    public string? AlbumArtist { get; init; }

    public IReadOnlyList<ArtistRef> AlbumArtists { get; init; } = [];

    public int? Year { get; init; }

    public TimeSpan? Duration { get; init; }

    /// <summary>Gets the cache tag of the primary image, or <see langword="null"/> when the album has no cover.</summary>
    public string? PrimaryImageTag { get; init; }

    public bool IsFavorite { get; init; }

    public int PlayCount { get; init; }

    public DateTimeOffset? DateAdded { get; init; }
}

/// <summary>A link to an artist.</summary>
public sealed record ArtistRef(string Id, string Name);
