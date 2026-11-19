using Groovefin.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

/// <summary>The server's view of where the client connects from (#453): <c>/System/Endpoint</c>.</summary>
public sealed class ServerNetworkTests : IDisposable
{
    private readonly JellyfinTestHost _host = new();

    public ServerNetworkTests() => _host.ConnectAsTestUser();

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServerNetwork Sut => _host.Services.GetRequiredService<IServerNetwork>();

    private void Stub(object body) =>
        _host.Server.Given(Request.Create().WithPath("/System/Endpoint").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBodyAsJson(body));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public async Task The_server_says_whether_the_client_is_in_its_network(bool isLocal, bool isInNetwork, bool expected)
    {
        Stub(new { IsLocal = isLocal, IsInNetwork = isInNetwork });

        (await Sut.IsInServerNetworkAsync(Ct)).Should().Be(expected);
    }

    [Fact]
    public async Task A_server_that_doesnt_say_gives_no_answer()
    {
        Stub(new { });

        (await Sut.IsInServerNetworkAsync(Ct)).Should().BeNull();
    }

    [Fact]
    public async Task A_server_without_the_endpoint_fails()
    {
        _host.Server.Given(Request.Create().WithPath("/System/Endpoint").UsingGet()).RespondWith(Response.Create().WithStatusCode(404));

        var act = () => Sut.IsInServerNetworkAsync(Ct);

        await act.Should().ThrowAsync<JellyfinException>();
    }
}
