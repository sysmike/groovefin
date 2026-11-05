using Groovefin.Core.Api;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

/// <summary>
/// Albums by when their songs were last played (#369): Jellyfin keeps play dates on songs, so the
/// library service orders the albums by their played songs itself.
/// </summary>
public sealed class AlbumsByPlayTests : IDisposable
{
    private const string AlbumA = "aaaaaaaa000000000000000000000000";
    private const string AlbumB = "bbbbbbbb000000000000000000000000";
    private const string AlbumC = "cccccccc000000000000000000000000";
    private const string AlbumD = "dddddddd000000000000000000000000";

    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static AlbumQuery ByLastPlay(bool descending = true, bool playedOnly = false) =>
        new() { SortBy = AlbumSortField.DatePlayed, Descending = descending, PlayedOnly = playedOnly };

    private static object Album(string id, string name) => new { Id = Guid.ParseExact(id, "N"), Name = name, Type = "MusicAlbum" };

    private static object Song(int number, string albumId, int plays = 1) =>
        new { Id = Guid.NewGuid(), Name = $"Song {number}", Type = "Audio", AlbumId = Guid.ParseExact(albumId, "N"), UserData = new { PlayCount = plays, Played = true } };

    private void Stub(IRequestBuilder request, object body, int priority) =>
        _host.Server.Given(request.UsingGet()).AtPriority(priority)
            .RespondWith(Response.Create().WithHeader("Content-Type", "application/json; charset=utf-8").WithBodyAsJson(body));

    /// <summary>B was played last, then A; C never. The server lists the albums by name.</summary>
    private void StubLibrary()
    {
        Stub(Request.Create().WithPath("/Items").WithParam("includeItemTypes", "Audio"),
            new { Items = new[] { Song(1, AlbumB), Song(2, AlbumA), Song(3, AlbumB) }, TotalRecordCount = 3 }, 1);
        Stub(Request.Create().WithPath("/Items").WithParam("ids"),
            new { Items = new[] { Album(AlbumA, "A"), Album(AlbumB, "B"), Album(AlbumC, "C") }, TotalRecordCount = 3 }, 1);
        Stub(Request.Create().WithPath("/Items").WithParam("includeItemTypes", "MusicAlbum"),
            new { Items = new[] { Album(AlbumA, "A"), Album(AlbumB, "B"), Album(AlbumC, "C") }, TotalRecordCount = 3 }, 5);
    }

    /// <summary>The query of each request for items; every one names the kind of item it asks for.</summary>
    private List<Dictionary<string, string>> Queries() =>
    [
        .. _host.Server.LogEntries
            .Where(e => e.RequestMessage!.Path == "/Items")
            .Select(e => e.RequestMessage!.Query!
                .ToDictionary(q => q.Key, q => string.Join(",", q.Value.ToArray()), StringComparer.OrdinalIgnoreCase)),
    ];

    [Fact]
    public async Task Last_played_first_then_the_never_played_by_name()
    {
        StubLibrary();
        _host.ConnectAsTestUser();

        var page = await _host.Library.GetAlbumsAsync(ByLastPlay(), TestContext.Current.CancellationToken);

        page.Items.Select(a => a.Name).Should().Equal("B", "A", "C");
        page.TotalCount.Should().Be(3);

        // The played songs newest first, without what isn't read.
        var songs = Queries().First(q => q["includeItemTypes"] == "Audio");
        songs["sortBy"].Should().Be("DatePlayed");
        songs["sortOrder"].Should().Be("Descending");
        songs["filters"].Should().Be("IsPlayed");
        songs["enableImages"].Should().BeEquivalentTo("false");
        songs["enableUserData"].Should().BeEquivalentTo("false");

        // No album is asked for by its own play date, nor as "played" (every song played).
        Queries().Where(q => q["includeItemTypes"] == "MusicAlbum")
            .Should().OnlyContain(q => q["sortBy"] != "DatePlayed" && !q.ContainsKey("filters"));
    }

    [Fact]
    public async Task Oldest_first_is_the_reverse()
    {
        StubLibrary();
        _host.ConnectAsTestUser();

        var page = await _host.Library.GetAlbumsAsync(ByLastPlay(descending: false), TestContext.Current.CancellationToken);

        page.Items.Select(a => a.Name).Should().Equal("C", "A", "B");
    }

    [Fact]
    public async Task Played_only_skips_the_list_of_all_albums()
    {
        // The start page's recently played albums: the songs give them, newest first.
        StubLibrary();
        _host.ConnectAsTestUser();

        var page = await _host.Library.GetAlbumsAsync(ByLastPlay(playedOnly: true) with { Limit = 20 }, TestContext.Current.CancellationToken);

        page.Items.Select(a => a.Name).Should().Equal("B", "A");
        page.TotalCount.Should().Be(2);
        Queries().Where(q => q["includeItemTypes"] == "MusicAlbum").Should().OnlyContain(q => q.ContainsKey("ids"));
    }

    [Fact]
    public async Task Filters_apply_to_the_albums_not_the_songs()
    {
        StubLibrary();
        _host.ConnectAsTestUser();

        await _host.Library.GetAlbumsAsync(ByLastPlay() with { FavouritesOnly = true, MinYear = 1999, MaxYear = 2000 }, TestContext.Current.CancellationToken);

        var all = Queries().Single(q => q["includeItemTypes"] == "MusicAlbum" && !q.ContainsKey("ids"));
        all["filters"].Should().Be("IsFavorite");
        all["years"].Should().Be("1999,2000");
        all["enableImages"].Should().BeEquivalentTo("false");
        all.Should().NotContainKey("limit");
        Queries().Single(q => q["includeItemTypes"] == "Audio").Should().NotContainKey("years");
    }

    [Fact]
    public async Task The_next_page_keeps_the_order_of_the_first()
    {
        StubLibrary();
        _host.ConnectAsTestUser();
        await _host.Library.GetAlbumsAsync(ByLastPlay() with { Limit = 2 }, TestContext.Current.CancellationToken);
        var requests = _host.Server.LogEntries.Count();

        var next = await _host.Library.GetAlbumsAsync(ByLastPlay() with { StartIndex = 2, Limit = 2 }, TestContext.Current.CancellationToken);

        next.Items.Select(a => a.Name).Should().Equal("C");
        next.StartIndex.Should().Be(2);
        _host.Server.LogEntries.Count().Should().Be(requests + 1, "only the page's albums are loaded");
    }

    [Fact]
    public async Task Most_played_sums_the_plays_of_each_album()
    {
        // A: 2 + 1 = 3 plays, B: 7, D: 4; C was never played. The server lists the songs most played first.
        Stub(Request.Create().WithPath("/Items").WithParam("includeItemTypes", "Audio"),
            new
            {
                Items = new[] { Song(1, AlbumB, plays: 7), Song(2, AlbumD, plays: 4), Song(3, AlbumA, plays: 2), Song(4, AlbumA, plays: 1) },
                TotalRecordCount = 4,
            },
            1);
        var albums = new[] { Album(AlbumA, "A"), Album(AlbumB, "B"), Album(AlbumC, "C"), Album(AlbumD, "D") };
        Stub(Request.Create().WithPath("/Items").WithParam("ids"), new { Items = albums, TotalRecordCount = 4 }, 1);
        Stub(Request.Create().WithPath("/Items").WithParam("includeItemTypes", "MusicAlbum"), new { Items = albums, TotalRecordCount = 4 }, 5);
        _host.ConnectAsTestUser();

        var most = await _host.Library.GetAlbumsAsync(new AlbumQuery { SortBy = AlbumSortField.PlayCount, Descending = true }, TestContext.Current.CancellationToken);
        var least = await _host.Library.GetAlbumsAsync(new AlbumQuery { SortBy = AlbumSortField.PlayCount }, TestContext.Current.CancellationToken);

        most.Items.Select(a => a.Name).Should().Equal("B", "D", "A", "C");
        least.Items.Select(a => a.Name).Should().Equal("C", "A", "D", "B");

        // The songs most played first, with their plays; no album by its own count.
        var songs = Queries().First(q => q["includeItemTypes"] == "Audio");
        songs["sortBy"].Should().Be("PlayCount");
        songs["filters"].Should().Be("IsPlayed");
        songs["enableUserData"].Should().BeEquivalentTo("true");
        Queries().Where(q => q["includeItemTypes"] == "MusicAlbum").Should().OnlyContain(q => q["sortBy"] != "PlayCount");
    }

    [Fact]
    public void Most_played_breaks_ties_by_name()
    {
        var plays = new Dictionary<string, int> { ["A"] = 2, ["B"] = 5, ["C"] = 2, ["X"] = 9 };

        // X does not match the query; D was never played.
        LibraryService.MostPlayed(["A", "B", "C", "D"], plays).Should().Equal("B", "A", "C");
    }

    [Theory]
    [InlineData(true, false, "B,A,C")]
    [InlineData(false, false, "C,A,B")]
    [InlineData(true, true, "B,A")]
    [InlineData(false, true, "A,B")]
    public void Order_by_last_play(bool descending, bool playedOnly, string expected)
    {
        var order = LibraryService.Order(["A", "B", "C"], ["B", "A", "X"], ByLastPlay(descending, playedOnly));

        // X was played but does not match the query's filters.
        string.Join(',', order).Should().Be(expected);
    }

    [Fact]
    public void Played_only_in_another_order_keeps_that_order()
    {
        var order = LibraryService.Order(["C", "B", "A"], ["A", "C"], new AlbumQuery { SortBy = AlbumSortField.Year, PlayedOnly = true });

        order.Should().Equal("C", "A");
    }
}
