using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Jellyfin.Mapping;
using Jellyfin.Sdk.Generated.Models;

namespace Groovefin.Jellyfin;

/// <summary><see cref="ILibraryService"/> for the active Jellyfin connection.</summary>
internal sealed class LibraryService(JellyfinClient client) : ILibraryService
{
    /// <summary>Tracks per request of <see cref="GetTrackFilesAsync"/>.</summary>
    internal const int TrackFilePageSize = 100;

    /// <summary>Played songs per request when ordering albums by their songs' plays (#369).</summary>
    internal const int PlayedSongPageSize = 500;

    /// <summary>The most played songs looked at; albums played only before them count as not played.</summary>
    internal const int MaxPlayedSongs = 5000;

    /// <summary>The last order of <see cref="GetAlbumsByPlaysAsync"/>, for the pages after its first.</summary>
    private AlbumOrder? _albumOrder;

    public async Task<PagedResult<Album>> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.StartIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Limit);
        if (query.MinYear > query.MaxYear)
        {
            throw new ArgumentException("MinYear must not be after MaxYear.", nameof(query));
        }

        if (query.SortBy == AlbumSortField.DatePlayed || query.PlayedOnly)
        {
            return await GetAlbumsByPlaysAsync(query, cancellationToken).ConfigureAwait(false);
        }

        var result = await QueryAlbumsAsync(query, idsOnly: false, cancellationToken).ConfigureAwait(false);
        var items = (result.Items ?? []).Select(ItemMapper.ToAlbum).ToList();
        return new PagedResult<Album>(items, result.TotalRecordCount ?? items.Count, result.StartIndex ?? query.StartIndex);
    }

    /// <summary>
    /// Albums by when one of their songs was last played, or only albums with a played song (#369). Jellyfin
    /// keeps play dates on songs: an album's own date stays empty, and it counts as played only once every
    /// song was. The order is worked out for the first page and kept for the next ones.
    /// </summary>
    private async Task<PagedResult<Album>> GetAlbumsByPlaysAsync(AlbumQuery query, CancellationToken cancellationToken)
    {
        var key = OrderKey(query);
        if (query.StartIndex == 0 || _albumOrder is not { } order || order.Key != key)
        {
            // Newest played albums without filters (the start page): enough songs for this page will do.
            var quick = query is { SortBy: AlbumSortField.DatePlayed, Descending: true, PlayedOnly: true } && !HasFilters(query);
            var (played, complete) = await PlayedAlbumIdsAsync(quick ? query.StartIndex + query.Limit : int.MaxValue, cancellationToken).ConfigureAwait(false);
            if (quick)
            {
                order = new AlbumOrder(key, played, complete);
            }
            else
            {
                var matching = await QueryAlbumsAsync(
                    query with { SortBy = query.SortBy == AlbumSortField.DatePlayed ? AlbumSortField.Name : query.SortBy },
                    idsOnly: true,
                    cancellationToken).ConfigureAwait(false);
                var ids = (matching.Items ?? []).Select(item => ItemMapper.FormatId(item.Id)).OfType<string>().ToList();
                order = new AlbumOrder(key, Order(ids, played, query), Complete: true);
            }

            _albumOrder = order;
        }

        var pageIds = order.Ids.Skip(query.StartIndex).Take(query.Limit).ToList();
        IReadOnlyList<Album> albums = pageIds.Count == 0
            ? []
            : (await GetAlbumsAsync(new AlbumQuery { Ids = pageIds, Limit = pageIds.Count }, cancellationToken).ConfigureAwait(false)).Items;
        var byId = albums.DistinctBy(a => a.Id).ToDictionary(a => a.Id, StringComparer.Ordinal);
        var items = pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        // Until every played song was looked at, more albums may follow.
        var total = order.Complete ? order.Ids.Count : Math.Max(order.Ids.Count, query.StartIndex + query.Limit + 1);
        return new PagedResult<Album>(items, total, query.StartIndex);
    }

    /// <summary>
    /// Puts the albums that match the query (<paramref name="matching"/>, by name or in the query's own order)
    /// in the order <paramref name="query"/> asks for, by <paramref name="played"/>: their IDs, most recently
    /// played first. By last play, newest first, the played albums come first and the others follow by name;
    /// oldest first is the reverse. Played only keeps the played ones.
    /// </summary>
    internal static List<string> Order(IReadOnlyList<string> matching, IReadOnlyList<string> played, AlbumQuery query)
    {
        var matches = matching.ToHashSet(StringComparer.Ordinal);
        var playedMatching = played.Where(matches.Contains).Distinct(StringComparer.Ordinal).ToList();
        var wasPlayed = playedMatching.ToHashSet(StringComparer.Ordinal);
        if (query.SortBy != AlbumSortField.DatePlayed)
        {
            return [.. matching.Where(wasPlayed.Contains)];
        }

        List<string> unplayed = query.PlayedOnly ? [] : [.. matching.Where(id => !wasPlayed.Contains(id))];
        if (query.Descending)
        {
            return [.. playedMatching, .. unplayed];
        }

        playedMatching.Reverse();
        return [.. unplayed, .. playedMatching];
    }

    /// <summary>
    /// Gets the albums of the played songs, most recently played first, from at most
    /// <see cref="MaxPlayedSongs"/> songs. Stops once it has <paramref name="wanted"/> albums;
    /// <c>Complete</c> tells whether every played song was looked at.
    /// </summary>
    private async Task<(List<string> Ids, bool Complete)> PlayedAlbumIdsAsync(int wanted, CancellationToken cancellationToken)
    {
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; start < MaxPlayedSongs; start += PlayedSongPageSize)
        {
            var result = await JellyfinCall.RunAsync(
                () => api.Items.GetAsync(
                    request =>
                    {
                        var p = request.QueryParameters;
                        p.UserId = userId;
                        p.ParentId = LibraryFolder(connection);
                        p.IncludeItemTypes = [BaseItemKind.Audio];
                        p.Recursive = true;
                        p.StartIndex = start;
                        p.Limit = PlayedSongPageSize;
                        p.SortBy = [ItemSortBy.DatePlayed];
                        p.SortOrder = [SortOrder.Descending];
                        p.Filters = [ItemFilter.IsPlayed];

                        // Only the album IDs are read.
                        p.EnableImages = false;
                        p.EnableUserData = false;
                        p.EnableTotalRecordCount = false;
                    },
                    cancellationToken),
                "Loading played songs").ConfigureAwait(false);

            var songs = result.Items ?? [];
            ids.AddRange(songs.Select(song => ItemMapper.FormatId(song.AlbumId)).OfType<string>().Where(seen.Add));

            if (songs.Count < PlayedSongPageSize)
            {
                return (ids, true);
            }

            if (ids.Count >= wanted)
            {
                return (ids, false);
            }
        }

        return (ids, false);
    }

    /// <summary>
    /// Asks the server for albums. <paramref name="idsOnly"/> asks for every matching album at once, without
    /// images or user data: only their IDs are read.
    /// </summary>
    private Task<BaseItemDtoQueryResult> QueryAlbumsAsync(AlbumQuery query, bool idsOnly, CancellationToken cancellationToken)
    {
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var order = query.Descending ? SortOrder.Descending : SortOrder.Ascending;

        return JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = LibraryFolder(connection);
                    p.IncludeItemTypes = [BaseItemKind.MusicAlbum];
                    p.Recursive = true;
                    p.SortBy = SortFields(query.SortBy);
                    p.SortOrder = [order];
                    if (idsOnly)
                    {
                        p.EnableImages = false;
                        p.EnableUserData = false;
                        p.EnableTotalRecordCount = false;
                    }
                    else
                    {
                        p.StartIndex = query.StartIndex;
                        p.Limit = query.Limit;
                        p.EnableUserData = true;
                        p.EnableTotalRecordCount = true;
                        p.ImageTypeLimit = 1;
                        p.EnableImageTypes = [ImageType.Primary];
                        p.Fields = [ItemFields.DateCreated];
                    }

                    if (query.GenreIds.Count > 0)
                    {
                        p.GenreIds = query.GenreIds.Select(id => (Guid?)Guid.Parse(id)).ToArray();
                    }

                    if (query.Ids.Count > 0)
                    {
                        p.Ids = query.Ids.Select(id => (Guid?)ParseId(id)).ToArray();
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
            "Loading albums");
    }

    private static bool HasFilters(AlbumQuery query) =>
        query.GenreIds.Count > 0 || query.Ids.Count > 0 || query.MinYear is not null || query.MaxYear is not null
        || query.FavouritesOnly || query.AlbumArtistId is not null || query.ContributingArtistId is not null;

    /// <summary>What the order of <see cref="GetAlbumsByPlaysAsync"/> depends on: everything but the page.</summary>
    private string OrderKey(AlbumQuery query)
    {
        var connection = client.Active.Connection;
        return string.Join(
            '|',
            connection.UserId,
            connection.LibraryId,
            query.SortBy,
            query.Descending,
            query.PlayedOnly,
            string.Join(',', query.GenreIds),
            string.Join(',', query.Ids),
            query.MinYear,
            query.MaxYear,
            query.FavouritesOnly,
            query.AlbumArtistId,
            query.ContributingArtistId);
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

    public async Task<TrackDetails> GetTrackDetailsAsync(string trackId, CancellationToken cancellationToken = default)
    {
        var id = ParseId(trackId);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        // One item from /Items/{id} carries every field: people, studios, provider IDs, media sources and streams.
        var dto = await JellyfinCall.RunAsync(
            () => api.Items[id].GetAsync(request => request.QueryParameters.UserId = userId, cancellationToken),
            "Loading track details").ConfigureAwait(false);
        return ItemMapper.ToTrackDetails(dto);
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

    public async Task<IReadOnlyList<Track>> GetTracksByIdAsync(IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        if (trackIds.Count == 0)
        {
            return [];
        }

        Guid?[] ids = [.. trackIds.Select(id => (Guid?)ParseId(id))];
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);

        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.Ids = ids;
                    p.IncludeItemTypes = [BaseItemKind.Audio];
                    p.EnableUserData = true;
                    // Primary only: the track DTO then carries AlbumPrimaryImageTag for covers.
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                    p.EnableTotalRecordCount = false;
                },
                cancellationToken),
            "Loading tracks").ConfigureAwait(false);

        return (result.Items ?? []).Select(ItemMapper.ToTrack).ToList();
    }

    public async Task<IReadOnlyDictionary<string, TrackFile>> GetTrackFilesAsync(IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var files = new Dictionary<string, TrackFile>(StringComparer.Ordinal);

        // A hundred IDs keep the URL short; only the media sources, without images or user data.
        foreach (var ids in trackIds.Chunk(TrackFilePageSize).Select(chunk => chunk.Select(id => (Guid?)ParseId(id)).ToArray()))
        {
            var result = await JellyfinCall.RunAsync(
                () => api.Items.GetAsync(
                    request =>
                    {
                        var p = request.QueryParameters;
                        p.UserId = userId;
                        p.Ids = ids;
                        p.Fields = [ItemFields.MediaSources];
                        p.EnableImages = false;
                        p.EnableUserData = false;
                        p.EnableTotalRecordCount = false;
                    },
                    cancellationToken),
                "Loading track files").ConfigureAwait(false);
            var known = (result.Items ?? [])
                .Select(dto => (Id: ItemMapper.FormatId(dto.Id), File: ItemMapper.ToTrackFile(dto)))
                .Where(item => item.Id is not null && item.File is not null);
            foreach (var (id, file) in known)
            {
                files[id!] = file!;
            }
        }

        return files;
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
        TrackSortField.DatePlayed => [ItemSortBy.DatePlayed, ItemSortBy.SortName],
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null),
    };

    private static ItemFilter[] Filters(AlbumQuery query)
    {
        var filters = new List<ItemFilter>(2);
        if (query.FavouritesOnly)
        {
            filters.Add(ItemFilter.IsFavorite);
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
        // Ordered by the songs' plays in GetAlbumsByPlaysAsync, not by the server (#369).
        AlbumSortField.DatePlayed => [ItemSortBy.SortName],
        AlbumSortField.Random => [ItemSortBy.Random],
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null),
    };

    private sealed record AlbumOrder(string Key, List<string> Ids, bool Complete);
}
