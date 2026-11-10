using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Search;

namespace Groovefin.Core.Downloads;

/// <summary>
/// The library's queries answered from the download index (#400): for every page offline, and for the
/// "Downloaded only" filters. An album, artist or genre is there when one of its songs is downloaded, and holds
/// only those songs. The filters and sort orders are the server's; ties go by name.
/// </summary>
internal static class OfflineQueries
{
    private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

    public static IEnumerable<Track> Songs(DownloadIndex index) => index.Tracks.Values.Select(t => t.Track);

    /// <summary>Every album with a downloaded song, by ID: a favourite one as saved, the others from their songs.</summary>
    public static Dictionary<string, OfflineAlbum> Albums(DownloadIndex index)
    {
        var saved = index.Albums.DistinctBy(a => a.Album.Id).ToDictionary(a => a.Album.Id, StringComparer.Ordinal);
        var albums = new Dictionary<string, OfflineAlbum>(StringComparer.Ordinal);
        foreach (var songs in Songs(index).Where(t => t.AlbumId is not null).GroupBy(t => t.AlbumId!, StringComparer.Ordinal))
        {
            albums[songs.Key] = OfflineAlbum.Of(songs.Key, [.. InAlbumOrder(songs)], saved.GetValueOrDefault(songs.Key)?.Album);
        }

        // A favourite album whose songs came without its ID.
        foreach (var album in saved.Values.Where(a => !albums.ContainsKey(a.Album.Id)))
        {
            var songs = OfflineAnswers.Downloaded(index, album.TrackIds);
            if (songs.Count > 0)
            {
                albums[album.Album.Id] = OfflineAlbum.Of(album.Album.Id, songs, album.Album);
            }
        }

        return albums;
    }

    public static PagedResult<Album> Albums(DownloadIndex index, AlbumQuery query)
    {
        IEnumerable<OfflineAlbum> albums = Albums(index).Values;
        if (query.FavouritesOnly)
        {
            var favourites = index.Albums.Select(a => a.Album.Id).ToHashSet(StringComparer.Ordinal);
            albums = albums.Where(a => a.Album.IsFavorite || favourites.Contains(a.Album.Id));
        }

        if (query.GenreIds.Count > 0)
        {
            albums = albums.Where(a => a.Genres.Any(g => query.GenreIds.Contains(g.Id, StringComparer.Ordinal)));
        }

        if (query.MinYear is { } min)
        {
            albums = albums.Where(a => a.Album.Year >= min);
        }

        if (query.MaxYear is { } max)
        {
            albums = albums.Where(a => a.Album.Year <= max);
        }

        if (query.AlbumArtistId is { } albumArtist)
        {
            albums = albums.Where(a => a.IsByAlbumArtist(albumArtist));
        }

        if (query.ContributingArtistId is { } contributor)
        {
            albums = albums.Where(a => !a.IsByAlbumArtist(contributor) && a.Songs.Any(t => t.Artists.Any(r => r.Id == contributor)));
        }

        if (query.PlayedOnly || query.PlayedBefore is not null)
        {
            albums = albums.Where(a => a.PlayCount > 0);
        }

        if (query.PlayedBefore is { } before)
        {
            albums = albums.Where(a => a.LastPlayed is not { } last || last < before);
        }

        if (query.Ids.Count > 0)
        {
            var ids = query.Ids.ToHashSet(StringComparer.Ordinal);
            albums = albums.Where(a => ids.Contains(a.Album.Id));
        }

        if (query.NameLessThan is { } name)
        {
            albums = albums.Where(a => Names.Compare(a.Album.Name, name) < 0);
        }

        var byName = albums.OrderBy(a => a.Album.Name, Names).ToList();
        IEnumerable<OfflineAlbum> sorted = query.SortBy switch
        {
            AlbumSortField.AlbumArtist => By(byName, a => a.Album.AlbumArtist ?? string.Empty, Names, query.Descending),
            AlbumSortField.Year => By(byName, a => a.Album.Year ?? 0, Comparer<int>.Default, query.Descending),
            AlbumSortField.DateAdded => By(byName, a => a.Album.DateAdded ?? DateTimeOffset.MinValue, Comparer<DateTimeOffset>.Default, query.Descending),
            AlbumSortField.PlayCount => By(byName, a => a.PlayCount, Comparer<int>.Default, query.Descending),
            AlbumSortField.DatePlayed => By(byName, a => a.LastPlayed ?? DateTimeOffset.MinValue, Comparer<DateTimeOffset>.Default, query.Descending),
            AlbumSortField.Random => Shuffled(byName),
            _ => query.Descending ? Enumerable.Reverse(byName) : byName,
        };
        return OfflineAnswers.Page<Album>([.. sorted.Select(a => a.Album)], query.StartIndex, query.Limit);
    }

    /// <summary>
    /// The artists of the downloaded songs: the album artists of their albums, or every artist on them. A
    /// favourite artist counts with the songs downloaded for them, and comes as saved.
    /// </summary>
    public static List<OfflineArtist> Artists(DownloadIndex index, bool albumArtistsOnly)
    {
        var saved = index.Artists.DistinctBy(a => a.Artist.Id).ToDictionary(a => a.Artist.Id, StringComparer.Ordinal);
        var songs = new Dictionary<string, (ArtistRef Ref, List<Track> Songs)>(StringComparer.Ordinal);
        foreach (var song in Songs(index))
        {
            foreach (var artist in ArtistsOf(song, albumArtistsOnly))
            {
                if (!songs.TryGetValue(artist.Id, out var entry))
                {
                    songs[artist.Id] = entry = (artist, new List<Track>());
                }

                entry.Songs.Add(song);
            }
        }

        foreach (var artist in saved.Values.Where(a => !songs.ContainsKey(a.Artist.Id)))
        {
            var downloaded = OfflineAnswers.Downloaded(index, artist.TrackIds);
            if (downloaded.Count > 0)
            {
                songs[artist.Artist.Id] = (new ArtistRef(artist.Artist.Id, artist.Artist.Name), downloaded.ToList());
            }
        }

        return
        [
            .. songs.Values.Select(e => new OfflineArtist(
                saved.GetValueOrDefault(e.Ref.Id)?.Artist ?? new Artist { Id = e.Ref.Id, Name = e.Ref.Name },
                e.Songs,
                saved.ContainsKey(e.Ref.Id))),
        ];
    }

    public static PagedResult<Artist> Artists(DownloadIndex index, ArtistQuery query)
    {
        IEnumerable<OfflineArtist> artists = Artists(index, query.AlbumArtistsOnly);
        if (query.FavouritesOnly)
        {
            artists = artists.Where(a => a.IsFavourite);
        }

        if (query.NameLessThan is { } name)
        {
            artists = artists.Where(a => Names.Compare(a.Artist.Name, name) < 0);
        }

        var byName = artists.OrderBy(a => a.Artist.Name, Names).ToList();
        IEnumerable<OfflineArtist> sorted = query.SortBy switch
        {
            ArtistSortField.DateAdded => By(byName, a => a.DateAdded ?? DateTimeOffset.MinValue, Comparer<DateTimeOffset>.Default, query.Descending),
            ArtistSortField.PlayCount => By(byName, a => a.PlayCount, Comparer<int>.Default, query.Descending),
            ArtistSortField.Random => Shuffled(byName),
            _ => query.Descending ? Enumerable.Reverse(byName) : byName,
        };
        return OfflineAnswers.Page<Artist>([.. sorted.Select(a => a.Artist)], query.StartIndex, query.Limit);
    }

    public static PagedResult<Track> Tracks(DownloadIndex index, TrackQuery query)
    {
        var albums = Albums(index);
        IEnumerable<Track> songs = Songs(index);
        if (query.FavouritesOnly)
        {
            var favourites = index.FavouriteSongs.ToHashSet(StringComparer.Ordinal);
            songs = songs.Where(t => t.IsFavorite || favourites.Contains(t.Id));
        }

        if (query.GenreIds.Count > 0)
        {
            songs = songs.Where(t => GenresOf(t, albums).Any(g => query.GenreIds.Contains(g.Id, StringComparer.Ordinal)));
        }

        if (query.MinYear is { } min)
        {
            songs = songs.Where(t => t.Year >= min);
        }

        if (query.MaxYear is { } max)
        {
            songs = songs.Where(t => t.Year <= max);
        }

        if (query.NameLessThan is { } name)
        {
            songs = songs.Where(t => Names.Compare(t.Name, name) < 0);
        }

        // Ties go by album and track order, then by name.
        var inOrder = InAlbumOrder(songs).ToList();
        IEnumerable<Track> sorted = query.SortBy switch
        {
            TrackSortField.Artist => By(inOrder, t => t.Artists.FirstOrDefault()?.Name ?? string.Empty, Names, query.Descending),
            TrackSortField.Album => By(inOrder, t => t.AlbumName ?? string.Empty, Names, query.Descending),
            TrackSortField.AlbumArtist => By(inOrder, t => t.AlbumArtist ?? string.Empty, Names, query.Descending),
            TrackSortField.Year => By(inOrder, t => t.Year ?? 0, Comparer<int>.Default, query.Descending),
            TrackSortField.Duration => By(inOrder, t => t.Duration ?? TimeSpan.Zero, Comparer<TimeSpan>.Default, query.Descending),
            TrackSortField.PlayCount => By(inOrder, t => t.PlayCount, Comparer<int>.Default, query.Descending),
            TrackSortField.DateAdded => By(inOrder, t => t.DateAdded ?? DateTimeOffset.MinValue, Comparer<DateTimeOffset>.Default, query.Descending),
            TrackSortField.DatePlayed => By(inOrder, t => t.LastPlayed ?? DateTimeOffset.MinValue, Comparer<DateTimeOffset>.Default, query.Descending),
            TrackSortField.Random => Shuffled(inOrder),
            _ => By(inOrder, t => t.Name, Names, query.Descending),
        };
        return OfflineAnswers.Page<Track>([.. sorted], query.StartIndex, query.Limit);
    }

    /// <summary>The genres of the downloaded songs and their albums, by name.</summary>
    public static IReadOnlyList<Genre> Genres(DownloadIndex index) =>
        [.. Albums(index).Values.SelectMany(a => a.Genres).DistinctBy(g => g.Id).OrderBy(g => g.Name, Names)];

    /// <summary>The downloaded songs of a genre, album by album, or <see langword="null"/> when none has it.</summary>
    public static IReadOnlyList<Track>? GenreTracks(DownloadIndex index, string genreId, int limit)
    {
        var albums = Albums(index);
        var songs = Songs(index)
            .Where(t => GenresOf(t, albums).Any(g => g.Id == genreId))
            .OrderBy(t => t.AlbumArtist ?? string.Empty, Names)
            .ThenBy(t => t.AlbumName ?? string.Empty, Names)
            .ThenBy(t => t.DiscNumber ?? 1)
            .ThenBy(t => t.TrackNumber ?? int.MaxValue)
            .Take(limit)
            .ToList();
        return songs.Count > 0 ? songs : null;
    }

    /// <summary>Finds downloaded music as the server's forgiving search does (#391): every word must match.</summary>
    public static SearchResults Search(DownloadIndex index, string query, int limit)
    {
        var words = ForgivingSearch.Words(query);
        if (words.Count == 0)
        {
            return SearchResults.Empty;
        }

        var albums = Albums(index).Values.Select(a => a.Album).OrderBy(a => a.Name, Names);
        var artists = Artists(index, albumArtistsOnly: false).Select(a => a.Artist).OrderBy(a => a.Name, Names);
        var songs = Songs(index).OrderBy(t => t.Name, Names);
        var playlists = index.Playlists.Select(p => OfflinePlaylistService.Downloaded(index, p)).OfType<Playlist>().OrderBy(p => p.Name, Names);
        return new SearchResults(
            ForgivingSearch.Rank(artists, a => a.Id, a => [a.Name], words, limit),
            ForgivingSearch.Rank(albums, a => a.Id, a => [a.Name, a.AlbumArtist], words, limit),
            ForgivingSearch.Rank(songs, t => t.Id, t => [t.Name, t.AlbumName, .. t.Artists.Select(a => a.Name)], words, limit),
            ForgivingSearch.Rank(playlists, p => p.Id, p => [p.Name], words, limit))
        {
            Genres = ForgivingSearch.Rank(Genres(index), g => g.Id, g => [g.Name], words, limit),
        };
    }

    /// <summary>Disc and track order, album by album.</summary>
    public static IOrderedEnumerable<Track> InAlbumOrder(IEnumerable<Track> songs) =>
        songs
            .OrderBy(t => t.AlbumName ?? string.Empty, Names)
            .ThenBy(t => t.AlbumId, StringComparer.Ordinal)
            .ThenBy(t => t.DiscNumber ?? 1)
            .ThenBy(t => t.TrackNumber ?? int.MaxValue)
            .ThenBy(t => t.Name, Names);

    /// <summary>
    /// The song's album artists, or every artist on it. A song downloaded before #400 has no album artists until the
    /// next sync: its own artists stand in.
    /// </summary>
    private static IEnumerable<ArtistRef> ArtistsOf(Track song, bool albumArtistsOnly) =>
        albumArtistsOnly
            ? song.AlbumArtists.Count > 0 ? song.AlbumArtists : song.Artists
            : song.AlbumArtists.Concat(song.Artists).DistinctBy(a => a.Id);

    /// <summary>The song's genres, or its album's when it has none (a favourite album saved with them).</summary>
    private static IReadOnlyList<Genre> GenresOf(Track song, Dictionary<string, OfflineAlbum> albums) =>
        song.Genres.Count > 0 || song.AlbumId is null || !albums.TryGetValue(song.AlbumId, out var album) ? song.Genres : album.Genres;

    /// <summary>Sorted by a key; a stable sort, so the order given breaks ties.</summary>
    private static List<T> By<T, TKey>(List<T> items, Func<T, TKey> key, IComparer<TKey> comparer, bool descending) =>
        descending ? [.. items.OrderByDescending(key, comparer)] : [.. items.OrderBy(key, comparer)];

    private static T[] Shuffled<T>(List<T> items)
    {
        var shuffled = items.ToArray();
        Random.Shared.Shuffle(shuffled);
        return shuffled;
    }
}

/// <summary>A downloaded album with its downloaded songs, and what the queries filter and sort it by.</summary>
internal sealed record OfflineAlbum(Album Album, IReadOnlyList<Track> Songs, IReadOnlyList<Genre> Genres)
{
    public int PlayCount => Songs.Sum(t => t.PlayCount);

    public DateTimeOffset? LastPlayed => Songs.Max(t => t.LastPlayed);

    /// <summary>
    /// Whether the artist is an album artist of it: by the album's artists, or else by its songs' (#400).
    /// </summary>
    public bool IsByAlbumArtist(string artistId) =>
        Album.AlbumArtists.Count > 0
            ? Album.AlbumArtists.Any(a => a.Id == artistId)
            : Songs.Any(t => (t.AlbumArtists.Count > 0 ? t.AlbumArtists : t.Artists).Any(a => a.Id == artistId));

    /// <summary>The album as saved, or made from its songs; with the length of the downloaded ones.</summary>
    public static OfflineAlbum Of(string id, IReadOnlyList<Track> songs, Album? saved)
    {
        var first = songs[0];
        var fromSongs = new Album
        {
            Id = id,
            Name = first.AlbumName ?? string.Empty,
            AlbumArtist = first.AlbumArtist,
            AlbumArtists = first.AlbumArtists,
            PrimaryImageTag = first.AlbumImageTag,
            Year = songs.Select(t => t.Year).FirstOrDefault(y => y is not null),
            DateAdded = songs.Min(t => t.DateAdded),
            PlayCount = songs.Sum(t => t.PlayCount),
        };

        // What the saved album lacks, its songs tell.
        var album = saved is null
            ? fromSongs
            : saved with
            {
                AlbumArtist = saved.AlbumArtist ?? fromSongs.AlbumArtist,
                AlbumArtists = saved.AlbumArtists.Count > 0 ? saved.AlbumArtists : fromSongs.AlbumArtists,
                PrimaryImageTag = saved.PrimaryImageTag ?? fromSongs.PrimaryImageTag,
                Year = saved.Year ?? fromSongs.Year,
                DateAdded = saved.DateAdded ?? fromSongs.DateAdded,
            };
        IReadOnlyList<Genre> genres = album.Genres.Count > 0 ? album.Genres : [.. songs.SelectMany(t => t.Genres).DistinctBy(g => g.Id)];
        return new OfflineAlbum(
            album with { Duration = TimeSpan.FromTicks(songs.Sum(t => t.Duration?.Ticks ?? 0)), Genres = genres },
            songs,
            genres);
    }
}

/// <summary>An artist of the downloaded songs, with them.</summary>
internal sealed record OfflineArtist(Artist Artist, IReadOnlyList<Track> Songs, bool IsSavedFavourite)
{
    public bool IsFavourite => IsSavedFavourite || Artist.IsFavorite;

    public int PlayCount => Songs.Sum(t => t.PlayCount);

    public DateTimeOffset? DateAdded => Songs.Min(t => t.DateAdded);
}
