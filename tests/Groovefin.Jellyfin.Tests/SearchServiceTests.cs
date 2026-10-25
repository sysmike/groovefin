using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class SearchServiceTests : IDisposable
{
    private const string LibraryId = "aaaabbbbccccddddeeeeffff00001111";

    private readonly JellyfinTestHost _host = new();

    public SearchServiceTests()
    {
        _host.ConnectAsTestUser();
        Stub("/Artists", new { Items = new object[] { new { Id = "11111111-0000-0000-0000-000000000001", Name = "Daft Punk", Type = "MusicArtist" } } });
        Stub("/Artists/AlbumArtists", new { Items = Array.Empty<object>() });
        StubItems("MusicAlbum", new { Id = "22222222-0000-0000-0000-000000000002", Name = "Discovery", Type = "MusicAlbum", AlbumArtist = "Daft Punk" });
        StubItems("Audio", new { Id = "33333333-0000-0000-0000-000000000003", Name = "Digital Love", Type = "Audio", Album = "Discovery" });
        StubItems("Playlist", new { Id = "44444444-0000-0000-0000-000000000004", Name = "Disco nights", Type = "Playlist", ChildCount = 12, RunTimeTicks = 36_000_000_000L });
    }

    public void Dispose() => _host.Dispose();

    private ISearchService Sut => _host.Services.GetRequiredService<ISearchService>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Stub(string path, object body, int status = 200) =>
        _host.Server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBodyAsJson(body));

    private void StubItems(string kind, object item, int status = 200) =>
        _host.Server
            .Given(Request.Create().WithPath("/Items").WithParam("includeItemTypes", kind).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBodyAsJson(new { Items = new[] { item } }));

    private Dictionary<string, string> QueryOf(string path, string? kind = null) =>
        _host.Server.LogEntries
            .Select(e => e.RequestMessage!)
            .Last(r => r.Path == path && (kind is null || r.Query!["includeItemTypes"].Contains(kind)))
            .Query!
            .ToDictionary(q => q.Key, q => string.Join(",", q.Value.ToArray()), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Finds_artists_albums_tracks_and_playlists()
    {
        var results = await Sut.SearchAsync("dis", 5, Ct);

        results.Artists.Should().ContainSingle().Which.Name.Should().Be("Daft Punk");
        results.Albums.Should().ContainSingle().Which.Name.Should().Be("Discovery");
        results.Tracks.Should().ContainSingle().Which.AlbumName.Should().Be("Discovery");
        var playlist = results.Playlists.Should().ContainSingle().Subject;
        playlist.Name.Should().Be("Disco nights");
        playlist.TrackCount.Should().Be(12);
        playlist.Duration.Should().Be(TimeSpan.FromHours(1));
        results.Count.Should().Be(4);
    }

    [Fact]
    public async Task Every_request_carries_the_trimmed_term_and_the_limit()
    {
        await Sut.SearchAsync("  dis ", 5, Ct);

        foreach (var query in new[] { QueryOf("/Artists"), QueryOf("/Artists/AlbumArtists"), QueryOf("/Items", "MusicAlbum"), QueryOf("/Items", "Audio"), QueryOf("/Items", "Playlist") })
        {
            query["searchTerm"].Should().Be("dis");
            query["limit"].Should().Be("5");
            query.Should().ContainKey("userId");
        }

        QueryOf("/Items", "Playlist")["mediaTypes"].Should().Be("Audio");
        QueryOf("/Items", "Audio")["recursive"].Should().Be("true");
    }

    [Fact]
    public async Task The_selected_library_limits_everything_but_playlists()
    {
        _host.Client.Connect(new ServerConnection(_host.ServerUrl, JellyfinTestHost.UserId, JellyfinTestHost.Token) { LibraryId = LibraryId });

        await Sut.SearchAsync("dis", 5, Ct);

        foreach (var query in new[] { QueryOf("/Artists"), QueryOf("/Artists/AlbumArtists"), QueryOf("/Items", "MusicAlbum"), QueryOf("/Items", "Audio") })
        {
            Guid.Parse(query["parentId"]).ToString("N").Should().Be(LibraryId);
        }

        QueryOf("/Items", "Playlist").Should().NotContainKey("parentId");
    }

    [Fact]
    public async Task Album_artists_are_found_too_each_artist_once_by_name()
    {
        // /Artists returns only the tracks' artists; an album artist like "Various Artists" comes from
        // /Artists/AlbumArtists alone (#243).
        Stub("/Artists", new
        {
            Items = new object[]
            {
                new { Id = "11111111-0000-0000-0000-000000000001", Name = "Daft Punk", Type = "MusicArtist" },
                new { Id = "11111111-0000-0000-0000-000000000003", Name = "Todd Edwards", Type = "MusicArtist" },
            },
        });
        Stub("/Artists/AlbumArtists", new
        {
            Items = new object[]
            {
                new { Id = "11111111-0000-0000-0000-000000000001", Name = "Daft Punk", Type = "MusicArtist" },
                new { Id = "11111111-0000-0000-0000-000000000002", Name = "Various Artists", Type = "MusicArtist" },
            },
        });

        var results = await Sut.SearchAsync("a", 5, Ct);

        results.Artists.Select(a => a.Name).Should().Equal("Daft Punk", "Todd Edwards", "Various Artists");
        QueryOf("/Artists")["sortBy"].Should().Be("SortName");
        QueryOf("/Artists/AlbumArtists")["sortBy"].Should().Be("SortName");
    }

    [Fact]
    public async Task Merged_artists_are_cut_to_the_limit()
    {
        Stub("/Artists", new { Items = new object[] { new { Id = "11111111-0000-0000-0000-000000000001", Name = "Air", Type = "MusicArtist" } } });
        Stub("/Artists/AlbumArtists", new { Items = new object[] { new { Id = "11111111-0000-0000-0000-000000000002", Name = "Beck", Type = "MusicArtist" } } });

        var results = await Sut.SearchAsync("a", 1, Ct);

        results.Artists.Should().ContainSingle().Which.Name.Should().Be("Air");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_term_asks_nothing(string query)
    {
        var before = _host.Server.LogEntries.Count();

        var results = await Sut.SearchAsync(query, 5, Ct);

        results.Should().BeSameAs(SearchResults.Empty);
        _host.Server.LogEntries.Count().Should().Be(before);
    }

    [Fact]
    public async Task A_failing_request_fails_the_search()
    {
        StubItems("Audio", new { }, status: 500);

        var act = () => Sut.SearchAsync("dis", 5, Ct);

        await act.Should().ThrowAsync<JellyfinException>();
    }

    [Fact]
    public async Task A_limit_below_one_is_rejected()
    {
        var act = () => Sut.SearchAsync("dis", 0, Ct);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
