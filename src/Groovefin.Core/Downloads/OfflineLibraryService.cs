using Groovefin.Core.Api;
using Groovefin.Core.Models;

namespace Groovefin.Core.Downloads;

/// <summary>
/// <see cref="ILibraryService"/> for the pages: the server's, but when it cannot be reached the Favourites page and
/// the pages of albums and artists come from the downloads (#335). They then hold only downloaded songs; an
/// artist's similar artists are empty. Every other query goes to the server as before.
/// </summary>
public sealed class OfflineLibraryService(ILibraryService server, OfflineAnswers answers) : ILibraryService
{
    public Task<IReadOnlyList<MusicLibrary>> GetMusicLibrariesAsync(CancellationToken cancellationToken = default) =>
        server.GetMusicLibrariesAsync(cancellationToken);

    public Task<PagedResult<Album>> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var answerable = (query.FavouritesOnly || query.AlbumArtistId is not null) && query.GenreIds.Count == 0 && query.MinYear is null && query.MaxYear is null;
        return answerable
            ? answers.AskAsync(() => server.GetAlbumsAsync(query, cancellationToken), index => Albums(index, query), cancellationToken)
            : server.GetAlbumsAsync(query, cancellationToken);
    }

    public Task<Album> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(() => server.GetAlbumAsync(albumId, cancellationToken), index => AlbumsOf(index).GetValueOrDefault(albumId), cancellationToken);

    public Task<IReadOnlyList<Track>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetAlbumTracksAsync(albumId, cancellationToken),
            index => NullIfEmpty(Songs(index).Where(t => t.AlbumId == albumId).OrderBy(t => t.DiscNumber ?? 1).ThenBy(t => t.TrackNumber ?? int.MaxValue).ToList()),
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

    public Task<PagedResult<Track>> GetTracksAsync(TrackQuery query, CancellationToken cancellationToken = default) =>
        server.GetTracksAsync(query, cancellationToken);

    public Task<IReadOnlyList<Track>> GetTracksByIdAsync(IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default) =>
        server.GetTracksByIdAsync(trackIds, cancellationToken);

    public Task<IReadOnlyDictionary<string, TrackFile>> GetTrackFilesAsync(IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default) =>
        server.GetTrackFilesAsync(trackIds, cancellationToken);

    public Task<FolderContent> GetFolderAsync(string? folderId, CancellationToken cancellationToken = default) =>
        server.GetFolderAsync(folderId, cancellationToken);

    public Task<IReadOnlyList<Track>> GetFolderTracksAsync(string folderId, int limit, CancellationToken cancellationToken = default) =>
        server.GetFolderTracksAsync(folderId, limit, cancellationToken);

    public Task<PagedResult<Artist>> GetArtistsAsync(ArtistQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.FavouritesOnly
            ? answers.AskAsync(
                () => server.GetArtistsAsync(query, cancellationToken),
                index => OfflineAnswers.Page<Artist>(
                    [.. index.Artists.Where(a => a.TrackIds.Any(index.Tracks.ContainsKey)).Select(a => a.Artist).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)],
                    query.StartIndex,
                    query.Limit),
                cancellationToken)
            : server.GetArtistsAsync(query, cancellationToken);
    }

    public Task<Artist> GetArtistAsync(string artistId, CancellationToken cancellationToken = default) =>
        answers.AskAsync(
            () => server.GetArtistAsync(artistId, cancellationToken),
            index => index.Artists.FirstOrDefault(a => a.Artist.Id == artistId)?.Artist
                ?? Songs(index).SelectMany(t => t.Artists).Where(a => a.Id == artistId).Select(a => new Artist { Id = a.Id, Name = a.Name }).FirstOrDefault(),
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
        server.GetAlbumGenresAsync(cancellationToken);

    public Task<Genre> GetGenreAsync(string genreId, CancellationToken cancellationToken = default) =>
        server.GetGenreAsync(genreId, cancellationToken);

    public Task<IReadOnlyList<Track>> GetGenreTracksAsync(string genreId, int limit, CancellationToken cancellationToken = default) =>
        server.GetGenreTracksAsync(genreId, limit, cancellationToken);

    private static IEnumerable<Track> Songs(DownloadIndex index) => index.Tracks.Values.Select(t => t.Track);

    /// <summary>An artist's downloaded songs, album by album in disc and track order.</summary>
    private static IEnumerable<Track> SongsOf(DownloadIndex index, string artistId) =>
        Songs(index)
            .Where(t => t.Artists.Any(a => a.Id == artistId))
            .OrderBy(t => t.AlbumName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.DiscNumber ?? 1)
            .ThenBy(t => t.TrackNumber ?? int.MaxValue);

    /// <summary>Every album with a downloaded song: the favourite ones as saved, the others from their songs.</summary>
    private static Dictionary<string, Album> AlbumsOf(DownloadIndex index)
    {
        var albums = index.Albums
            .Where(a => a.TrackIds.Any(index.Tracks.ContainsKey))
            .ToDictionary(a => a.Album.Id, a => a.Album, StringComparer.Ordinal);
        foreach (var song in Songs(index).Where(t => t.AlbumId is not null))
        {
            albums.TryAdd(song.AlbumId!, new Album
            {
                Id = song.AlbumId!,
                Name = song.AlbumName ?? string.Empty,
                AlbumArtist = song.AlbumArtist,
                PrimaryImageTag = song.AlbumImageTag,
                Year = song.Year,
            });
        }

        return albums;
    }

    private static PagedResult<Album> Albums(DownloadIndex index, AlbumQuery query)
    {
        IEnumerable<Album> albums = query.FavouritesOnly
            ? index.Albums.Where(a => a.TrackIds.Any(index.Tracks.ContainsKey)).Select(a => a.Album)
            : AlbumsOf(index).Values;
        if (query.AlbumArtistId is { } artistId)
        {
            var ofArtist = SongsOf(index, artistId).Select(t => t.AlbumId).OfType<string>().ToHashSet(StringComparer.Ordinal);
            albums = albums.Where(a => a.AlbumArtists.Any(r => r.Id == artistId) || ofArtist.Contains(a.Id));
        }

        return OfflineAnswers.Page<Album>([.. albums.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)], query.StartIndex, query.Limit);
    }

    private static IReadOnlyList<Track>? NullIfEmpty(List<Track> tracks) => tracks.Count > 0 ? tracks : null;
}
