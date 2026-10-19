using Groovefin.Core.Api;
using Groovefin.Core.Lyrics;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class LyricsServiceTests : IDisposable
{
    private const string TrackId = "aaaaaaaa000000000000000000000001";

    private readonly JellyfinTestHost _host = new();

    public LyricsServiceTests() => _host.ConnectAsTestUser();

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ILyricsService Sut => _host.Services.GetRequiredService<ILyricsService>();

    private void Stub(object? body, int status = 200)
    {
        var response = Response.Create().WithStatusCode(status);
        if (body is not null)
        {
            response = response.WithHeader("Content-Type", "application/json").WithBodyAsJson(body);
        }

        _host.Server.Given(Request.Create().WithPath($"/Audio/{Guid.ParseExact(TrackId, "N")}/Lyrics").UsingGet()).RespondWith(response);
    }

    [Fact]
    public async Task Timed_lines_from_the_server_are_synced_lyrics()
    {
        Stub(new
        {
            Metadata = new { Artist = "Daft Punk", Title = "Digital Love", IsSynced = true, Offset = 0L },
            Lyrics = new object[]
            {
                new { Text = "Last night", Start = 120_000_000L },
                new { Text = "I had a dream", Start = 150_000_000L },
            },
        });

        var lyrics = await Sut.GetLyricsAsync(TrackId, Ct);

        lyrics!.IsSynced.Should().BeTrue();
        lyrics.Lines.Should().Equal(
            new LyricLine(TimeSpan.FromSeconds(12), "Last night"),
            new LyricLine(TimeSpan.FromSeconds(15), "I had a dream"));
        lyrics.Metadata.Artist.Should().Be("Daft Punk");
        lyrics.Metadata.Title.Should().Be("Digital Love");
    }

    [Fact]
    public async Task Untimed_lines_are_unsynced_lyrics()
    {
        Stub(new { Metadata = new { IsSynced = false }, Lyrics = new object[] { new { Text = "Verse one" }, new { Text = "" }, new { Text = "Verse two" } } });

        var lyrics = await Sut.GetLyricsAsync(TrackId, Ct);

        lyrics!.IsSynced.Should().BeFalse();
        lyrics.Lines.Select(l => l.Text).Should().Equal("Verse one", string.Empty, "Verse two");
    }

    [Fact]
    public async Task Unparsed_LRC_text_is_parsed_here()
    {
        Stub(new { Metadata = new { Album = "Discovery" }, Lyrics = new object[] { new { Text = "[ar:Daft Punk]\n[00:01.00]One\n[00:02.50]Two" } } });

        var lyrics = await Sut.GetLyricsAsync(TrackId, Ct);

        lyrics!.IsSynced.Should().BeTrue();
        lyrics.Lines.Should().Equal(new LyricLine(TimeSpan.FromSeconds(1), "One"), new LyricLine(TimeSpan.FromSeconds(2.5), "Two"));
        lyrics.Metadata.Artist.Should().Be("Daft Punk");
        lyrics.Metadata.Album.Should().Be("Discovery");
    }

    [Fact]
    public async Task A_track_without_lyrics_gives_null()
    {
        Stub(null, status: 404);

        (await Sut.GetLyricsAsync(TrackId, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Empty_lyrics_give_null()
    {
        Stub(new { Metadata = new { }, Lyrics = Array.Empty<object>() });

        (await Sut.GetLyricsAsync(TrackId, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Other_failures_are_reported()
    {
        Stub(null, status: 403);

        var act = () => Sut.GetLyricsAsync(TrackId, Ct);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_malformed_track_id_is_rejected()
    {
        var act = () => Sut.GetLyricsAsync("nope", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
