using System.Net;
using Groovefin.Core.Api;
using Microsoft.Extensions.Http.Resilience;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class ResilienceTests
{
    private const string InfoPath = "/System/Info/Public";
    private const string Scenario = "flaky";

    private static object PublicInfo => new { Id = "f00d", ServerName = "Media Box", Version = "10.11.2" };

    private static IResponseBuilder Json(int status, object body) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBodyAsJson(body);

    [Fact]
    public async Task Transient_server_errors_are_retried()
    {
        using var host = new JellyfinTestHost();
        host.Server.Given(Request.Create().WithPath(InfoPath).UsingGet()).InScenario(Scenario)
            .WillSetStateTo("second").RespondWith(Json(503, new { }));
        host.Server.Given(Request.Create().WithPath(InfoPath).UsingGet()).InScenario(Scenario)
            .WhenStateIs("second").WillSetStateTo("third").RespondWith(Json(502, new { }));
        host.Server.Given(Request.Create().WithPath(InfoPath).UsingGet()).InScenario(Scenario)
            .WhenStateIs("third").RespondWith(Json(200, PublicInfo));

        var info = await host.Client.GetServerInfoAsync(host.ServerUrl, TestContext.Current.CancellationToken);

        info.Name.Should().Be("Media Box");
        host.Server.LogEntries.Should().HaveCount(3);
    }

    [Fact]
    public async Task Unauthorized_is_not_retried()
    {
        using var host = new JellyfinTestHost();
        host.Server.Given(Request.Create().WithPath(InfoPath).UsingGet()).RespondWith(Json(401, new { }));

        var act = () => host.Client.GetServerInfoAsync(host.ServerUrl, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.IsUnauthorized.Should().BeTrue();
        host.Server.LogEntries.Should().HaveCount(1);
    }

    [Fact]
    public async Task Persistent_server_errors_give_up_after_three_retries()
    {
        using var host = new JellyfinTestHost();
        host.Server.Given(Request.Create().WithPath(InfoPath).UsingGet()).RespondWith(Json(500, new { }));

        var act = () => host.Client.GetServerInfoAsync(host.ServerUrl, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        host.Server.LogEntries.Should().HaveCount(4);
    }

    [Fact]
    public async Task Slow_responses_time_out_and_become_JellyfinException()
    {
        using var host = new JellyfinTestHost(attemptTimeout: TimeSpan.FromMilliseconds(200));
        host.Server.Given(Request.Create().WithPath(InfoPath).UsingGet())
            .RespondWith(Json(200, PublicInfo).WithDelay(TimeSpan.FromSeconds(2)));

        var act = () => host.Client.GetServerInfoAsync(host.ServerUrl, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_an_error()
    {
        using var host = new JellyfinTestHost();
        host.Server.Given(Request.Create().WithPath(InfoPath).UsingGet())
            .RespondWith(Json(200, PublicInfo).WithDelay(TimeSpan.FromSeconds(2)));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var act = () => host.Client.GetServerInfoAsync(host.ServerUrl, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Defaults_do_not_retry_unsafe_methods_and_bound_the_total_time()
    {
        var options = new HttpStandardResilienceOptions();

        ServiceCollectionExtensions.ConfigureDefaults(options);

        options.Retry.MaxRetryAttempts.Should().Be(3);
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(45));
        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(15));
        options.Retry.ShouldRetryAfterHeader.Should().BeTrue();
    }
}
