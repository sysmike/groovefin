using System.Net;
using System.Net.Sockets;
using System.Text;
using Groovefin.Core.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Jellyfin.Tests;

public sealed class ServerDiscoveryTests
{
    private const string LivingRoom = """{"Address":"http://192.168.1.10:8096","Id":"0123456789abcdef0123456789abcdef","Name":"Living room","EndpointAddress":null}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DiscoveredServer? Parse(string json) => ServerDiscovery.Parse(Encoding.UTF8.GetBytes(json));

    /// <summary>A fake server on the loopback address: answers each question with <paramref name="answers"/>.</summary>
    private static (UdpClient Server, Task Answering) Answer(params string[] answers)
    {
        var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var answering = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var question = await server.ReceiveAsync(Ct);
                    if (Encoding.UTF8.GetString(question.Buffer) != ServerDiscovery.Question)
                    {
                        continue;
                    }

                    foreach (var answer in answers)
                    {
                        await server.SendAsync(Encoding.UTF8.GetBytes(answer), question.RemoteEndPoint, Ct);
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or OperationCanceledException)
            {
                // The test is over.
            }
        });
        return (server, answering);
    }

    private static ServerDiscovery Sut(params IPEndPoint[] targets) =>
        new(() => targets, TimeSpan.FromMilliseconds(500), NullLogger.Instance);

    [Fact]
    public void An_answer_gives_the_servers_id_name_and_address()
    {
        Parse(LivingRoom).Should().Be(new DiscoveredServer("0123456789abcdef0123456789abcdef", "Living room", new Uri("http://192.168.1.10:8096")));
    }

    [Fact]
    public void An_id_with_dashes_is_read_without_them_and_a_missing_name_is_the_host()
    {
        var server = Parse("""{"Address":"https://music.local:8920","Id":"01234567-89ab-cdef-0123-456789abcdef"}""");

        server.Should().Be(new DiscoveredServer("0123456789abcdef0123456789abcdef", "music.local", new Uri("https://music.local:8920")));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"Address":"http://192.168.1.10:8096"}""")]
    [InlineData("""{"Address":"ftp://192.168.1.10","Id":"x"}""")]
    [InlineData("""{"Address":"192.168.1.10:8096","Id":"x"}""")]
    [InlineData("""{"Address":7,"Id":"x"}""")]
    public void Other_answers_are_ignored(string json) => Parse(json).Should().BeNull();

    [Theory]
    [InlineData("192.168.1.23", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.1.2.3", "255.0.0.0", "10.255.255.255")]
    [InlineData("172.16.5.4", "255.255.240.0", "172.16.15.255")]
    public void The_broadcast_address_has_the_hosts_bits_set(string address, string mask, string broadcast)
    {
        ServerDiscovery.BroadcastAddress(IPAddress.Parse(address), IPAddress.Parse(mask)).Should().Be(IPAddress.Parse(broadcast));
    }

    [Fact]
    public async Task Servers_that_answer_are_found_once()
    {
        var (server, answering) = Answer(LivingRoom, LivingRoom, "garbage");
        using (server)
        {
            var found = await Sut((IPEndPoint)server.Client.LocalEndPoint!).DiscoverAsync(Ct);

            found.Should().ContainSingle().Which.Name.Should().Be("Living room");
        }

        await answering;
    }

    [Fact]
    public async Task Without_a_server_nothing_is_found()
    {
        // A port nobody listens on: Windows reports that datagram back, which must not end the search early or fail it.
        using var unused = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)unused.Client.LocalEndPoint!).Port;
        unused.Close();

        var found = await Sut(new IPEndPoint(IPAddress.Loopback, port)).DiscoverAsync(Ct);

        found.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_stops_the_search()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cancel.CancelAsync();

        var search = () => Sut(new IPEndPoint(IPAddress.Loopback, ServerDiscovery.Port)).DiscoverAsync(cancel.Token);

        await search.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void The_discovery_is_registered()
    {
        using var host = new JellyfinTestHost();

        host.Services.GetRequiredService<IServerDiscovery>().Should().BeOfType<ServerDiscovery>();
    }
}
