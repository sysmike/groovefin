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

        foreach (var query in new[] { QueryOf("/Artists"), QueryOf("/Items", "MusicAlbum"), QueryOf("/Items", "Audio"), QueryOf("/Items", "Playlist") })
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

        foreach (var query in new[] { QueryOf("/Artists"), QueryOf("/Items", "MusicAlbum"), QueryOf("/Items", "Audio") })
        {
            Guid.Parse(query["parentId"]).ToString("N").Should().Be(LibraryId);
        }

        QueryOf("/Items", "Playlist").Should().NotContainKey("parentId");
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
