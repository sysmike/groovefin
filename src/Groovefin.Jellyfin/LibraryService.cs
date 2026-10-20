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
                    p.ParentId = LibraryFolder(connection);
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

                    if (Filters(query) is { Length: > 0 } filters)
                    {
                        p.Filters = filters;
                    }

                    if (query.AlbumArtistId is { } albumArtistId)
                    {
                        p.AlbumArtistIds = [ParseId(albumArtistId)];
                    }

                    if (query.ContributingArtistId is { } contributingArtistId)
                    {
                        p.ContributingArtistIds = [ParseId(contributingArtistId)];
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
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for the now playing cover.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
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

    public async Task<PagedResult<Track>> GetTracksAsync(TrackQuery query, CancellationToken cancellationToken = default)
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
                    p.ParentId = LibraryFolder(connection);
                    p.IncludeItemTypes = [BaseItemKind.Audio];
                    p.Recursive = true;
                    p.StartIndex = query.StartIndex;
                    p.Limit = query.Limit;
                    p.SortBy = TrackSortFields(query.SortBy);
                    p.SortOrder = [order];
                    p.EnableUserData = true;
                    p.EnableTotalRecordCount = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for covers.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.Fields = [ItemFields.DateCreated];
                },
                cancellationToken),
            "Loading tracks").ConfigureAwait(false);

        var items = (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
        return new PagedResult<Track>(items, result.TotalRecordCount ?? items.Count, result.StartIndex ?? query.StartIndex);
    }

    public async Task<FolderContent> GetFolderAsync(string? folderId, CancellationToken cancellationToken = default)
    {
        var (api, connection) = client.Active;
        var parentId = folderId is null ? LibraryFolder(connection) : ParseId(folderId);
        if (parentId is not { } parent)
        {
            var libraries = await GetMusicLibrariesAsync(cancellationToken).ConfigureAwait(false);
            return new FolderContent([.. libraries.Select(l => new Folder(l.Id, l.Name))], []);
        }

        var userId = Guid.Parse(connection.UserId);
        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = parent;
                    p.SortBy = [ItemSortBy.SortName];
                    p.EnableUserData = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for covers.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading folder").ConfigureAwait(false);

        var items = result.Items ?? [];
        return new FolderContent(
            [.. items.Where(i => i.IsFolder == true && i.Id is not null).Select(i => new Folder(ItemMapper.FormatId(i.Id)!, i.Name ?? string.Empty))],
            [.. items.Where(i => i.Type == BaseItemDto_Type.Audio && i.Id is not null).Select(ItemMapper.ToTrack)]);
    }

    public async Task<IReadOnlyList<Track>> GetFolderTracksAsync(string folderId, int limit, CancellationToken cancellationToken = default)
    {
        var id = ParseId(folderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
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
                    p.SortBy = [ItemSortBy.AlbumArtist, ItemSortBy.Album, ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber, ItemSortBy.SortName];
                    p.Limit = limit;
                    p.EnableUserData = true;
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading folder tracks").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
    }

    public async Task<PagedResult<Artist>> GetArtistsAsync(ArtistQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.StartIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Limit);

        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var order = query.Descending ? SortOrder.Descending : SortOrder.Ascending;

        var sortBy = ArtistSortFields(query.SortBy);
        bool? favourites = query.FavouritesOnly ? true : null;

        // /Artists/AlbumArtists and /Artists take the same parameters, in two generated types.
        // TODO(#95): the server marks both obsolete ("Use GetPersons"), but /Persons cannot sort or
        // tell album artists apart yet, and the official web client still uses these endpoints.
#pragma warning disable CS0618
        var result = await JellyfinCall.RunAsync(
            () => query.AlbumArtistsOnly
                ? api.Artists.AlbumArtists.GetAsync(
                    request =>
                    {
                        var p = request.QueryParameters;
                        p.UserId = userId;
                        p.ParentId = LibraryFolder(connection);
                        p.StartIndex = query.StartIndex;
                        p.Limit = query.Limit;
                        p.SortBy = sortBy;
                        p.SortOrder = [order];
                        p.IsFavorite = favourites;
                        p.EnableUserData = true;
                        p.EnableTotalRecordCount = true;
                        p.ImageTypeLimit = 1;
                        p.EnableImageTypes = [ImageType.Primary];
                    },
                    cancellationToken)
                : api.Artists.GetAsync(
                    request =>
                    {
                        var p = request.QueryParameters;
                        p.UserId = userId;
                        p.ParentId = LibraryFolder(connection);
                        p.StartIndex = query.StartIndex;
                        p.Limit = query.Limit;
                        p.SortBy = sortBy;
                        p.SortOrder = [order];
                        p.IsFavorite = favourites;
                        p.EnableUserData = true;
                        p.EnableTotalRecordCount = true;
                        p.ImageTypeLimit = 1;
                        p.EnableImageTypes = [ImageType.Primary];
                    },
                    cancellationToken),
            "Loading artists").ConfigureAwait(false);
#pragma warning restore CS0618

        var items = (result.Items ?? []).Select(ItemMapper.ToArtist).ToList();
        return new PagedResult<Artist>(items, result.TotalRecordCount ?? items.Count, result.StartIndex ?? query.StartIndex);
    }

    public async Task<Artist> GetArtistAsync(string artistId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(artistId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var dto = await JellyfinCall.RunAsync(
            () => api.Items[id].GetAsync(request => request.QueryParameters.UserId = userId, cancellationToken),
            "Loading artist").ConfigureAwait(false);
        return ItemMapper.ToArtist(dto);
    }

    public async Task<IReadOnlyList<Track>> GetInstantMixAsync(string itemId, int limit, CancellationToken cancellationToken = default)
    {
        var id = ParseId(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items[id].InstantMix.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.Limit = limit;
                    p.EnableUserData = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for covers.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                },
                cancellationToken),
            "Loading an instant mix").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
    }

    public async Task<IReadOnlyList<Track>> GetFavouriteTracksAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = LibraryFolder(connection);
                    p.IncludeItemTypes = [BaseItemKind.Audio];
                    p.Recursive = true;
                    p.Filters = [ItemFilter.IsFavorite];
                    p.SortBy = [ItemSortBy.SortName];
                    p.Limit = limit;
                    p.EnableUserData = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for covers.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading favourite tracks").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
    }

    public async Task<IReadOnlyList<Track>> GetArtistTopTracksAsync(string artistId, int limit, CancellationToken cancellationToken = default)
    {
        var id = ParseId(artistId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = LibraryFolder(connection);
                    p.ArtistIds = [id];
                    p.IncludeItemTypes = [BaseItemKind.Audio];
                    p.Recursive = true;
                    p.SortBy = [ItemSortBy.PlayCount, ItemSortBy.SortName];
                    p.SortOrder = [SortOrder.Descending];
                    p.Limit = limit;
                    p.EnableUserData = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for the now playing cover.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading top tracks").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
    }

    public async Task<IReadOnlyList<Track>> GetArtistTracksAsync(string artistId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(artistId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = LibraryFolder(connection);
                    p.ArtistIds = [id];
                    p.IncludeItemTypes = [BaseItemKind.Audio];
                    p.Recursive = true;
                    p.SortBy = [ItemSortBy.ProductionYear, ItemSortBy.Album, ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber];
                    p.SortOrder = [SortOrder.Descending, SortOrder.Ascending, SortOrder.Ascending, SortOrder.Ascending];
                    p.EnableUserData = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for the now playing cover.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading artist tracks").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
    }

    public async Task<IReadOnlyList<Artist>> GetSimilarArtistsAsync(string artistId, int limit, CancellationToken cancellationToken = default)
    {
        var id = ParseId(artistId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items[id].Similar.GetAsync(
                request =>
                {
                    request.QueryParameters.UserId = userId;
                    request.QueryParameters.Limit = limit;
                },
                cancellationToken),
            "Loading similar artists").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToArtist).ToList();
    }

    public async Task<IReadOnlyList<MusicLibrary>> GetMusicLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.UserViews.GetAsync(request => request.QueryParameters.UserId = userId, cancellationToken),
            "Loading libraries").ConfigureAwait(false);

        return (result.Items ?? [])
            .Where(view => view.CollectionType == BaseItemDto_CollectionType.Music && view.Id is not null)
            .Select(view => new MusicLibrary(ItemMapper.FormatId(view.Id)!, view.Name ?? string.Empty))
            .ToList();
    }

    /// <summary>The selected music library as parent folder of browse queries; <see langword="null"/> browses all.</summary>
    private static Guid? LibraryFolder(ServerConnection connection) =>
        connection.LibraryId is { } id ? ParseId(id) : null;

    private static Guid ParseId(string id) =>
        Guid.TryParse(id, out var guid) ? guid : throw new ArgumentException($"'{id}' is not a Jellyfin item ID.", nameof(id));

    public async Task<Genre> GetGenreAsync(string genreId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(genreId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var dto = await JellyfinCall.RunAsync(
            () => api.Items[id].GetAsync(request => request.QueryParameters.UserId = userId, cancellationToken),
            "Loading genre").ConfigureAwait(false);
        return new Genre(ItemMapper.FormatId(dto.Id) ?? genreId, dto.Name ?? string.Empty);
    }

    public async Task<IReadOnlyList<Track>> GetGenreTracksAsync(string genreId, int limit, CancellationToken cancellationToken = default)
    {
        var id = ParseId(genreId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = LibraryFolder(connection);
                    p.GenreIds = [id];
                    p.IncludeItemTypes = [BaseItemKind.Audio];
                    p.Recursive = true;
                    p.SortBy = [ItemSortBy.AlbumArtist, ItemSortBy.Album, ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber];
                    p.Limit = limit;
                    p.EnableUserData = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for covers.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading genre tracks").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
    }

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
                    p.ParentId = LibraryFolder(connection);
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

    private static ItemSortBy[] ArtistSortFields(ArtistSortField sortBy) => sortBy switch
    {
        ArtistSortField.Name => [ItemSortBy.SortName],
        ArtistSortField.DateAdded => [ItemSortBy.DateCreated, ItemSortBy.SortName],
        ArtistSortField.PlayCount => [ItemSortBy.PlayCount, ItemSortBy.SortName],
        ArtistSortField.Random => [ItemSortBy.Random],
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null),
    };

    private static ItemSortBy[] TrackSortFields(TrackSortField sortBy) => sortBy switch
    {
        TrackSortField.Name => [ItemSortBy.SortName],
        TrackSortField.Artist => [ItemSortBy.Artist, ItemSortBy.Album, ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber, ItemSortBy.SortName],
        TrackSortField.Album => [ItemSortBy.Album, ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber, ItemSortBy.SortName],
        TrackSortField.AlbumArtist => [ItemSortBy.AlbumArtist, ItemSortBy.Album, ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber, ItemSortBy.SortName],
        TrackSortField.Year => [ItemSortBy.ProductionYear, ItemSortBy.Album, ItemSortBy.ParentIndexNumber, ItemSortBy.IndexNumber, ItemSortBy.SortName],
        TrackSortField.Duration => [ItemSortBy.Runtime, ItemSortBy.SortName],
        TrackSortField.PlayCount => [ItemSortBy.PlayCount, ItemSortBy.SortName],
        TrackSortField.DateAdded => [ItemSortBy.DateCreated, ItemSortBy.SortName],
        TrackSortField.Random => [ItemSortBy.Random],
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null),
    };

    private static ItemFilter[] Filters(AlbumQuery query)
    {
        var filters = new List<ItemFilter>(2);
        if (query.FavouritesOnly)
        {
            filters.Add(ItemFilter.IsFavorite);
        }

        if (query.PlayedOnly)
        {
            filters.Add(ItemFilter.IsPlayed);
        }

        return [.. filters];
    }

    private static ItemSortBy[] SortFields(AlbumSortField sortBy) => sortBy switch
    {
        AlbumSortField.Name => [ItemSortBy.SortName],
        AlbumSortField.AlbumArtist => [ItemSortBy.AlbumArtist, ItemSortBy.SortName],
        AlbumSortField.Year => [ItemSortBy.ProductionYear, ItemSortBy.SortName],
        AlbumSortField.DateAdded => [ItemSortBy.DateCreated, ItemSortBy.SortName],
        AlbumSortField.PlayCount => [ItemSortBy.PlayCount, ItemSortBy.SortName],
        AlbumSortField.DatePlayed => [ItemSortBy.DatePlayed, ItemSortBy.SortName],
        AlbumSortField.Random => [ItemSortBy.Random],
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null),
    };
}
