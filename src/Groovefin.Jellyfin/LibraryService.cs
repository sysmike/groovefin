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
    private PlayOrder? _albumOrder;

    /// <summary>The last order of <see cref="GetArtistsByPlaysAsync"/>, for the pages after its first.</summary>
    private PlayOrder? _artistOrder;

    public async Task<PagedResult<Album>> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.StartIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Limit);
        if (query.MinYear > query.MaxYear)
        {
            throw new ArgumentException("MinYear must not be after MaxYear.", nameof(query));
        }

        if (query.SortBy is AlbumSortField.DatePlayed or AlbumSortField.PlayCount || query.PlayedOnly)
        {
            return await GetAlbumsByPlaysAsync(query, cancellationToken).ConfigureAwait(false);
        }

        var result = await QueryAlbumsAsync(query, idsOnly: false, cancellationToken).ConfigureAwait(false);
        var items = (result.Items ?? []).Select(ItemMapper.ToAlbum).ToList();
        return new PagedResult<Album>(items, result.TotalRecordCount ?? items.Count, result.StartIndex ?? query.StartIndex);
    }

    /// <summary>
    /// Albums by when one of their songs was last played (#369) or by how often their songs were played (#372),
    /// or only albums with a played song. Jellyfin keeps play dates and counts on songs: an album's own stay
    /// empty, and it counts as played only once every song was. The order is worked out for the first page and
    /// kept for the next ones.
    /// </summary>
    private async Task<PagedResult<Album>> GetAlbumsByPlaysAsync(AlbumQuery query, CancellationToken cancellationToken)
    {
        var key = OrderKey(query);
        if (query.StartIndex == 0 || _albumOrder is not { } order || order.Key != key)
        {
            // Newest played albums without filters (the start page): enough songs for this page will do.
            var quick = query is { SortBy: AlbumSortField.DatePlayed, Descending: true, PlayedOnly: true } && !HasFilters(query);
            var byCount = query.SortBy == AlbumSortField.PlayCount;
            var (played, plays, complete) = await PlayedSongsAsync(
                byCount, quick ? query.StartIndex + query.Limit : int.MaxValue, song => [song.AlbumId], cancellationToken).ConfigureAwait(false);
            if (quick)
            {
                order = new PlayOrder(key, played, complete);
            }
            else
            {
                var matching = await QueryAlbumsAsync(
                    query with { SortBy = query.SortBy is AlbumSortField.DatePlayed or AlbumSortField.PlayCount ? AlbumSortField.Name : query.SortBy },
                    idsOnly: true,
                    cancellationToken).ConfigureAwait(false);
                var ids = (matching.Items ?? []).Select(item => ItemMapper.FormatId(item.Id)).OfType<string>().ToList();
                order = new PlayOrder(key, Order(ids, byCount ? MostPlayed(ids, plays) : played, query), Complete: true);
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
    /// or most often played first. By last play or play count, from the top, the played albums come first and
    /// the others follow by name; from the bottom is the reverse. Played only keeps the played ones.
    /// </summary>
    internal static List<string> Order(IReadOnlyList<string> matching, IReadOnlyList<string> played, AlbumQuery query)
    {
        var matches = matching.ToHashSet(StringComparer.Ordinal);
        var playedMatching = played.Where(matches.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (query.SortBy is not (AlbumSortField.DatePlayed or AlbumSortField.PlayCount))
        {
            var wasPlayed = playedMatching.ToHashSet(StringComparer.Ordinal);
            return [.. matching.Where(wasPlayed.Contains)];
        }

        return Ranked(matching, playedMatching, query.Descending, query.PlayedOnly);
    }

    /// <summary>
    /// Puts <paramref name="played"/> (all in <paramref name="matching"/>, most recently or most often played
    /// first) before the rest of <paramref name="matching"/> (by name), or for ascending order the rest first
    /// and the played ones reversed. <paramref name="playedOnly"/> leaves the rest out.
    /// </summary>
    private static List<string> Ranked(IReadOnlyList<string> matching, IReadOnlyList<string> played, bool descending, bool playedOnly)
    {
        var wasPlayed = played.ToHashSet(StringComparer.Ordinal);
        List<string> unplayed = playedOnly ? [] : [.. matching.Where(id => !wasPlayed.Contains(id))];
        return descending ? [.. played, .. unplayed] : [.. unplayed, .. played.Reverse()];
    }

    /// <summary>
    /// Gets the played albums or artists of <paramref name="matching"/> (by name), most played first: by the
    /// sum of their songs' plays, ties by name.
    /// </summary>
    internal static List<string> MostPlayed(IReadOnlyList<string> matching, IReadOnlyDictionary<string, int> plays) =>
        [.. matching.Where(plays.ContainsKey).OrderByDescending(id => plays[id])];

    /// <summary>
    /// Gets what the played songs are <paramref name="credited"/> to (their album, or their artists), most
    /// recently played first, or for <paramref name="byCount"/> in the order of their most played song, with
    /// the sum of each one's song plays. Reads at most <see cref="MaxPlayedSongs"/> songs, so the least played
    /// or longest ago drop out. Stops once it has <paramref name="wanted"/> of them; <c>Complete</c> tells
    /// whether every played song was looked at.
    /// </summary>
    private async Task<(List<string> Ids, Dictionary<string, int> Plays, bool Complete)> PlayedSongsAsync(
        bool byCount, int wanted, Func<BaseItemDto, IEnumerable<Guid?>> credited, CancellationToken cancellationToken)
    {
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var ids = new List<string>();
        var plays = new Dictionary<string, int>(StringComparer.Ordinal);
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
                        p.SortBy = [byCount ? ItemSortBy.PlayCount : ItemSortBy.DatePlayed];
                        p.SortOrder = [SortOrder.Descending];
                        p.Filters = [ItemFilter.IsPlayed];

                        // Only the albums or artists are read, and for the counts the songs' plays.
                        p.EnableImages = false;
                        p.EnableUserData = byCount;
                        p.EnableTotalRecordCount = false;
                    },
                    cancellationToken),
                "Loading played songs").ConfigureAwait(false);

            var songs = result.Items ?? [];
            foreach (var (id, count) in songs.SelectMany(song => credited(song)
                .Select(ItemMapper.FormatId)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(credit => (Id: credit, Count: song.UserData?.PlayCount ?? 0))))
            {
                if (plays.TryAdd(id, count))
                {
                    ids.Add(id);
                }
                else
                {
                    plays[id] += count;
                }
            }

            if (songs.Count < PlayedSongPageSize)
            {
                return (ids, plays, true);
            }

            if (ids.Count >= wanted)
            {
                return (ids, plays, false);
            }
        }

        return (ids, plays, false);
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

                    // The filters of a mix (#401).
                    if (query.GenreIds.Count > 0)
                    {
                        p.GenreIds = query.GenreIds.Select(id => (Guid?)Guid.Parse(id)).ToArray();
                    }

                    if (Years(query.MinYear, query.MaxYear) is { } years)
                    {
                        p.Years = years;
                    }

                    if (query.FavouritesOnly)
                    {
                        p.Filters = [ItemFilter.IsFavorite];
                    }
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

        if (query.SortBy == ArtistSortField.PlayCount)
        {
            return await GetArtistsByPlaysAsync(query, cancellationToken).ConfigureAwait(false);
        }

        var result = await QueryArtistsAsync(query, idsOnly: false, cancellationToken).ConfigureAwait(false);
        var items = (result.Items ?? []).Select(ItemMapper.ToArtist).ToList();
        return new PagedResult<Artist>(items, result.TotalRecordCount ?? items.Count, result.StartIndex ?? query.StartIndex);
    }

    /// <summary>
    /// Artists by how often their songs were played (#374). Jellyfin counts plays on songs: an artist's own
    /// count stays 0. A song counts for its album artists, or when every artist is listed, for its track
    /// artists. The order is worked out for the first page and kept for the next ones.
    /// </summary>
    private async Task<PagedResult<Artist>> GetArtistsByPlaysAsync(ArtistQuery query, CancellationToken cancellationToken)
    {
        var key = string.Join('|', client.Active.Connection.UserId, client.Active.Connection.LibraryId, query.AlbumArtistsOnly, query.Descending, query.FavouritesOnly);
        if (query.StartIndex == 0 || _artistOrder is not { } order || order.Key != key)
        {
            var (_, plays, _) = await PlayedSongsAsync(
                byCount: true,
                int.MaxValue,
                song => (query.AlbumArtistsOnly ? song.AlbumArtists : song.ArtistItems)?.Select(artist => artist.Id) ?? [],
                cancellationToken).ConfigureAwait(false);
            var matching = await QueryArtistsAsync(query with { SortBy = ArtistSortField.Name, Descending = false }, idsOnly: true, cancellationToken).ConfigureAwait(false);
            var ids = (matching.Items ?? []).Select(item => ItemMapper.FormatId(item.Id)).OfType<string>().ToList();
            order = new PlayOrder(key, Ranked(ids, MostPlayed(ids, plays), query.Descending, playedOnly: false), Complete: true);
            _artistOrder = order;
        }

        var pageIds = order.Ids.Skip(query.StartIndex).Take(query.Limit).ToList();
        List<Artist> artists = pageIds.Count == 0 ? [] : await QueryArtistsByIdAsync(pageIds, cancellationToken).ConfigureAwait(false);
        var byId = artists.DistinctBy(a => a.Id).ToDictionary(a => a.Id, StringComparer.Ordinal);
        return new PagedResult<Artist>([.. pageIds.Where(byId.ContainsKey).Select(id => byId[id])], order.Ids.Count, query.StartIndex);
    }

    public async Task<IReadOnlyList<Artist>> GetArtistsByIdAsync(IReadOnlyList<string> artistIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artistIds);
        return artistIds.Count == 0 ? [] : await QueryArtistsByIdAsync(artistIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets artists by ID, in no particular order.</summary>
    private async Task<List<Artist>> QueryArtistsByIdAsync(IReadOnlyList<string> artistIds, CancellationToken cancellationToken)
    {
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var result = await JellyfinCall.RunAsync(
            () => api.Items.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.Ids = artistIds.Select(id => (Guid?)ParseId(id)).ToArray();
                    p.EnableUserData = true;
                    p.EnableTotalRecordCount = false;
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                },
                cancellationToken),
            "Loading artists").ConfigureAwait(false);
        return [.. (result.Items ?? []).Select(ItemMapper.ToArtist)];
    }

    /// <summary>
    /// Asks the server for artists or album artists. <paramref name="idsOnly"/> asks for every matching one at
    /// once, without images or user data: only their IDs are read.
    /// </summary>
    private Task<BaseItemDtoQueryResult> QueryArtistsAsync(ArtistQuery query, bool idsOnly, CancellationToken cancellationToken)
    {
        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        var order = query.Descending ? SortOrder.Descending : SortOrder.Ascending;

        var sortBy = ArtistSortFields(query.SortBy);
        bool? favourites = query.FavouritesOnly ? true : null;
        int? startIndex = idsOnly ? null : query.StartIndex;
        int? limit = idsOnly ? null : query.Limit;

        // /Artists/AlbumArtists and /Artists take the same parameters, in two generated types.
        // TODO(#95): the server marks both obsolete ("Use GetPersons"), but /Persons cannot sort or
        // tell album artists apart yet, and the official web client still uses these endpoints.
#pragma warning disable CS0618
        return JellyfinCall.RunAsync(
            () => query.AlbumArtistsOnly
                ? api.Artists.AlbumArtists.GetAsync(
                    request =>
                    {
                        var p = request.QueryParameters;
                        p.UserId = userId;
                        p.ParentId = LibraryFolder(connection);
                        p.StartIndex = startIndex;
                        p.Limit = limit;
                        p.SortBy = sortBy;
                        p.SortOrder = [order];
                        p.IsFavorite = favourites;
                        p.EnableUserData = !idsOnly;
                        p.EnableTotalRecordCount = !idsOnly;
                        p.EnableImages = !idsOnly;
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
                        p.StartIndex = startIndex;
                        p.Limit = limit;
                        p.SortBy = sortBy;
                        p.SortOrder = [order];
                        p.IsFavorite = favourites;
                        p.EnableUserData = !idsOnly;
                        p.EnableTotalRecordCount = !idsOnly;
                        p.EnableImages = !idsOnly;
                        p.ImageTypeLimit = 1;
                        p.EnableImageTypes = [ImageType.Primary];
                    },
                    cancellationToken),
            "Loading artists");
#pragma warning restore CS0618
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
    internal static int?[]? Years(AlbumQuery query) => Years(query.MinYear, query.MaxYear);

    /// <summary>The years from <paramref name="minYear"/> to <paramref name="maxYear"/>, or <see langword="null"/> for any year.</summary>
    internal static int?[]? Years(int? minYear, int? maxYear)
    {
        if (minYear is null && maxYear is null)
        {
            return null;
        }

        var first = minYear ?? 1900;
        var last = maxYear ?? DateTime.UtcNow.Year + 1;
        return Enumerable.Range(first, last - first + 1).Select(y => (int?)y).ToArray();
    }

    private static ItemSortBy[] ArtistSortFields(ArtistSortField sortBy) => sortBy switch
    {
        ArtistSortField.Name => [ItemSortBy.SortName],
        ArtistSortField.DateAdded => [ItemSortBy.DateCreated, ItemSortBy.SortName],
        // Ordered by the songs' plays in GetArtistsByPlaysAsync, not by the server (#374).
        ArtistSortField.PlayCount => [ItemSortBy.SortName],
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
        // Ordered by the songs' plays in GetAlbumsByPlaysAsync, not by the server (#372).
        AlbumSortField.PlayCount => [ItemSortBy.SortName],
        // Ordered by the songs' plays in GetAlbumsByPlaysAsync, not by the server (#369).
        AlbumSortField.DatePlayed => [ItemSortBy.SortName],
        AlbumSortField.Random => [ItemSortBy.Random],
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null),
    };

    /// <summary>Albums or artists in the order of their songs' plays, for the query <c>Key</c> names.</summary>
    private sealed record PlayOrder(string Key, List<string> Ids, bool Complete);
}
