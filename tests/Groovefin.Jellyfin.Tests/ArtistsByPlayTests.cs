using Groovefin.Core.Api;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

/// <summary>
/// Artists by how often their songs were played (#374): Jellyfin counts plays on songs, so the library
/// service adds them up per artist itself.
/// </summary>
public sealed class ArtistsByPlayTests : IDisposable
{
    private const string ArtistA = "aaaaaaaa000000000000000000000000";
    private const string ArtistB = "bbbbbbbb000000000000000000000000";
    private const string ArtistC = "cccccccc000000000000000000000000";
    private const string ArtistD = "dddddddd000000000000000000000000";

    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static object Ref(string id) => new { Id = Guid.ParseExact(id, "N"), Name = id[..1].ToUpperInvariant() };

    private static object Artist(string id) => new { Id = Guid.ParseExact(id, "N"), Name = id[..1].ToUpperInvariant(), Type = "MusicArtist" };

    /// <summary>A song by <paramref name="artists"/> on an album by <paramref name="albumArtist"/>.</summary>
    private static object Song(int plays, string albumArtist, params string[] artists) => new
    {
        Id = Guid.NewGuid(),
        Name = $"Song {plays}",
        Type = "Audio",
        AlbumArtists = new[] { Ref(albumArtist) },
        ArtistItems = artists.Select(Ref).ToArray(),
        UserData = new { PlayCount = plays, Played = true },
    };

    private void Stub(IRequestBuilder request, object body) =>
        _host.Server.Given(request.UsingGet())
            .RespondWith(Response.Create().WithHeader("Content-Type", "application/json; charset=utf-8").WithBodyAsJson(body));

    /// <summary>
    /// Album artists: A 2 + 1 = 3 plays, B 4; C has none. Track artists: D 2 + 4 = 6 (a guest on B's song),
    /// A 2 + 1 = 3, B 4. The servers list the artists by name.
    /// </summary>
    private void StubLibrary()
    {
        Stub(Request.Create().WithPath("/Items").WithParam("includeItemTypes", "Audio"),
            new { Items = new[] { Song(4, ArtistB, ArtistB, ArtistD), Song(2, ArtistA, ArtistA, ArtistD), Song(1, ArtistA, ArtistA) } });
        var all = new[] { Artist(ArtistA), Artist(ArtistB), Artist(ArtistC), Artist(ArtistD) };
        Stub(Request.Create().WithPath("/Items").WithParam("ids"), new { Items = all });
        Stub(Request.Create().WithPath("/Artists/AlbumArtists"), new { Items = all.Take(3).ToArray() });
        Stub(Request.Create().WithPath("/Artists"), new { Items = all });
    }

    private List<Dictionary<string, string>> Queries(string path) =>
    [
        .. _host.Server.LogEntries
            .Where(e => e.RequestMessage!.Path == path)
            .Select(e => e.RequestMessage!.Query!
                .ToDictionary(q => q.Key, q => string.Join(",", q.Value.ToArray()), StringComparer.OrdinalIgnoreCase)),
    ];

    [Fact]
    public async Task Album_artists_by_the_plays_of_their_albums_songs()
    {
        StubLibrary();
        _host.ConnectAsTestUser();

        var most = await _host.Library.GetArtistsAsync(
            new ArtistQuery { SortBy = ArtistSortField.PlayCount, Descending = true }, TestContext.Current.CancellationToken);
        var least = await _host.Library.GetArtistsAsync(new ArtistQuery { SortBy = ArtistSortField.PlayCount }, TestContext.Current.CancellationToken);

        most.Items.Select(a => a.Name).Should().Equal("B", "A", "C");
        most.TotalCount.Should().Be(3);
        least.Items.Select(a => a.Name).Should().Equal("C", "A", "B");

        // The songs most played first, with their plays; the artists listed light, by name.
        var songs = Queries("/Items").First(q => q.GetValueOrDefault("includeItemTypes") == "Audio");
        songs["sortBy"].Should().Be("PlayCount");
        songs["filters"].Should().Be("IsPlayed");
        songs["enableUserData"].Should().BeEquivalentTo("true");
        Queries("/Artists/AlbumArtists").Should().OnlyContain(q =>
            q["sortBy"] == "SortName" && q["sortOrder"] == "Ascending" && !q.ContainsKey("limit") && q["enableImages"] == "false");
    }

    [Fact]
    public async Task All_artists_by_the_plays_of_the_songs_they_are_on()
    {
        StubLibrary();
        _host.ConnectAsTestUser();

        var page = await _host.Library.GetArtistsAsync(
            new ArtistQuery { AlbumArtistsOnly = false, SortBy = ArtistSortField.PlayCount, Descending = true, FavouritesOnly = true },
            TestContext.Current.CancellationToken);

        page.Items.Select(a => a.Name).Should().Equal("D", "B", "A", "C");
        Queries("/Artists").Single()["isFavorite"].Should().BeEquivalentTo("true");
    }

    [Fact]
    public async Task The_next_page_keeps_the_order_of_the_first()
    {
        StubLibrary();
        _host.ConnectAsTestUser();
        var query = new ArtistQuery { SortBy = ArtistSortField.PlayCount, Descending = true, Limit = 2 };
        await _host.Library.GetArtistsAsync(query, TestContext.Current.CancellationToken);
        var requests = _host.Server.LogEntries.Count();

        var next = await _host.Library.GetArtistsAsync(query with { StartIndex = 2 }, TestContext.Current.CancellationToken);

        next.Items.Select(a => a.Name).Should().Equal("C");
        _host.Server.LogEntries.Count().Should().Be(requests + 1, "only the page's artists are loaded");
    }
}
