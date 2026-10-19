namespace Groovefin.Core.Models;

/// <summary>A playable audio track.</summary>
public sealed record Track
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? AlbumId { get; init; }

    public string? AlbumName { get; init; }

    /// <summary>Gets the cache tag of the album cover, or <see langword="null"/> when the album has none.</summary>
    public string? AlbumImageTag { get; init; }

    /// <summary>Gets the track's artists (may differ from the album artist on compilations).</summary>
    public IReadOnlyList<ArtistRef> Artists { get; init; } = [];

    public TimeSpan? Duration { get; init; }

    /// <summary>Gets the track number on its disc, or <see langword="null"/> when unknown.</summary>
    public int? TrackNumber { get; init; }

    /// <summary>Gets the disc number, or <see langword="null"/> when the album has only one disc.</summary>
    public int? DiscNumber { get; init; }

    public bool IsFavorite { get; init; }

    public int PlayCount { get; init; }
}
