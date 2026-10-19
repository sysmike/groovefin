namespace Groovefin.Core.Models;

/// <summary>An audio playlist on the server.</summary>
public sealed record Playlist
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Gets the cache tag of the playlist image, or <see langword="null"/> when it has none.</summary>
    public string? PrimaryImageTag { get; init; }

    /// <summary>Gets the number of tracks, or <see langword="null"/> when the server did not say.</summary>
    public int? TrackCount { get; init; }

    public TimeSpan? Duration { get; init; }
}
