using Groovefin.Core.Models;

namespace Groovefin.Core.Api;

/// <summary>Matches of one search, grouped by kind, each in the server's order.</summary>
public sealed record SearchResults(
    IReadOnlyList<Artist> Artists,
    IReadOnlyList<Album> Albums,
    IReadOnlyList<Track> Tracks,
    IReadOnlyList<Playlist> Playlists)
{
    public static SearchResults Empty { get; } = new([], [], [], []);

    /// <summary>Gets the number of matches of all kinds.</summary>
    public int Count => Artists.Count + Albums.Count + Tracks.Count + Playlists.Count;
}

/// <summary>Searches the music library (artists, albums, tracks) and the user's audio playlists.</summary>
public interface ISearchService
{
    /// <summary>Finds up to <paramref name="limit"/> matches of each kind for <paramref name="query"/>.</summary>
    /// <exception cref="JellyfinException">A request failed.</exception>
    Task<SearchResults> SearchAsync(string query, int limit, CancellationToken cancellationToken = default);
}
