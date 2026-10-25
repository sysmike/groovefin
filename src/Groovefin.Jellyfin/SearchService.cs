using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Jellyfin.Mapping;
using Jellyfin.Sdk.Generated.Models;

namespace Groovefin.Jellyfin;

/// <summary>
/// <see cref="ISearchService"/> for the active Jellyfin connection: one request per kind, in parallel.
/// Artists, albums and tracks come from the selected music library; playlists are the user's audio
/// playlists, which live outside the libraries. /Artists returns only the tracks' artists, so album
/// artists are asked for separately and merged in (#243).
/// </summary>
internal sealed class SearchService(JellyfinClient client) : ISearchService
{
    public async Task<SearchResults> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var term = query.Trim();
        if (term.Length == 0)
        {
            return SearchResults.Empty;
        }

        var (api, connection) = client.Active;
        var userId = Guid.Parse(connection.UserId);
        Guid? library = connection.LibraryId is { } id && Guid.TryParse(id, out var guid) ? guid : null;

        // /Artists/AlbumArtists and /Artists take the same parameters, in two generated types.
        // TODO(#95): both are marked obsolete ("Use GetPersons"), but /Persons cannot search artists yet.
#pragma warning disable CS0618
        var artists = JellyfinCall.RunAsync(
            () => api.Artists.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = library;
                    p.SearchTerm = term;
                    p.Limit = limit;
                    p.SortBy = [ItemSortBy.SortName];
                    p.EnableUserData = true;
                    p.EnableTotalRecordCount = false;
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                },
                cancellationToken),
            "Searching artists");
        var albumArtists = JellyfinCall.RunAsync(
            () => api.Artists.AlbumArtists.GetAsync(
                request =>
                {
                    var p = request.QueryParameters;
                    p.UserId = userId;
                    p.ParentId = library;
                    p.SearchTerm = term;
                    p.Limit = limit;
                    p.SortBy = [ItemSortBy.SortName];
                    p.EnableUserData = true;
                    p.EnableTotalRecordCount = false;
                    p.ImageTypeLimit = 1;
                    p.EnableImageTypes = [ImageType.Primary];
                },
                cancellationToken),
            "Searching artists");
#pragma warning restore CS0618

        var albums = FindItemsAsync(BaseItemKind.MusicAlbum, library, "Searching albums");
        var tracks = FindItemsAsync(BaseItemKind.Audio, library, "Searching tracks");
        var playlists = FindItemsAsync(BaseItemKind.Playlist, parent: null, "Searching playlists");

        await Task.WhenAll(artists, albumArtists, albums, tracks, playlists).ConfigureAwait(false);

        var allArtists = Map(await artists.ConfigureAwait(false), ItemMapper.ToArtist)
            .Concat(Map(await albumArtists.ConfigureAwait(false), ItemMapper.ToArtist))
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();

        return new SearchResults(
            allArtists,
            Map(await albums.ConfigureAwait(false), ItemMapper.ToAlbum),
            Map(await tracks.ConfigureAwait(false), ItemMapper.ToTrack),
            Map(await playlists.ConfigureAwait(false), ItemMapper.ToPlaylist));

        Task<BaseItemDtoQueryResult> FindItemsAsync(BaseItemKind kind, Guid? parent, string operation) =>
            JellyfinCall.RunAsync(
                () => api.Items.GetAsync(
                    request =>
                    {
                        var p = request.QueryParameters;
                        p.UserId = userId;
                        p.ParentId = parent;
                        p.IncludeItemTypes = [kind];
                        p.Recursive = true;
                        p.SearchTerm = term;
                        p.Limit = limit;
                        p.SortBy = [ItemSortBy.SortName];
                        p.EnableUserData = true;
                        p.EnableTotalRecordCount = false;
                        p.ImageTypeLimit = 1;
                        p.EnableImageTypes = [ImageType.Primary];
                        if (kind == BaseItemKind.Playlist)
                        {
                            p.MediaTypes = [MediaType.Audio];
                            p.Fields = [ItemFields.ChildCount];
                        }
                    },
                    cancellationToken),
                operation);
    }

    private static List<T> Map<T>(BaseItemDtoQueryResult result, Func<BaseItemDto, T> map) =>
        (result.Items ?? []).Select(map).ToList();
}
