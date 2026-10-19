using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Jellyfin.Mapping;
using Jellyfin.Sdk.Generated.Models;

namespace Groovefin.Jellyfin;

/// <summary><see cref="ILibraryService"/> for the active Jellyfin connection.</summary>
internal sealed class LibraryService(JellyfinClient client) : ILibraryService
{
    public async Task<PagedResult<Album>> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.StartIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Limit);
        if (query.MinYear > query.MaxYear)
        {
            throw new ArgumentException("MinYear must not be after MaxYear.", nameof(query));
        }

        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var order = query.Descending ? SortOrder.Descending : SortOrder.Ascending;

        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.IncludeItemTypes = [BaseItemKind.MusicAlbum];
                    p.Recursive = true;
                    p.StartIndex = query.StartIndex;
                    p.Limit = query.Limit;
                    p.SortBy = SortFields(query.SortBy);
                    p.SortOrder = [order];
                    p.EnableUserData = true;
                    p.EnableTotalRecordCount = true;
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.Fields = [ItemFields.DateCreated];

                    if (query.GenreIds.Count > 0)
                    {
                        p.GenreIds = query.GenreIds.Select(id => (Guid?)Guid.Parse(id)).ToArray();
                    }

                    if (Years(query) is { } years)
                    {
                        p.Years = years;
                    }

                    if (query.FavouritesOnly)
                    {
                        p.Filters = [ItemFilter.IsFavorite];
                    }
                },
                cancellationToken),
            "Loading albums").ConfigureAwait(false);

        var items = (result.Items ?? []).Select(ItemMapper.ToAlbum).ToList();
        return new PagedResult<Album>(items, result.TotalRecordCount ?? items.Count, result.StartIndex ?? query.StartIndex);
    }

    public async Task<Album> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(albumId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var dto = await JellyfinCall.RunAsync(
            () => api.Items[id].GetAsync(request => request.QueryParameters.UserId = userId, cancellationToken),
            "Loading album").ConfigureAwait(false);
        return ItemMapper.ToAlbum(dto);
    }

    public async Task<IReadOnlyList<Track>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(albumId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = id;
                    p.IncludeItemTypes = [BaseItemKind.Audio];
                    p.Recursive = true;
                    p.SortBy = [ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber, ItemSortBy.SortName];
                    p.EnableUserData = true;
                    p.EnableImages = false;
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading tracks").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
    }

    public async Task SetFavouriteAsync(string itemId, bool isFavourite, CancellationToken cancellationToken = default)
    {
        var id = ParseId(itemId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var item = api.UserFavoriteItems[id];

        await JellyfinCall.RunAsync(
            () => isFavourite
                ? item.PostAsync(request => request.QueryParameters.UserId = userId, cancellationToken)
                : item.DeleteAsync(request => request.QueryParameters.UserId = userId, cancellationToken),
            isFavourite ? "Adding favourite" : "Removing favourite").ConfigureAwait(false);
    }

    private static Guid ParseId(string id) =>
        Guid.TryParse(id, out var guid) ? guid : throw new ArgumentException($"'{id}' is not a Jellyfin item ID.", nameof(id));

    public async Task<IReadOnlyList<Genre>> GetAlbumGenresAsync(CancellationToken cancellationToken = default)
    {
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Genres.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.IncludeItemTypes = [BaseItemKind.MusicAlbum];
                    p.SortBy = [ItemSortBy.SortName];
                    p.EnableImages = false;
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading genres").ConfigureAwait(false);

        return (result.Items ?? [])
            .Where(g => g.Id is not null && !string.IsNullOrEmpty(g.Name))
            .Select(g => new Genre(ItemMapper.FormatId(g.Id)!, g.Name!))
            .ToList();
    }

    /// <summary>
    /// Jellyfin filters by a list of years; an open-ended range is limited to 1900 … next year.
    /// </summary>
    internal static int?[]? Years(AlbumQuery query)
    {
        if (query.MinYear is null && query.MaxYear is null)
        {
            return null;
        }

        var first = query.MinYear ?? 1900;
        var last = query.MaxYear ?? DateTime.UtcNow.Year + 1;
        return Enumerable.Range(first, last - first + 1).Select(y => (int?)y).ToArray();
    }

    private static ItemSortBy[] SortFields(AlbumSortField sortBy) => sortBy switch
    {
        AlbumSortField.Name => [ItemSortBy.SortName],
        AlbumSortField.AlbumArtist => [ItemSortBy.AlbumArtist, ItemSortBy.SortName],
        AlbumSortField.Year => [ItemSortBy.ProductionYear, ItemSortBy.SortName],
        AlbumSortField.DateAdded => [ItemSortBy.DateCreated, ItemSortBy.SortName],
        AlbumSortField.PlayCount => [ItemSortBy.PlayCount, ItemSortBy.SortName],
        AlbumSortField.Random => [ItemSortBy.Random],
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null),
    };
}
