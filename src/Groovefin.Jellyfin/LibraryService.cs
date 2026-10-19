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
                },
                cancellationToken),
            "Loading albums").ConfigureAwait(false);

        var items = (result.Items ?? []).Select(ItemMapper.ToAlbum).ToList();
        return new PagedResult<Album>(items, result.TotalRecordCount ?? items.Count, result.StartIndex ?? query.StartIndex);
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
