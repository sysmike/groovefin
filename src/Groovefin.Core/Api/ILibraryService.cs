using Groovefin.Core.Models;

namespace Groovefin.Core.Api;

/// <summary>
/// Browses the music library of the active connection.
/// </summary>
public interface ILibraryService
{
    /// <summary>Gets the user's music libraries, sorted as on the server. Browsing is limited to <see cref="ServerConnection.LibraryId"/> when set.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<MusicLibrary>> GetMusicLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one page of albums.</summary>
    /// <exception cref="InvalidOperationException">There is no active connection.</exception>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<PagedResult<Album>> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellationToken = default);

    /// <summary>Gets one album.</summary>
    /// <exception cref="JellyfinException">The album does not exist (HTTP 404) or the request failed.</exception>
    Task<Album> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default);

    /// <summary>Gets the tracks of an album in disc and track order.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Track>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default);

    /// <summary>Marks an album, artist or track as favourite of the signed-in user, or removes the mark.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task SetFavouriteAsync(string itemId, bool isFavourite, CancellationToken cancellationToken = default);

    /// <summary>Gets up to <paramref name="limit"/> favourite tracks of the music library, by name.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Track>> GetFavouriteTracksAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Gets an instant mix: up to <paramref name="limit"/> tracks like the item (track, album, artist, playlist or genre).</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Track>> GetInstantMixAsync(string itemId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Gets one page of all tracks of the music library.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<PagedResult<Track>> GetTracksAsync(TrackQuery query, CancellationToken cancellationToken = default);

    /// <summary>Gets one page of artists or album artists.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<PagedResult<Artist>> GetArtistsAsync(ArtistQuery query, CancellationToken cancellationToken = default);

    /// <summary>Gets one artist with biography and genres.</summary>
    /// <exception cref="JellyfinException">The artist does not exist (HTTP 404) or the request failed.</exception>
    Task<Artist> GetArtistAsync(string artistId, CancellationToken cancellationToken = default);

    /// <summary>Gets the artist's most played tracks, most played first.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Track>> GetArtistTopTracksAsync(string artistId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Gets all tracks of an artist, album by album (newest first) in disc and track order.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Track>> GetArtistTracksAsync(string artistId, CancellationToken cancellationToken = default);

    /// <summary>Gets artists the server considers similar.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Artist>> GetSimilarArtistsAsync(string artistId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Gets the genres used by albums, sorted by name.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Genre>> GetAlbumGenresAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one genre by ID.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<Genre> GetGenreAsync(string genreId, CancellationToken cancellationToken = default);

    /// <summary>Gets up to <paramref name="limit"/> tracks of the genre in the music library, album by album.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Track>> GetGenreTracksAsync(string genreId, int limit, CancellationToken cancellationToken = default);
}

/// <summary>Paging, sorting and filters for <see cref="ILibraryService.GetAlbumsAsync"/>.</summary>
public sealed record AlbumQuery
{
    public int StartIndex { get; init; }

    public int Limit { get; init; } = 100;

    public AlbumSortField SortBy { get; init; } = AlbumSortField.Name;

    public bool Descending { get; init; }

    /// <summary>Gets the genre IDs; an album matches when it has any of them. Empty means all genres.</summary>
    public IReadOnlyList<string> GenreIds { get; init; } = [];

    /// <summary>Gets the first production year to include, or <see langword="null"/> for no lower bound.</summary>
    public int? MinYear { get; init; }

    /// <summary>Gets the last production year to include, or <see langword="null"/> for no upper bound.</summary>
    public int? MaxYear { get; init; }

    public bool FavouritesOnly { get; init; }

    /// <summary>Gets an artist ID; only albums by this album artist are included.</summary>
    public string? AlbumArtistId { get; init; }

    /// <summary>Gets an artist ID; only albums this artist appears on, but is not the album artist of, are included.</summary>
    public string? ContributingArtistId { get; init; }

    /// <summary>Gets a value indicating whether only albums the user has played are included.</summary>
    public bool PlayedOnly { get; init; }
}

/// <summary>Album sort orders. Ties are broken by name.</summary>
public enum AlbumSortField
{
    Name,
    AlbumArtist,
    Year,
    DateAdded,
    PlayCount,
    DatePlayed,
    Random,
}

/// <summary>Paging, sorting and filters for <see cref="ILibraryService.GetArtistsAsync"/>.</summary>
public sealed record ArtistQuery
{
    public int StartIndex { get; init; }

    public int Limit { get; init; } = 100;

    /// <summary>Gets a value indicating whether only album artists are listed (otherwise every track artist).</summary>
    public bool AlbumArtistsOnly { get; init; } = true;

    public ArtistSortField SortBy { get; init; } = ArtistSortField.Name;

    public bool Descending { get; init; }

    public bool FavouritesOnly { get; init; }
}

/// <summary>Artist sort orders. Ties are broken by name.</summary>
public enum ArtistSortField
{
    Name,
    DateAdded,
    PlayCount,
    Random,
}

/// <summary>Paging and sorting for <see cref="ILibraryService.GetTracksAsync"/>.</summary>
public sealed record TrackQuery
{
    public int StartIndex { get; init; }

    public int Limit { get; init; } = 200;

    public TrackSortField SortBy { get; init; } = TrackSortField.Name;

    public bool Descending { get; init; }
}

/// <summary>Track sort orders. Ties are broken by album and track order, then by name.</summary>
public enum TrackSortField
{
    Name,
    Artist,
    Album,
    AlbumArtist,
    Year,
    Duration,
    PlayCount,
    DateAdded,
    Random,
}
