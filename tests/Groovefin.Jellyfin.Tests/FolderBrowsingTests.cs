using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

/// <summary>Folder browsing: one level at a time, and all tracks below a folder.</summary>
public sealed class FolderBrowsingTests : IDisposable
{
    private const string LibraryId = "cccccccc111122223333444444444444";
    private const string FolderId = "dddddddd111122223333444444444444";

    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Stub(string path, object body) =>
        _host.Server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(body));

    private Dictionary<string, string> LastQuery() =>
        _host.Server.LogEntries.Last().RequestMessage!.Query!
            .ToDictionary(q => q.Key, q => string.Join(",", q.Value.ToArray()), StringComparer.OrdinalIgnoreCase);

    private static object FolderResponse => new
    {
        Items = new object[]
        {
            new { Id = "dddddddd-1111-2222-3333-444444444444", Name = "Daft Punk", Type = "Folder", IsFolder = true },
            new { Id = "eeeeeeee-1111-2222-3333-444444444444", Name = "Discovery", Type = "MusicAlbum", IsFolder = true },
            new { Id = "ffffffff-1111-2222-3333-444444444444", Name = "Intro", Type = "Audio", IsFolder = false },
            new { Id = "abababab-1111-2222-3333-444444444444", Name = "cover.jpg", Type = "Photo", IsFolder = false },
        },
    };

    [Fact]
    public async Task Without_a_selected_library_the_top_lists_the_music_libraries()
    {
        _host.ConnectAsTestUser();
        Stub("/UserViews", new { Items = new object[] { new { Id = "cccccccc-1111-2222-3333-444444444444", Name = "Music", CollectionType = "music" } } });

        var content = await _host.Library.GetFolderAsync(null, Ct);

        content.Folders.Should().Equal(new Core.Models.Folder(LibraryId, "Music"));
        content.Tracks.Should().BeEmpty();
    }

    [Fact]
    public async Task With_a_selected_library_the_top_is_its_folder()
    {
        _host.ConnectAsTestUser();
        _host.Client.Connect(_host.Client.Connection! with { LibraryId = LibraryId });
        Stub("/Items", FolderResponse);

        var content = await _host.Library.GetFolderAsync(null, Ct);

        Guid.Parse(LastQuery()["parentId"]).ToString("N").Should().Be(LibraryId);
        content.Folders.Select(f => f.Name).Should().Equal("Daft Punk", "Discovery");
    }

    [Fact]
    public async Task A_folder_lists_subfolders_and_tracks_one_level_deep()
    {
        _host.ConnectAsTestUser();
        Stub("/Items", FolderResponse);

        var content = await _host.Library.GetFolderAsync(FolderId, Ct);

        content.Folders.Select(f => f.Id).Should().Equal(FolderId, "eeeeeeee111122223333444444444444");
        content.Tracks.Should().ContainSingle().Which.Name.Should().Be("Intro");
        var query = LastQuery();
        Guid.Parse(query["parentId"]).ToString("N").Should().Be(FolderId);
        query.Should().NotContainKey("recursive");
        query["sortBy"].Should().Be("SortName");
    }

    [Fact]
    public async Task Folder_tracks_include_every_subfolder()
    {
        _host.ConnectAsTestUser();
        Stub("/Items", new { Items = new object[] { new { Id = "ffffffff-1111-2222-3333-444444444444", Name = "Intro", Type = "Audio" } } });

        var tracks = await _host.Library.GetFolderTracksAsync(FolderId, 500, Ct);

        tracks.Should().ContainSingle();
        var query = LastQuery();
        Guid.Parse(query["parentId"]).ToString("N").Should().Be(FolderId);
        query["recursive"].Should().Be("true");
        query["includeItemTypes"].Should().Be("Audio");
        query["limit"].Should().Be("500");
    }

    [Fact]
    public async Task A_malformed_folder_id_is_rejected()
    {
        _host.ConnectAsTestUser();

        var act = () => _host.Library.GetFolderAsync("not-an-id", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
