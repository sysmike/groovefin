using Groovefin.Core.Api;
using Groovefin.Core.Models;

namespace Groovefin.Core.Downloads;

/// <summary>
/// <see cref="ILibraryService"/> for the pages: the server's, but when it cannot be reached the pages come from the
/// downloads (#335, #400): Home, Albums, Artists, Songs, Genres, Favourites and the pages of albums, artists and
/// genres (<see cref="OfflineQueries"/>). They then hold only downloaded songs; an artist's similar artists are
/// empty. A query with <c>DownloadedOnly</c> is answered from the downloads online too. Folders, instant mixes and
/// what downloads and the info dialog need still go to the server.
/// </summary>
public sealed class OfflineLibraryService(ILibraryService server, OfflineAnswers answers) : ILibraryService
{
    public Task<IReadOnlyList<MusicLibrary>> GetMusicLibrariesAsync(CancellationToken cancellationToken = default) =>
        server.GetMusicLibrariesAsync(cancellationToken);

    public Task<PagedResult<Album>> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.DownloadedOnly
            ? answers.FromDownloadsAsync(index => OfflineQueries.Albums(index, query), Empty<Album>(query.StartIndex), cancellationToken)
            : answers.AskAsync(() => server.GetAlbumsAsync(query, cancellationToken), index => OfflineQueries.Albums(index, query), cancellationToken);
    }

    public Task<Album> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetAlbumAsync(albumId, cancellationToken),
            index => OfflineQueries.Albums(index).GetValueOrDefault(albumId)?.Album,
            cancellationToken);

    public Task<IReadOnlyList<Track>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetAlbumTracksAsync(albumId, cancellationToken),
            index => OfflineQueries.Albums(index).GetValueOrDefault(albumId)?.Songs,
            cancellationToken);

    public Task<TrackDetails> GetTrackDetailsAsync(string trackId, CancellationToken cancellationToken = default) =>
        server.GetTrackDetailsAsync(trackId, cancellationToken);

    public Task SetFavouriteAsync(string itemId, bool isFavourite, CancellationToken cancellationToken = default) =>
        server.SetFavouriteAsync(itemId, isFavourite, cancellationToken);

    public Task<IReadOnlyList<Track>> GetFavouriteTracksAsync(int limit, CancellationToken cancellationToken = default) =>
        answers.AskAsync<IReadOnlyList<Track>>(
            () => server.GetFavouriteTracksAsync(limit, cancellationToken),
            index => [.. OfflineAnswers.Downloaded(index, index.FavouriteSongs).Take(limit)],
            cancellationToken);

    public Task<IReadOnlyList<Track>> GetInstantMixAsync(string itemId, int limit, CancellationToken cancellationToken = default) =>
        server.GetInstantMixAsync(itemId, limit, cancellationToken);

    public Task<PagedResult<Track>> GetTracksAsync(TrackQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.DownloadedOnly
            ? answers.FromDownloadsAsync(index => OfflineQueries.Tracks(index, query), Empty<Track>(query.StartIndex), cancellationToken)
            : answers.AskAsync(() => server.GetTracksAsync(query, cancellationToken), index => OfflineQueries.Tracks(index, query), cancellationToken);
    }

    public Task<IReadOnlyList<Track>> GetTracksByIdAsync(IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetTracksByIdAsync(trackIds, cancellationToken),
            index => OfflineAnswers.Downloaded(index, trackIds),
            cancellationToken);

    public Task<IReadOnlyDictionary<string, TrackFile>> GetTrackFilesAsync(IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default) =>
        server.GetTrackFilesAsync(trackIds, cancellationToken);

    public Task<FolderContent> GetFolderAsync(string? folderId, CancellationToken cancellationToken = default) =>
        server.GetFolderAsync(folderId, cancellationToken);

    public Task<IReadOnlyList<Track>> GetFolderTracksAsync(string folderId, int limit, CancellationToken cancellationToken = default) =>
        server.GetFolderTracksAsync(folderId, limit, cancellationToken);

    public Task<PagedResult<Artist>> GetArtistsAsync(ArtistQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.DownloadedOnly
            ? answers.FromDownloadsAsync(index => OfflineQueries.Artists(index, query), Empty<Artist>(query.StartIndex), cancellationToken)
            : answers.AskAsync(() => server.GetArtistsAsync(query, cancellationToken), index => OfflineQueries.Artists(index, query), cancellationToken);
    }

    public Task<IReadOnlyList<Artist>> GetArtistsByIdAsync(IReadOnlyList<string> artistIds, CancellationToken cancellationToken = default) =>
        answers.AskAsync<IReadOnlyList<Artist>>(
            () => server.GetArtistsByIdAsync(artistIds, cancellationToken),
            index =>
            {
                var wanted = artistIds.ToHashSet(StringComparer.Ordinal);
                return [.. OfflineQueries.Artists(index, albumArtistsOnly: false).Select(a => a.Artist).Where(a => wanted.Contains(a.Id))];
            },
            cancellationToken);

    public Task<Artist> GetArtistAsync(string artistId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetArtistAsync(artistId, cancellationToken),
            index => OfflineQueries.Artists(index, albumArtistsOnly: false).FirstOrDefault(a => a.Artist.Id == artistId)?.Artist,
            cancellationToken);

    public Task<IReadOnlyList<Track>> GetArtistTopTracksAsync(string artistId, int limit, CancellationToken cancellationToken = default) =>
        answers.AskAsync<IReadOnlyList<Track>>(
            () => server.GetArtistTopTracksAsync(artistId, limit, cancellationToken),
            index => [.. SongsOf(index, artistId).OrderByDescending(t => t.PlayCount).Take(limit)],
            cancellationToken);

    public Task<IReadOnlyList<Track>> GetArtistTracksAsync(string artistId, CancellationToken cancellationToken = default) =>
        answers.AskAsync<IReadOnlyList<Track>>(
            () => server.GetArtistTracksAsync(artistId, cancellationToken),
            index => [.. SongsOf(index, artistId)],
            cancellationToken);

    public Task<IReadOnlyList<Artist>> GetSimilarArtistsAsync(string artistId, int limit, CancellationToken cancellationToken = default) =>
        answers.AskAsync<IReadOnlyList<Artist>>(() => server.GetSimilarArtistsAsync(artistId, limit, cancellationToken), _ => [], cancellationToken);

    public Task<IReadOnlyList<Genre>> GetAlbumGenresAsync(CancellationToken cancellationToken = default) =>
        answers.AskAsync(() => server.GetAlbumGenresAsync(cancellationToken), OfflineQueries.Genres, cancellationToken);

    public Task<Genre> GetGenreAsync(string genreId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetGenreAsync(genreId, cancellationToken),
            index => OfflineQueries.Genres(index).FirstOrDefault(g => g.Id == genreId),
            cancellationToken);

    public Task<IReadOnlyList<Track>> GetGenreTracksAsync(string genreId, int limit, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetGenreTracksAsync(genreId, limit, cancellationToken),
            index => OfflineQueries.GenreTracks(index, genreId, limit),
            cancellationToken);

    /// <summary>An artist's downloaded songs (on them, or on their albums), album by album in disc and track order.</summary>
    private static IOrderedEnumerable<Track> SongsOf(DownloadIndex index, string artistId) =>
        OfflineQueries.InAlbumOrder(
            OfflineQueries.Songs(index).Where(t => t.Artists.Any(a => a.Id == artistId) || t.AlbumArtists.Any(a => a.Id == artistId)));

    private static PagedResult<T> Empty<T>(int startIndex) => new([], 0, startIndex);
}
