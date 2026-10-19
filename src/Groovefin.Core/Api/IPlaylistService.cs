namespace Groovefin.Core.Api;

/// <summary>Playlists on the server. Browsing and editing them is tracked in #36.</summary>
public interface IPlaylistService
{
    /// <summary>Creates an audio playlist with <paramref name="trackIds"/> in that order.</summary>
    /// <returns>The new playlist's ID.</returns>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<string> CreateAsync(string name, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default);
}
