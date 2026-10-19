using Groovefin.Core.Models;

namespace Groovefin.Core.Api;

/// <summary>
/// Browses the music library of the active connection.
/// </summary>
public interface ILibraryService
{
    /// <summary>Gets one page of albums.</summary>
    /// <exception cref="InvalidOperationException">There is no active connection.</exception>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<PagedResult<Album>> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellationToken = default);

    /// <summary>Gets the genres used by albums, sorted by name.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<IReadOnlyList<Genre>> GetAlbumGenresAsync(CancellationToken cancellationToken = default);
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
