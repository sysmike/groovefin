using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;

namespace Groovefin.Core.Tests.Downloads;

/// <summary>The library's queries answered from the download index (#400).</summary>
public sealed class OfflineQueriesTests
{
    private static readonly ArtistRef DaftPunk = new("ar1", "Daft Punk");
    private static readonly ArtistRef Romanthony = new("ar2", "Romanthony");
    private static readonly ArtistRef Various = new("ar3", "Various Artists");
    private static readonly Genre House = new("g1", "House");
    private static readonly Genre Disco = new("g2", "Disco");

    private readonly DownloadIndex _index = new()
    {
        FavouriteSongs = ["s3"],
        Albums = [new DownloadedAlbum { Album = new Album { Id = "al2", Name = "Homework", AlbumArtists = [DaftPunk], IsFavorite = true, Genres = [House] }, TrackIds = ["s3"] }],
    };

    public OfflineQueriesTests()
    {
        // Discovery (2001) by Daft Punk, with Romanthony on one song; a compilation; Homework, a favourite album.
        Add(new Track { Id = "s1", Name = "One More Time", AlbumId = "al1", AlbumName = "Discovery", AlbumArtist = "Daft Punk", AlbumArtists = [DaftPunk], Artists = [DaftPunk, Romanthony], TrackNumber = 1, Year = 2001, Genres = [House], PlayCount = 5, LastPlayed = At(2026, 1), DateAdded = At(2024, 1), Duration = TimeSpan.FromMinutes(5) });
        Add(new Track { Id = "s2", Name = "Aerodynamic", AlbumId = "al1", AlbumName = "Discovery", AlbumArtist = "Daft Punk", AlbumArtists = [DaftPunk], Artists = [DaftPunk], TrackNumber = 2, Year = 2001, Genres = [House], PlayCount = 1, LastPlayed = At(2025, 6), DateAdded = At(2024, 1), Duration = TimeSpan.FromMinutes(3) });
        Add(new Track { Id = "s3", Name = "Da Funk", AlbumId = "al2", AlbumName = "Homework", AlbumArtist = "Daft Punk", Artists = [DaftPunk], TrackNumber = 7, Year = 1997, DateAdded = At(2023, 1) });
        Add(new Track { Id = "s4", Name = "Bad Girls", AlbumId = "al3", AlbumName = "Club Classics", AlbumArtist = "Various Artists", AlbumArtists = [Various], Artists = [new ArtistRef("ar4", "Donna Summer")], TrackNumber = 1, Year = 1979, Genres = [Disco], DateAdded = At(2025, 1) });
    }

    private static DateTimeOffset At(int year, int month) => new(year, month, 1, 0, 0, 0, TimeSpan.Zero);

    private void Add(Track track) =>
        _index.Tracks[track.Id] = new DownloadedTrack { Track = track, FileName = $"{track.Id}.flac", Format = "original" };

    private string[] Albums(AlbumQuery query) => [.. OfflineQueries.Albums(_index, query).Items.Select(a => a.Name)];

    private string[] Artists(ArtistQuery query) => [.. OfflineQueries.Artists(_index, query).Items.Select(a => a.Name)];

    private string[] Songs(TrackQuery query) => [.. OfflineQueries.Tracks(_index, query).Items.Select(t => t.Name)];

    [Fact]
    public void Albums_come_from_their_songs_with_the_downloaded_length_and_plays()
    {
        var discovery = OfflineQueries.Albums(_index)["al1"].Album;

        discovery.Should().BeEquivalentTo(new { Name = "Discovery", AlbumArtist = "Daft Punk", Year = 2001, PlayCount = 6, Duration = TimeSpan.FromMinutes(8) });
        discovery.Genres.Should().Equal(House);
        OfflineQueries.Albums(_index)["al2"].Album.IsFavorite.Should().BeTrue("a favourite album comes as saved");
    }

    [Fact]
    public void Albums_sort_as_on_the_server()
    {
        Albums(new AlbumQuery()).Should().Equal("Club Classics", "Discovery", "Homework");
        Albums(new AlbumQuery { Descending = true }).Should().Equal("Homework", "Discovery", "Club Classics");
        Albums(new AlbumQuery { SortBy = AlbumSortField.Year }).Should().Equal("Club Classics", "Homework", "Discovery");
        Albums(new AlbumQuery { SortBy = AlbumSortField.DateAdded, Descending = true }).Should().Equal("Club Classics", "Discovery", "Homework");
        Albums(new AlbumQuery { SortBy = AlbumSortField.PlayCount, Descending = true }).Should().Equal(new[] { "Discovery", "Club Classics", "Homework" }, "ties go by name");
        Albums(new AlbumQuery { SortBy = AlbumSortField.Random }).Should().BeEquivalentTo("Club Classics", "Discovery", "Homework");
    }

    [Fact]
    public void Albums_filter_as_on_the_server()
    {
        Albums(new AlbumQuery { FavouritesOnly = true }).Should().Equal("Homework");
        Albums(new AlbumQuery { GenreIds = ["g1"] }).Should().Equal(new[] { "Discovery", "Homework" }, "a saved album has its own genres");
        Albums(new AlbumQuery { MinYear = 1990, MaxYear = 2000 }).Should().Equal("Homework");
        Albums(new AlbumQuery { AlbumArtistId = "ar1" }).Should().Equal("Discovery", "Homework");
        Albums(new AlbumQuery { ContributingArtistId = "ar2" }).Should().Equal("Discovery");
        Albums(new AlbumQuery { ContributingArtistId = "ar1" }).Should().BeEmpty("Daft Punk is the album artist");
        Albums(new AlbumQuery { PlayedOnly = true }).Should().Equal("Discovery");
        Albums(new AlbumQuery { PlayedBefore = At(2025, 12) }).Should().BeEmpty("a song of it was played since");
        Albums(new AlbumQuery { PlayedBefore = At(2026, 2) }).Should().Equal("Discovery");
        Albums(new AlbumQuery { Ids = ["al3", "al2"] }).Should().Equal("Club Classics", "Homework");
        Albums(new AlbumQuery { NameLessThan = "H" }).Should().Equal("Club Classics", "Discovery");
    }

    [Fact]
    public void A_page_of_albums_counts_them_all()
    {
        var page = OfflineQueries.Albums(_index, new AlbumQuery { StartIndex = 1, Limit = 1 });

        page.Items.Select(a => a.Name).Should().Equal("Discovery");
        page.TotalCount.Should().Be(3);
        page.StartIndex.Should().Be(1);
    }

    [Fact]
    public void Artists_are_the_album_artists_or_everyone_on_the_songs()
    {
        Artists(new ArtistQuery()).Should().Equal(new[] { "Daft Punk", "Various Artists" }, "Homework's song has no album artists: its artist stands in");
        Artists(new ArtistQuery { AlbumArtistsOnly = false }).Should().Equal("Daft Punk", "Donna Summer", "Romanthony", "Various Artists");
        Artists(new ArtistQuery { AlbumArtistsOnly = false, SortBy = ArtistSortField.PlayCount, Descending = true }).Should().StartWith(["Daft Punk", "Romanthony"]);
        Artists(new ArtistQuery { NameLessThan = "E" }).Should().Equal("Daft Punk");
    }

    [Fact]
    public void A_favourite_artist_comes_as_saved()
    {
        _index.Artists =
        [
            new DownloadedArtist { Artist = new Artist { Id = "ar2", Name = "Romanthony", PrimaryImageTag = "tag", IsFavorite = true }, TrackIds = ["s1"] },
            new DownloadedArtist { Artist = new Artist { Id = "ar4", Name = "Donna Summer" }, TrackIds = ["s4"] },
        ];

        var artists = OfflineQueries.Artists(_index, new ArtistQuery { AlbumArtistsOnly = false, FavouritesOnly = true }).Items;

        artists.Should().ContainSingle("Donna Summer was chosen to download, not made a favourite (#387)").Which.PrimaryImageTag.Should().Be("tag");
    }

    [Fact]
    public void Songs_sort_and_filter_as_on_the_server()
    {
        Songs(new TrackQuery()).Should().Equal("Aerodynamic", "Bad Girls", "Da Funk", "One More Time");
        Songs(new TrackQuery { SortBy = TrackSortField.Album }).Should().Equal("Bad Girls", "One More Time", "Aerodynamic", "Da Funk");
        Songs(new TrackQuery { SortBy = TrackSortField.PlayCount, Descending = true }).Should().StartWith(["One More Time", "Aerodynamic"]);
        Songs(new TrackQuery { SortBy = TrackSortField.Duration, Descending = true }).Should().StartWith(["One More Time", "Aerodynamic"]);
        Songs(new TrackQuery { SortBy = TrackSortField.DatePlayed, Descending = true }).Should().StartWith(["One More Time", "Aerodynamic"]);
        Songs(new TrackQuery { FavouritesOnly = true }).Should().Equal("Da Funk");
        Songs(new TrackQuery { GenreIds = ["g1"] }).Should().Equal(new[] { "Aerodynamic", "Da Funk", "One More Time" }, "Da Funk has its album's genre");
        Songs(new TrackQuery { MinYear = 2000 }).Should().Equal("Aerodynamic", "One More Time");
        Songs(new TrackQuery { NameLessThan = "C" }).Should().Equal("Aerodynamic", "Bad Girls");
    }

    [Fact]
    public void Genres_come_from_the_songs_and_albums()
    {
        OfflineQueries.Genres(_index).Should().Equal(Disco, House);
        OfflineQueries.GenreTracks(_index, "g1", 2)!.Select(t => t.Name).Should().Equal("One More Time", "Aerodynamic");
        OfflineQueries.GenreTracks(_index, "other", 10).Should().BeNull();
    }

    [Fact]
    public void Search_finds_downloaded_music_with_a_typo()
    {
        _index.Playlists = [new DownloadedPlaylist { Playlist = new Playlist { Id = "p1", Name = "Friday night" }, TrackIds = ["s4"] }];

        var results = OfflineQueries.Search(_index, "daft pnuk", 10);

        results.Artists.Select(a => a.Name).Should().Equal("Daft Punk");
        results.Albums.Select(a => a.Name).Should().Equal("Discovery", "Homework");
        OfflineQueries.Search(_index, "one more", 10).Tracks.Select(t => t.Name).Should().Equal("One More Time");
        OfflineQueries.Search(_index, "friday", 10).Playlists.Select(p => p.Name).Should().Equal("Friday night");
        OfflineQueries.Search(_index, "disco", 10).Genres.Should().Equal(Disco);
        OfflineQueries.Search(_index, " ", 10).Count.Should().Be(0);
    }
}
