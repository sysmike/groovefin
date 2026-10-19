using Groovefin.Core.Models;

namespace Groovefin.Core.Api;

/// <summary>The user's audio playlists on the server: browse, create, rename, delete and edit entries.</summary>
public interface IPlaylistService
{
    /// <summary>Gets the user's audio playlists, by name.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one playlist (name, image, track count, length).</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>Gets the playlist's entries in order.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<PlaylistEntry>> GetEntriesAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>Creates an audio playlist with <paramref name="trackIds"/> in that order.</summary>
    /// <returns>The new playlist's ID.</returns>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<string> CreateAsync(string name, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default);

    /// <exception cref="JellyfinException">The request failed.</exception>
    Task RenameAsync(string playlistId, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes the playlist (not its tracks).</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task DeleteAsync(string playlistId, CancellationToken cancellationToken = default);

    /// <summary>Appends <paramref name="trackIds"/> to the playlist, in that order.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task AddTracksAsync(string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default);

    /// <exception cref="JellyfinException">The request failed.</exception>
    Task RemoveEntriesAsync(string playlistId, IReadOnlyList<string> entryIds, CancellationToken cancellationToken = default);

    /// <summary>Moves an entry to <paramref name="newIndex"/> (0-based).</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task MoveEntryAsync(string playlistId, string entryId, int newIndex, CancellationToken cancellationToken = default);
}
