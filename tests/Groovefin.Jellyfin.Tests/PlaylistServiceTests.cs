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
}
