using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class PlaylistServiceTests : IDisposable
{
    private readonly JellyfinTestHost _host = new();

    public PlaylistServiceTests() => _host.ConnectAsTestUser();

    public void Dispose() => _host.Dispose();

    private IPlaylistService Sut => _host.Services.GetRequiredService<IPlaylistService>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void RespondWith(object body) =>
        _host.Server.Given(Request.Create().WithPath("/Playlists").UsingPost()).RespondWith(Response.Create().WithHeader("Content-Type", "application/json").WithBodyAsJson(body));

    [Fact]
    public async Task Create_posts_an_audio_playlist_with_the_tracks_in_order()
    {
        RespondWith(new { Id = "99990000aaaabbbbccccddddeeeeffff" });

        var id = await Sut.CreateAsync("  Road trip ", ["11112222333344445555666677778888", "22223333444455556666777788889999"], Ct);

        id.Should().Be("99990000aaaabbbbccccddddeeeeffff");
        var body = _host.Server.LogEntries.Last().RequestMessage!.Body!;
        body.Should().Contain("\"Name\":\"Road trip\"")
            .And.Contain("\"Ids\":[\"11112222-3333-4444-5555-666677778888\",\"22223333-4444-5555-6666-777788889999\"]")
            .And.Contain("\"MediaType\":\"Audio\"")
            .And.Contain("\"UserId\":");
    }

    [Fact]
    public async Task A_missing_id_in_the_answer_is_an_error()
    {
        RespondWith(new { });

        var act = () => Sut.CreateAsync("Mix", [], Ct);

        await act.Should().ThrowAsync<JellyfinException>();
    }

    [Fact]
    public async Task Server_errors_become_JellyfinException()
    {
        _host.Server.Given(Request.Create().WithPath("/Playlists").UsingPost()).RespondWith(Response.Create().WithStatusCode(500));

        var act = () => Sut.CreateAsync("Mix", [], Ct);

        await act.Should().ThrowAsync<JellyfinException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_rejected(string name)
    {
        var act = () => Sut.CreateAsync(name, [], Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Invalid_track_ids_are_rejected()
    {
        var act = () => Sut.CreateAsync("Mix", ["not-an-id"], Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private const string PlaylistId = "99990000aaaabbbbccccddddeeeeffff";

    private const string PlaylistPath = "/Playlists/99990000-aaaa-bbbb-cccc-ddddeeeeffff";

    private void Stub(string path, Func<IRequestBuilder, IRequestBuilder> method, object? body = null, int status = 200)
    {
        var response = Response.Create().WithStatusCode(status);
        if (body is not null)
        {
            response = response.WithHeader("Content-Type", "application/json").WithBodyAsJson(body);
        }

        _host.Server.Given(method(Request.Create().WithPath(path))).RespondWith(response);
    }

    private WireMock.Logging.ILogEntry LastRequest() => _host.Server.LogEntries.Last();

    private Dictionary<string, string> LastQuery() =>
        LastRequest().RequestMessage!.Query!.ToDictionary(q => q.Key, q => string.Join(",", q.Value.ToArray()), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Lists_the_users_audio_playlists_by_name()
    {
        Stub("/Items", r => r.UsingGet(), new { Items = new[] { new { Id = "11110000-0000-0000-0000-000000000001", Name = "Chill", Type = "Playlist", ChildCount = 3 } } });

        var playlists = await Sut.GetPlaylistsAsync(Ct);

        playlists.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = "11110000000000000000000000000001", Name = "Chill", TrackCount = 3 });
        var query = LastQuery();
        query["includeItemTypes"].Should().Be("Playlist");
        query["mediaTypes"].Should().Be("Audio");
        query["sortBy"].Should().Be("SortName");
        query["fields"].Should().Be("ChildCount");
        query.Should().ContainKey("userId");
    }

    [Fact]
    public async Task Gets_one_playlist()
    {
        Stub($"/Items/99990000-aaaa-bbbb-cccc-ddddeeeeffff", r => r.UsingGet(), new { Id = PlaylistId, Name = "Chill", Type = "Playlist", ChildCount = 2, RunTimeTicks = 6_000_000_000L });

        var playlist = await Sut.GetPlaylistAsync(PlaylistId, Ct);

        playlist.Name.Should().Be("Chill");
        playlist.Duration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Entries_come_in_order_with_their_entry_ids()
    {
        Stub($"{PlaylistPath}/Items", r => r.UsingGet(), new
        {
            Items = new object[]
            {
                new { Id = "12121212-0000-0000-0000-000000000001", Name = "One", Type = "Audio", PlaylistItemId = "entry-1" },
                new { Id = "12121212-0000-0000-0000-000000000001", Name = "One", Type = "Audio", PlaylistItemId = "entry-2" },
                new { Id = "12121212-0000-0000-0000-000000000003", Name = "No entry", Type = "Audio" },
            },
        });

        var entries = await Sut.GetEntriesAsync(PlaylistId, Ct);

        entries.Select(e => e.EntryId).Should().Equal("entry-1", "entry-2");
        entries[0].Track.Name.Should().Be("One");
        LastQuery()["enableImageTypes"].Should().Be("Primary");
    }

    [Fact]
    public async Task Rename_posts_the_trimmed_name()
    {
        Stub(PlaylistPath, r => r.UsingPost(), status: 204);

        await Sut.RenameAsync(PlaylistId, "  Calm ", Ct);

        LastRequest().RequestMessage!.Body.Should().Contain("\"Name\":\"Calm\"");
    }

    [Fact]
    public async Task Delete_deletes_the_playlist_item()
    {
        Stub("/Items", r => r.UsingDelete(), status: 204);

        await Sut.DeleteAsync(PlaylistId, Ct);

        LastRequest().RequestMessage!.Method.Should().Be("DELETE");
        LastQuery()["ids"].Should().Be("99990000-aaaa-bbbb-cccc-ddddeeeeffff");
    }

    [Fact]
    public async Task Adding_tracks_posts_their_ids_in_order()
    {
        Stub($"{PlaylistPath}/Items", r => r.UsingPost(), status: 204);

        await Sut.AddTracksAsync(PlaylistId, ["11112222333344445555666677778888", "22223333444455556666777788889999"], Ct);

        var query = LastQuery();
        query["ids"].Should().Be("11112222-3333-4444-5555-666677778888,22223333-4444-5555-6666-777788889999");
        query.Should().ContainKey("userId");
    }

    [Fact]
    public async Task Removing_entries_deletes_them_by_entry_id()
    {
        Stub($"{PlaylistPath}/Items", r => r.UsingDelete(), status: 204);

        await Sut.RemoveEntriesAsync(PlaylistId, ["entry-1", "entry-2"], Ct);

        LastQuery()["entryIds"].Should().Be("entry-1,entry-2");
    }

    [Fact]
    public async Task Moving_an_entry_posts_its_new_index()
    {
        Stub($"{PlaylistPath}/Items/entry-2/Move/0", r => r.UsingPost(), status: 204);

        await Sut.MoveEntryAsync(PlaylistId, "entry-2", 0, Ct);

        LastRequest().RequestMessage!.Path.Should().Be($"{PlaylistPath}/Items/entry-2/Move/0");
    }

    [Fact]
    public async Task Editing_failures_become_JellyfinException()
    {
        Stub($"{PlaylistPath}/Items", r => r.UsingDelete(), status: 500);

        var act = () => Sut.RemoveEntriesAsync(PlaylistId, ["entry-1"], Ct);

        await act.Should().ThrowAsync<JellyfinException>();
    }

    [Fact]
    public async Task Bad_arguments_are_rejected_before_any_request()
    {
        await FluentActions.Awaiting(() => Sut.RenameAsync(PlaylistId, " ", Ct)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Sut.MoveEntryAsync(PlaylistId, "entry-1", -1, Ct)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => Sut.AddTracksAsync("nope", [], Ct)).Should().ThrowAsync<ArgumentException>();
        _host.Server.LogEntries.Should().BeEmpty();
    }
}
