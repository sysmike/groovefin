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
}

/// <summary>Paging and sorting for <see cref="ILibraryService.GetAlbumsAsync"/>.</summary>
public sealed record AlbumQuery
{
    public int StartIndex { get; init; }

    public int Limit { get; init; } = 100;

    public AlbumSortField SortBy { get; init; } = AlbumSortField.Name;

    public bool Descending { get; init; }
}

/// <summary>Album sort orders. Ties are broken by name.</summary>
public enum AlbumSortField
{
    Name,
    AlbumArtist,
    Year,
    DateAdded,
    PlayCount,
    Random,
}
