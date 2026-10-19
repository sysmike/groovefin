using System.Net;
using System.Net.Sockets;
using Groovefin.Core.Api;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Groovefin.Jellyfin.Tests;

public sealed class JellyfinClientTests : IDisposable
{
    private readonly JellyfinTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private void StubPublicInfo(object body, int status = 200) =>
        _host.Server
            .Given(Request.Create().WithPath("/System/Info/Public").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json; charset=utf-8").WithBodyAsJson(body));

    [Fact]
    public async Task GetServerInfo_maps_public_system_info()
    {
        StubPublicInfo(new { Id = "f00d", ServerName = "Media Box", Version = "10.11.2", ProductName = "Jellyfin Server" });

        var info = await _host.Client.GetServerInfoAsync(_host.ServerUrl, TestContext.Current.CancellationToken);

        info.Id.Should().Be("f00d");
        info.Name.Should().Be("Media Box");
        info.Version.Should().Be("10.11.2");
        info.Address.Should().Be(_host.ServerUrl);
    }

    [Fact]
    public async Task GetServerInfo_sends_client_identification_without_token()
    {
        StubPublicInfo(new { Id = "f00d", ServerName = "Media Box", Version = "10.11.2" });

        await _host.Client.GetServerInfoAsync(_host.ServerUrl, TestContext.Current.CancellationToken);

        var header = _host.Server.LogEntries.Single().RequestMessage!.Headers!["Authorization"].Single();
        header.Should().Contain("Client=\"Groovefin\"").And.Contain("DeviceId=\"device-1\"").And.NotContain("Token=");
    }

    [Fact]
    public async Task GetServerInfo_without_server_id_is_rejected()
    {
        StubPublicInfo(new { ServerName = "Not Jellyfin" });

        var act = () => _host.Client.GetServerInfoAsync(_host.ServerUrl, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<JellyfinException>();
    }

    [Fact]
    public async Task GetServerInfo_http_error_becomes_JellyfinException_with_status()
    {
        StubPublicInfo(new { }, status: 404);

        var act = () => _host.Client.GetServerInfoAsync(_host.ServerUrl, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetServerInfo_unreachable_server_becomes_JellyfinException_without_status()
    {
        var unusedPort = GetUnusedPort();

        var act = () => _host.Client.GetServerInfoAsync(new Uri($"http://127.0.0.1:{unusedPort}"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<JellyfinException>()).Which.StatusCode.Should().BeNull();
    }

    [Fact]
    public void Connect_and_Disconnect_update_the_connection_and_raise_ConnectionChanged()
    {
        var raised = 0;
        _host.Client.ConnectionChanged += (_, _) => raised++;

        _host.ConnectAsTestUser();
        _host.Client.Connection!.UserId.Should().Be(JellyfinTestHost.UserId);

        _host.Client.Disconnect();
        _host.Client.Connection.Should().BeNull();

        _host.Client.Disconnect();
        raised.Should().Be(2);
    }

    [Fact]
    public void ServerConnection_ToString_does_not_contain_the_token()
    {
        var connection = new ServerConnection(_host.ServerUrl, JellyfinTestHost.UserId, JellyfinTestHost.Token);

        connection.ToString().Should().NotContain(JellyfinTestHost.Token);
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
