using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Jellyfin.Mapping;
using Jellyfin.Sdk.Generated.Models;

namespace Groovefin.Jellyfin;

/// <summary>
/// <see cref="ISearchService"/> for the active Jellyfin connection: one request per kind, in parallel.
/// Artists, albums and tracks come from the selected music library; playlists are the user's audio
/// playlists, which live outside the libraries.
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

        // TODO(#95): /Artists is marked obsolete ("Use GetPersons"), but /Persons cannot search artists yet.
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

        await Task.WhenAll(artists, albums, tracks, playlists).ConfigureAwait(false);

        return new SearchResults(
            Map(await artists.ConfigureAwait(false), ItemMapper.ToArtist),
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
