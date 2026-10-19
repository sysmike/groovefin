namespace Groovefin.Core.Models;

/// <summary>A music artist or album artist.</summary>
public sealed record Artist
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Gets the cache tag of the primary image, or <see langword="null"/> when the artist has no image.</summary>
    public string? PrimaryImageTag { get; init; }

    public bool IsFavorite { get; init; }

    /// <summary>Gets the biography as plain text, or <see langword="null"/> when the server has none.</summary>
    public string? Biography { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];
}
