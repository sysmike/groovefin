using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class PlaybackReporterTests : IDisposable
{
    private const string TrackId = "11112222333344445555666677778888";

    private static readonly PlaybackReport Report = new(TrackId, "session-1", TimeSpan.FromSeconds(42.5), IsPaused: true, Volume: 80, IsMuted: false);

    private readonly JellyfinTestHost _host = new();

    public PlaybackReporterTests()
    {
        _host.ConnectAsTestUser();
        foreach (var path in new[] { "/Sessions/Playing", "/Sessions/Playing/Progress", "/Sessions/Playing/Stopped" })
        {
            _host.Server.Given(Request.Create().WithPath(path).UsingPost()).RespondWith(Response.Create().WithStatusCode(204));
        }
    }

    public void Dispose() => _host.Dispose();

    private IPlaybackReporter Sut => _host.Services.GetRequiredService<IPlaybackReporter>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (string Path, string Body, string Authorization) LastRequest()
    {
        var request = _host.Server.LogEntries.Last().RequestMessage!;
        return (request.Path, request.Body!, request.Headers!["Authorization"].Single());
    }

    [Fact]
    public async Task Start_posts_item_session_position_and_state_with_the_token()
    {
        await Sut.ReportStartAsync(Report, Ct);

        var (path, body, authorization) = LastRequest();
        path.Should().Be("/Sessions/Playing");
        body.Should().Contain("\"ItemId\":\"11112222-3333-4444-5555-666677778888\"")
            .And.Contain("\"PlaySessionId\":\"session-1\"")
            .And.Contain("\"PositionTicks\":425000000")
            .And.Contain("\"IsPaused\":true")
            .And.Contain("\"VolumeLevel\":80")
            .And.Contain("\"CanSeek\":true");
        authorization.Should().Contain($"Token=\"{JellyfinTestHost.Token}\"");
    }

    [Fact]
    public async Task Progress_posts_to_the_progress_endpoint()
    {
        await Sut.ReportProgressAsync(Report with { IsPaused = false }, Ct);

        var (path, body, _) = LastRequest();
        path.Should().Be("/Sessions/Playing/Progress");
        body.Should().Contain("\"IsPaused\":false").And.Contain("\"PositionTicks\":425000000");
    }

    [Fact]
    public async Task Stopped_posts_the_final_position()
    {
        await Sut.ReportStoppedAsync(Report, Ct);

        var (path, body, _) = LastRequest();
        path.Should().Be("/Sessions/Playing/Stopped");
        body.Should().Contain("\"PositionTicks\":425000000").And.Contain("\"PlaySessionId\":\"session-1\"");
    }

    [Fact]
    public async Task Server_errors_become_JellyfinException_and_are_not_retried()
    {
        _host.Server.Given(Request.Create().WithPath("/Sessions/Playing/Progress").UsingPost()).RespondWith(Response.Create().WithStatusCode(500));
        var before = _host.Server.LogEntries.Count();

        var act = () => Sut.ReportProgressAsync(Report, Ct);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
        _host.Server.LogEntries.Count().Should().Be(before + 1);
    }

    [Fact]
    public async Task Malformed_ids_and_missing_connections_are_rejected()
    {
        var bad = () => Sut.ReportStartAsync(Report with { ItemId = "nope" }, Ct);
        await bad.Should().ThrowAsync<ArgumentException>();

        _host.Client.Disconnect();
        var offline = () => Sut.ReportStoppedAsync(Report, Ct);
        await offline.Should().ThrowAsync<InvalidOperationException>();
    }
}
