using Groovefin.Core.Api;
using Groovefin.Core.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class LibraryServiceTests : IDisposable
{
    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static object AlbumsResponse => new
    {
        Items = new object[]
        {
            new
            {
                Id = "11111111-2222-3333-4444-555555555555",
                Name = "Discovery",
                Type = "MusicAlbum",
                AlbumArtist = "Daft Punk",
                AlbumArtists = new[] { new { Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", Name = "Daft Punk" } },
                ProductionYear = 2001,
                RunTimeTicks = 36_000_000_000L,
                ImageTags = new { Primary = "tag-1" },
                UserData = new { IsFavorite = true, PlayCount = 7 },
                DateCreated = "2024-05-01T10:00:00.0000000Z",
            },
            new { Id = "99999999-2222-3333-4444-555555555555", Name = "No Cover", Type = "MusicAlbum" },
        },
        TotalRecordCount = 250,
        StartIndex = 100,
    };

    private void StubItems(object body, int status = 200) =>
        _host.Server
            .Given(Request.Create().WithPath("/Items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json; charset=utf-8").WithBodyAsJson(body));

    private Dictionary<string, string> LastQuery() =>
        _host.Server.LogEntries.Last().RequestMessage!.Query!
            .ToDictionary(q => q.Key, q => string.Join(",", q.Value.ToArray()), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task GetAlbums_requires_a_connection()
    {
        var act = () => _host.Library.GetAlbumsAsync(new AlbumQuery(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAlbums_maps_items_and_paging()
    {
        StubItems(AlbumsResponse);
        _host.ConnectAsTestUser();

        var page = await _host.Library.GetAlbumsAsync(new AlbumQuery { StartIndex = 100, Limit = 2 }, TestContext.Current.CancellationToken);

        page.TotalCount.Should().Be(250);
        page.StartIndex.Should().Be(100);
        page.HasMore.Should().BeTrue();
        page.Items.Should().HaveCount(2);

        var album = page.Items[0];
        album.Id.Should().Be("11111111222233334444555555555555");
        album.Name.Should().Be("Discovery");
        album.AlbumArtist.Should().Be("Daft Punk");
        album.AlbumArtists.Should().ContainSingle().Which.Id.Should().Be("aaaaaaaabbbbccccddddeeeeeeeeeeee");
        album.Year.Should().Be(2001);
        album.Duration.Should().Be(TimeSpan.FromHours(1));
        album.PrimaryImageTag.Should().Be("tag-1");
        album.IsFavorite.Should().BeTrue();
        album.PlayCount.Should().Be(7);
        album.DateAdded.Should().Be(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero));

        var bare = page.Items[1];
        bare.PrimaryImageTag.Should().BeNull();
        bare.AlbumArtists.Should().BeEmpty();
        bare.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task GetAlbums_sends_query_and_token()
    {
        StubItems(AlbumsResponse);
        _host.ConnectAsTestUser();

        await _host.Library.GetAlbumsAsync(new AlbumQuery { StartIndex = 100, Limit = 50 }, TestContext.Current.CancellationToken);

        var query = LastQuery();
        query["includeItemTypes"].Should().Be("MusicAlbum");
        query["recursive"].Should().BeEquivalentTo("true");
        query["startIndex"].Should().Be("100");
        query["limit"].Should().Be("50");
        query["sortBy"].Should().Be("SortName");
        query["sortOrder"].Should().Be("Ascending");
        Guid.Parse(query["userId"]).ToString("N").Should().Be(JellyfinTestHost.UserId);

        var header = _host.Server.LogEntries.Last().RequestMessage!.Headers!["Authorization"].Single();
        header.Should().Contain("Client=\"Groovefin\"").And.Contain($"Token=\"{JellyfinTestHost.Token}\"");
    }

    [Theory]
    [InlineData(AlbumSortField.Year, true, "ProductionYear,SortName", "Descending")]
    [InlineData(AlbumSortField.AlbumArtist, false, "AlbumArtist,SortName", "Ascending")]
    [InlineData(AlbumSortField.DateAdded, true, "DateCreated,SortName", "Descending")]
    [InlineData(AlbumSortField.PlayCount, true, "PlayCount,SortName", "Descending")]
    [InlineData(AlbumSortField.DatePlayed, true, "DatePlayed,SortName", "Descending")]
    [InlineData(AlbumSortField.Random, false, "Random", "Ascending")]
    public async Task GetAlbums_maps_sort_order(AlbumSortField sortBy, bool descending, string expectedSortBy, string expectedOrder)
    {
        StubItems(AlbumsResponse);
        _host.ConnectAsTestUser();

        await _host.Library.GetAlbumsAsync(new AlbumQuery { SortBy = sortBy, Descending = descending }, TestContext.Current.CancellationToken);

        var query = LastQuery();
        query["sortBy"].Should().Be(expectedSortBy);
        query["sortOrder"].Should().Be(expectedOrder);
    }

    [Fact]
    public async Task GetAlbums_unauthorized_is_reported()
    {
        StubItems(new { }, status: 401);
        _host.ConnectAsTestUser();

        var act = () => _host.Library.GetAlbumsAsync(new AlbumQuery(), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.IsUnauthorized.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    public async Task GetAlbums_rejects_invalid_paging(int startIndex, int limit)
    {
        _host.ConnectAsTestUser();

        var act = () => _host.Library.GetAlbumsAsync(new AlbumQuery { StartIndex = startIndex, Limit = limit }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetAlbums_sends_genre_year_and_favourite_filters()
    {
        StubItems(AlbumsResponse);
        _host.ConnectAsTestUser();

        await _host.Library.GetAlbumsAsync(
            new AlbumQuery
            {
                GenreIds = ["aaaaaaaabbbbccccddddeeeeeeeeeeee"],
                MinYear = 1999,
                MaxYear = 2001,
                FavouritesOnly = true,
            },
            TestContext.Current.CancellationToken);

        var query = LastQuery();
        Guid.Parse(query["genreIds"]).ToString("N").Should().Be("aaaaaaaabbbbccccddddeeeeeeeeeeee");
        query["years"].Should().Be("1999,2000,2001");
        query["filters"].Should().Be("IsFavorite");
    }

    [Fact]
    public async Task GetAlbums_combines_favourite_and_played_filters()
    {
        StubItems(AlbumsResponse);
        _host.ConnectAsTestUser();

        await _host.Library.GetAlbumsAsync(new AlbumQuery { FavouritesOnly = true, PlayedOnly = true }, TestContext.Current.CancellationToken);

        LastQuery()["filters"].Should().Be("IsFavorite,IsPlayed");
    }

    [Fact]
    public async Task GetAlbums_without_filters_sends_none()
    {
        StubItems(AlbumsResponse);
        _host.ConnectAsTestUser();

        await _host.Library.GetAlbumsAsync(new AlbumQuery(), TestContext.Current.CancellationToken);

        LastQuery().Keys.Should().NotContain(["genreIds", "years", "filters"]);
    }

    [Fact]
    public void Open_ended_year_range_is_bounded()
    {
        var years = LibraryService.Years(new AlbumQuery { MaxYear = 1905 });

        years.Should().Equal(1900, 1901, 1902, 1903, 1904, 1905);
        LibraryService.Years(new AlbumQuery()).Should().BeNull();
    }

    [Fact]
    public async Task GetAlbums_rejects_an_inverted_year_range()
    {
        _host.ConnectAsTestUser();

        var act = () => _host.Library.GetAlbumsAsync(new AlbumQuery { MinYear = 2000, MaxYear = 1990 }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAlbumGenres_maps_genres_for_albums()
    {
        _host.Server.Given(Request.Create().WithPath("/Genres").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(new
            {
                Items = new object[]
                {
                    new { Id = "11111111-2222-3333-4444-555555555555", Name = "Electronic" },
                    new { Id = "99999999-2222-3333-4444-555555555555", Name = "Jazz" },
                    new { Name = "No id" },
                },
            }));
        _host.ConnectAsTestUser();

        var genres = await _host.Library.GetAlbumGenresAsync(TestContext.Current.CancellationToken);

        genres.Select(g => g.Name).Should().Equal("Electronic", "Jazz");
        genres[0].Id.Should().Be("11111111222233334444555555555555");
        LastQuery()["includeItemTypes"].Should().Be("MusicAlbum");
    }

    private const string AlbumId = "11111111222233334444555555555555";

    [Fact]
    public async Task GetAlbum_maps_the_album()
    {
        _host.Server.Given(Request.Create().WithPath($"/Items/{Guid.Parse(AlbumId)}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(new
            {
                Id = Guid.Parse(AlbumId),
                Name = "Discovery",
                AlbumArtist = "Daft Punk",
                ProductionYear = 2001,
                Type = "MusicAlbum",
            }));
        _host.ConnectAsTestUser();

        var album = await _host.Library.GetAlbumAsync(AlbumId, TestContext.Current.CancellationToken);

        album.Name.Should().Be("Discovery");
        album.Year.Should().Be(2001);
        Guid.Parse(LastQuery()["userId"]).ToString("N").Should().Be(JellyfinTestHost.UserId);
    }

    [Fact]
    public async Task GetAlbum_maps_genres_and_MusicBrainz_ids()
    {
        _host.Server.Given(Request.Create().WithPath($"/Items/{Guid.Parse(AlbumId)}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(new
            {
                Id = Guid.Parse(AlbumId),
                Name = "Discovery",
                Type = "MusicAlbum",
                GenreItems = new[] { new { Name = "Electronic", Id = Guid.Parse("11111111-2222-3333-4444-555555555555") } },
                // Real MusicBrainz IDs are UUIDs, which the SDK reads as GUIDs (#194); "AllMusic" is plain text.
                ProviderIds = new
                {
                    MusicBrainzAlbum = "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11",
                    MusicBrainzReleaseGroup = "48117b82-8a1e-3aaf-9f8c-1a1f4a2e8b6e",
                    AllMusic = "mw0000041153",
                },
            }));
        _host.ConnectAsTestUser();

        var album = await _host.Library.GetAlbumAsync(AlbumId, TestContext.Current.CancellationToken);

        album.Genres.Should().Equal(new Genre("11111111222233334444555555555555", "Electronic"));
        album.MusicBrainzReleaseId.Should().Be("8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11");
        album.MusicBrainzReleaseGroupId.Should().Be("48117b82-8a1e-3aaf-9f8c-1a1f4a2e8b6e");
    }

    [Fact]
    public async Task GetAlbum_that_does_not_exist_is_a_404()
    {
        _host.Server.Given(Request.Create().WithPath($"/Items/{Guid.Parse(AlbumId)}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        _host.ConnectAsTestUser();

        var act = () => _host.Library.GetAlbumAsync(AlbumId, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAlbum_rejects_a_malformed_id()
    {
        _host.ConnectAsTestUser();

        var act = () => _host.Library.GetAlbumAsync("not-an-id", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAlbumTracks_queries_by_parent_in_disc_order_and_maps_tracks()
    {
        StubItems(new
        {
            Items = new object[]
            {
                new
                {
                    Id = "aaaaaaaa-0000-0000-0000-000000000001",
                    Name = "One More Time",
                    Type = "Audio",
                    AlbumId = Guid.Parse(AlbumId),
                    Album = "Discovery",
                    ArtistItems = new[] { new { Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", Name = "Daft Punk" } },
                    RunTimeTicks = 3_200_000_000L,
                    IndexNumber = 1,
                    ParentIndexNumber = 2,
                    UserData = new { IsFavorite = true, PlayCount = 3 },
                },
            },
        });
        _host.ConnectAsTestUser();

        var tracks = await _host.Library.GetAlbumTracksAsync(AlbumId, TestContext.Current.CancellationToken);

        var query = LastQuery();
        Guid.Parse(query["parentId"]).ToString("N").Should().Be(AlbumId);
        query["includeItemTypes"].Should().Be("Audio");
        query["sortBy"].Should().Be("ParentIndexNumber,IndexNumber,SortName");
        tracks.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = "aaaaaaaa000000000000000000000001",
            Name = "One More Time",
            AlbumId,
            AlbumName = "Discovery",
            Duration = TimeSpan.FromSeconds(320),
            TrackNumber = 1,
            DiscNumber = 2,
            IsFavorite = true,
            PlayCount = 3,
        });
        tracks[0].Artists.Should().ContainSingle().Which.Name.Should().Be("Daft Punk");
    }

    [Fact]
    public async Task GetTracks_pages_through_all_audio_items_with_the_sort_order()
    {
        StubItems(new
        {
            Items = new object[]
            {
                new
                {
                    Id = "aaaaaaaa-0000-0000-0000-000000000001",
                    Name = "One More Time",
                    Type = "Audio",
                    AlbumArtist = "Daft Punk",
                    ProductionYear = 2001,
                    DateCreated = "2024-05-01T10:00:00.0000000Z",
                },
            },
            TotalRecordCount = 5000,
            StartIndex = 400,
        });
        _host.ConnectAsTestUser();

        var page = await _host.Library.GetTracksAsync(
            new TrackQuery { StartIndex = 400, Limit = 200, SortBy = TrackSortField.Album, Descending = true },
            TestContext.Current.CancellationToken);

        page.TotalCount.Should().Be(5000);
        page.StartIndex.Should().Be(400);
        var track = page.Items.Should().ContainSingle().Subject;
        track.AlbumArtist.Should().Be("Daft Punk");
        track.Year.Should().Be(2001);
        track.DateAdded.Should().Be(new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero));
        var query = LastQuery();
        query["includeItemTypes"].Should().Be("Audio");
        query["recursive"].Should().Be("true");
        query["startIndex"].Should().Be("400");
        query["limit"].Should().Be("200");
        query["sortBy"].Should().Be("Album,ParentIndexNumber,IndexNumber,SortName");
        query["sortOrder"].Should().Be("Descending");
        query["fields"].Should().Be("DateCreated");
        query["enableTotalRecordCount"].Should().Be("true");
    }

    [Theory]
    [InlineData(TrackSortField.Name, "SortName")]
    [InlineData(TrackSortField.Artist, "Artist,Album,ParentIndexNumber,IndexNumber,SortName")]
    [InlineData(TrackSortField.Duration, "Runtime,SortName")]
    [InlineData(TrackSortField.DateAdded, "DateCreated,SortName")]
    [InlineData(TrackSortField.Random, "Random")]
    [InlineData(TrackSortField.DatePlayed, "DatePlayed,SortName")]
    public async Task GetTracks_maps_each_sort_order(TrackSortField sortBy, string expected)
    {
        StubItems(new { Items = Array.Empty<object>(), TotalRecordCount = 0 });
        _host.ConnectAsTestUser();

        await _host.Library.GetTracksAsync(new TrackQuery { SortBy = sortBy }, TestContext.Current.CancellationToken);

        var query = LastQuery();
        query["sortBy"].Should().Be(expected);
        query["sortOrder"].Should().Be("Ascending");
    }

    [Fact]
    public async Task GetTracksById_asks_for_those_ids_with_the_last_played_date()
    {
        StubItems(new
        {
            Items = new object[]
            {
                new
                {
                    Id = "aaaaaaaa-0000-0000-0000-000000000001",
                    Name = "One More Time",
                    Type = "Audio",
                    UserData = new { PlayCount = 7, LastPlayedDate = "2026-10-20T18:30:00.0000000Z" },
                },
            },
        });
        _host.ConnectAsTestUser();

        var tracks = await _host.Library.GetTracksByIdAsync(
            ["aaaaaaaa000000000000000000000001", "aaaaaaaa000000000000000000000002"], TestContext.Current.CancellationToken);

        var track = tracks.Should().ContainSingle().Subject;
        track.PlayCount.Should().Be(7);
        track.LastPlayed.Should().Be(new DateTimeOffset(2026, 10, 20, 18, 30, 0, TimeSpan.Zero));
        var query = LastQuery();
        query["ids"].Split(',').Select(id => Guid.Parse(id).ToString("N")).Should().Equal(
            "aaaaaaaa000000000000000000000001", "aaaaaaaa000000000000000000000002");
        query["includeItemTypes"].Should().Be("Audio");
        query["enableUserData"].Should().Be("true");
    }

    [Fact]
    public async Task GetTracksById_without_ids_asks_nothing()
    {
        _host.ConnectAsTestUser();
        var before = _host.Server.LogEntries.Count();

        var tracks = await _host.Library.GetTracksByIdAsync([], TestContext.Current.CancellationToken);

        tracks.Should().BeEmpty();
        _host.Server.LogEntries.Count().Should().Be(before);
    }

    [Theory]
    [InlineData(true, "POST")]
    [InlineData(false, "DELETE")]
    public async Task SetFavourite_posts_or_deletes_the_favourite(bool favourite, string method)
    {
        _host.Server.Given(Request.Create().WithPath($"/UserFavoriteItems/{Guid.Parse(AlbumId)}").UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(new { IsFavorite = favourite }));
        _host.ConnectAsTestUser();

        await _host.Library.SetFavouriteAsync(AlbumId, favourite, TestContext.Current.CancellationToken);

        var request = _host.Server.LogEntries.Single().RequestMessage!;
        request.Method.Should().Be(method);
        Guid.Parse(LastQuery()["userId"]).ToString("N").Should().Be(JellyfinTestHost.UserId);
    }

    [Fact]
    public async Task SetFavourite_failure_becomes_JellyfinException()
    {
        _host.Server.Given(Request.Create().WithPath($"/UserFavoriteItems/{Guid.Parse(AlbumId)}").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(403));
        _host.ConnectAsTestUser();

        var act = () => _host.Library.SetFavouriteAsync(AlbumId, true, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<JellyfinException>();
    }

    [Fact]
    public async Task GetTrackDetails_maps_tags_people_provider_ids_and_the_audio_stream()
    {
        const string trackId = "22222222333344445555666666666666";
        _host.Server.Given(Request.Create().WithPath($"/Items/{Guid.Parse(trackId)}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(new
            {
                Id = Guid.Parse(trackId),
                Name = "Digital Love",
                Type = "Audio",
                Album = "Discovery",
                AlbumArtists = new[] { new { Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), Name = "Daft Punk" } },
                Genres = new[] { "Electronic" },
                PremiereDate = "2001-03-12T00:00:00.0000000Z",
                People = new object[]
                {
                    new { Name = "Thomas Bangalter", Type = "Composer" },
                    new { Name = "Daft Punk", Type = "Artist" },
                    new { Name = "Romanthony", Type = "Lyricist" },
                    new { Name = "Someone", Type = "Creator" },
                },
                Studios = new[] { new { Name = "Virgin", Id = Guid.Parse("11111111-2222-3333-4444-555555555555") } },
                Tags = new[] { "Summer" },
                ProviderIds = new { MusicBrainzTrack = "f2a1b9e3-0000-4000-8000-000000000001", AudioDbAlbum = "2109614", Empty = "" },
                HasLyrics = true,
                NormalizationGain = -6.2,
                UserData = new { IsFavorite = true, PlayCount = 7 },
                MediaSources = new[]
                {
                    new
                    {
                        Container = "flac",
                        Size = 38_502_400L,
                        Bitrate = 1_100_000,
                        Path = "/music/Daft Punk/Discovery/03 Digital Love.flac",
                        MediaStreams = new object[]
                        {
                            new { Type = "EmbeddedImage", Codec = "mjpeg" },
                            new { Type = "Audio", Codec = "flac", BitRate = 1_031_499, SampleRate = 44_100, BitDepth = 16, Channels = 2 },
                        },
                    },
                },
            }));
        _host.ConnectAsTestUser();

        var details = await _host.Library.GetTrackDetailsAsync(trackId, TestContext.Current.CancellationToken);

        details.Track.Name.Should().Be("Digital Love");
        details.Track.PlayCount.Should().Be(7);
        details.Track.NormalizationGain.Should().BeApproximately(-6.2, 0.001);
        details.AlbumArtists.Should().Equal("Daft Punk");
        details.Genres.Should().Equal("Electronic");
        details.ReleaseDate.Should().Be(new DateOnly(2001, 3, 12));
        details.People.Should().Equal(
            new TrackPerson("Thomas Bangalter", PersonRole.Composer),
            new TrackPerson("Romanthony", PersonRole.Lyricist),
            new TrackPerson("Someone", PersonRole.Other));
        details.Studios.Should().Equal("Virgin");
        details.Tags.Should().Equal("Summer");
        details.ProviderIds.Should().Equal(
            KeyValuePair.Create("AudioDbAlbum", "2109614"),
            KeyValuePair.Create("MusicBrainzTrack", "f2a1b9e3-0000-4000-8000-000000000001"));
        details.HasLyrics.Should().BeTrue();
        details.Container.Should().Be("flac");
        details.Codec.Should().Be("flac");
        details.Bitrate.Should().Be(1_031_499, "the audio stream's bitrate is the song's own; the source's includes the cover");
        details.SampleRate.Should().Be(44_100);
        details.BitDepth.Should().Be(16);
        details.Channels.Should().Be(2);
        details.Size.Should().Be(38_502_400);
        details.Path.Should().Be("/music/Daft Punk/Discovery/03 Digital Love.flac");
        Guid.Parse(LastQuery()["userId"]).ToString("N").Should().Be(JellyfinTestHost.UserId);
    }

    [Fact]
    public async Task GetTrackDetails_of_a_bare_item_leaves_the_details_empty()
    {
        const string trackId = "22222222333344445555666666666666";
        _host.Server.Given(Request.Create().WithPath($"/Items/{Guid.Parse(trackId)}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(new
            {
                Id = Guid.Parse(trackId),
                Name = "Untitled",
                Type = "Audio",
            }));
        _host.ConnectAsTestUser();

        var details = await _host.Library.GetTrackDetailsAsync(trackId, TestContext.Current.CancellationToken);

        details.Track.Name.Should().Be("Untitled");
        details.People.Should().BeEmpty();
        details.ProviderIds.Should().BeEmpty();
        details.ReleaseDate.Should().BeNull();
        details.Codec.Should().BeNull();
        details.Size.Should().BeNull();
        details.HasLyrics.Should().BeNull();
    }
}
