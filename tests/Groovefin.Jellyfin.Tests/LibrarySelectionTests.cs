using Groovefin.Core.Api;
using Groovefin.Core.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

/// <summary>Music library listing and the library limit on browse queries.</summary>
public sealed class LibrarySelectionTests : IDisposable
{
    private const string LibraryId = "cccccccc111122223333444444444444";

    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Stub(string path, object body) =>
        _host.Server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(body));

    private static object Empty => new { Items = Array.Empty<object>(), TotalRecordCount = 0 };

    private Dictionary<string, string> LastQuery() =>
        _host.Server.LogEntries.Last().RequestMessage!.Query!
            .ToDictionary(q => q.Key, q => string.Join(",", q.Value.ToArray()), StringComparer.OrdinalIgnoreCase);

    private void ConnectWithLibrary(string? libraryId)
    {
        _host.ConnectAsTestUser();
        var connection = _host.Client.Connection!;
        _host.Client.Connect(connection with { LibraryId = libraryId });
    }

    [Fact]
    public async Task Only_music_libraries_are_listed()
    {
        _host.ConnectAsTestUser();
        Stub("/UserViews", new
        {
            Items = new object[]
            {
                new { Id = "cccccccc-1111-2222-3333-444444444444", Name = "Music", CollectionType = "music" },
                new { Id = "dddddddd-1111-2222-3333-444444444444", Name = "Movies", CollectionType = "movies" },
                new { Id = "eeeeeeee-1111-2222-3333-444444444444", Name = "Live Recordings", CollectionType = "music" },
            },
        });

        var libraries = await _host.Library.GetMusicLibrariesAsync(Ct);

        libraries.Should().Equal(new MusicLibrary(LibraryId, "Music"), new MusicLibrary("eeeeeeee111122223333444444444444", "Live Recordings"));
        Guid.Parse(LastQuery()["userId"]).ToString("N").Should().Be(JellyfinTestHost.UserId);
    }

    [Fact]
    public async Task Browse_queries_are_limited_to_the_selected_library()
    {
        ConnectWithLibrary(LibraryId);
        Stub("/Items", Empty);
        Stub("/Genres", Empty);
        Stub("/Artists/AlbumArtists", Empty);

        await _host.Library.GetAlbumsAsync(new AlbumQuery(), Ct);
        var albums = LastQuery();
        await _host.Library.GetAlbumGenresAsync(Ct);
        var genres = LastQuery();
        await _host.Library.GetArtistsAsync(new ArtistQuery(), Ct);
        var artists = LastQuery();

        foreach (var query in new[] { albums, genres, artists })
        {
            Guid.Parse(query["parentId"]).ToString("N").Should().Be(LibraryId);
        }
    }

    [Fact]
    public async Task All_libraries_send_no_parent()
    {
        ConnectWithLibrary(null);
        Stub("/Items", Empty);

        await _host.Library.GetAlbumsAsync(new AlbumQuery(), Ct);

        LastQuery().Should().NotContainKey("parentId");
    }

    [Fact]
    public async Task Album_tracks_keep_the_album_as_parent()
    {
        ConnectWithLibrary(LibraryId);
        Stub("/Items", Empty);

        await _host.Library.GetAlbumTracksAsync("11111111222233334444555555555555", Ct);

        Guid.Parse(LastQuery()["parentId"]).ToString("N").Should().Be("11111111222233334444555555555555");
    }
}
