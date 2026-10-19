using Groovefin.Core.Api;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

/// <summary>Artist lists, artist detail, top tracks, similar artists and the artist album filters.</summary>
public sealed class ArtistLibraryTests : IDisposable
{
    private const string ArtistId = "aaaaaaaabbbbccccddddeeeeeeeeeeee";

    private readonly JellyfinTestHost _host = new();

    public ArtistLibraryTests() => _host.ConnectAsTestUser();

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object ArtistsResponse => new
    {
        Items = new object[]
        {
            new
            {
                Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                Name = "Daft Punk",
                Type = "MusicArtist",
                ImageTags = new { Primary = "img-1" },
                UserData = new { IsFavorite = true },
            },
            new { Id = "bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee", Name = "Justice", Type = "MusicArtist" },
        },
        TotalRecordCount = 42,
        StartIndex = 0,
    };

    private void Stub(string path, object body, int status = 200) =>
        _host.Server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBodyAsJson(body));

    private Dictionary<string, string> LastQuery() =>
        _host.Server.LogEntries.Last().RequestMessage!.Query!
            .ToDictionary(q => q.Key, q => string.Join(",", q.Value.ToArray()), StringComparer.OrdinalIgnoreCase);

    private string LastPath() => _host.Server.LogEntries.Last().RequestMessage!.Path;

    [Fact]
    public async Task Album_artists_are_listed_from_the_album_artists_endpoint()
    {
        Stub("/Artists/AlbumArtists", ArtistsResponse);

        var page = await _host.Library.GetArtistsAsync(new ArtistQuery { StartIndex = 0, Limit = 50 }, Ct);

        LastPath().Should().Be("/Artists/AlbumArtists");
        page.TotalCount.Should().Be(42);
        page.Items.Should().HaveCount(2);
        page.Items[0].Should().BeEquivalentTo(new { Id = ArtistId, Name = "Daft Punk", PrimaryImageTag = "img-1", IsFavorite = true });
        page.Items[1].PrimaryImageTag.Should().BeNull();
        var query = LastQuery();
        query["limit"].Should().Be("50");
        query["sortBy"].Should().Be("SortName");
        query["sortOrder"].Should().Be("Ascending");
        query.Should().NotContainKey("isFavorite");
    }

    [Fact]
    public async Task All_artists_use_the_artists_endpoint_with_sort_and_favourite_filter()
    {
        Stub("/Artists", ArtistsResponse);

        await _host.Library.GetArtistsAsync(
            new ArtistQuery { AlbumArtistsOnly = false, SortBy = ArtistSortField.PlayCount, Descending = true, FavouritesOnly = true },
            Ct);

        LastPath().Should().Be("/Artists");
        var query = LastQuery();
        query["sortBy"].Should().Be("PlayCount,SortName");
        query["sortOrder"].Should().Be("Descending");
        query["isFavorite"].Should().BeEquivalentTo("true");
    }

    [Theory]
    [InlineData(ArtistSortField.DateAdded, "DateCreated,SortName")]
    [InlineData(ArtistSortField.Random, "Random")]
    public async Task Artist_sort_fields_are_mapped(ArtistSortField sortBy, string expected)
    {
        Stub("/Artists/AlbumArtists", ArtistsResponse);

        await _host.Library.GetArtistsAsync(new ArtistQuery { SortBy = sortBy }, Ct);

        LastQuery()["sortBy"].Should().Be(expected);
    }

    [Fact]
    public async Task Artist_detail_maps_biography_and_genres()
    {
        Stub($"/Items/{Guid.Parse(ArtistId)}", new
        {
            Id = ArtistId,
            Name = "Daft Punk",
            Type = "MusicArtist",
            Overview = "French duo.<br/>Formed in 1993 &amp; split in 2021.",
            Genres = new[] { "Electronic", "House" },
            UserData = new { IsFavorite = false },
        });

        var artist = await _host.Library.GetArtistAsync(ArtistId, Ct);

        artist.Name.Should().Be("Daft Punk");
        artist.Biography.Should().Be("French duo.\nFormed in 1993 & split in 2021.");
        artist.Genres.Should().Equal("Electronic", "House");
        artist.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task Artist_detail_rejects_a_malformed_id()
    {
        var act = () => _host.Library.GetArtistAsync("not-an-id", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Top_tracks_are_the_most_played_audio_items_of_the_artist()
    {
        Stub("/Items", new
        {
            Items = new object[]
            {
                new { Id = "12121212-2222-3333-4444-555555555555", Name = "One More Time", Type = "Audio", Album = "Discovery", RunTimeTicks = 3_200_000_000L },
            },
        });

        var tracks = await _host.Library.GetArtistTopTracksAsync(ArtistId, 10, Ct);

        tracks.Should().ContainSingle().Which.AlbumName.Should().Be("Discovery");
        var query = LastQuery();
        Guid.Parse(query["artistIds"]).ToString("N").Should().Be(ArtistId);
        query["includeItemTypes"].Should().Be("Audio");
        query["sortBy"].Should().Be("PlayCount,SortName");
        query["sortOrder"].Should().Be("Descending");
        query["limit"].Should().Be("10");
    }

    [Fact]
    public async Task Similar_artists_use_the_similar_items_endpoint()
    {
        Stub($"/Items/{Guid.Parse(ArtistId)}/Similar", ArtistsResponse);

        var similar = await _host.Library.GetSimilarArtistsAsync(ArtistId, 12, Ct);

        similar.Select(a => a.Name).Should().Equal("Daft Punk", "Justice");
        LastQuery()["limit"].Should().Be("12");
    }

    [Fact]
    public async Task Albums_can_be_filtered_by_album_artist_or_contributing_artist()
    {
        Stub("/Items", new { Items = Array.Empty<object>(), TotalRecordCount = 0 });

        await _host.Library.GetAlbumsAsync(new AlbumQuery { AlbumArtistId = ArtistId }, Ct);
        var byArtist = LastQuery();
        await _host.Library.GetAlbumsAsync(new AlbumQuery { ContributingArtistId = ArtistId }, Ct);
        var appearsOn = LastQuery();

        Guid.Parse(byArtist["albumArtistIds"]).ToString("N").Should().Be(ArtistId);
        byArtist.Should().NotContainKey("contributingArtistIds");
        Guid.Parse(appearsOn["contributingArtistIds"]).ToString("N").Should().Be(ArtistId);
        appearsOn.Should().NotContainKey("albumArtistIds");
    }
}
