using Groovefin.Core.Api;
using Groovefin.Core.Servers;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ReturnsExtensions;

namespace Groovefin.Core.Tests.Servers;

/// <summary>At home or away (#453): the server says so, its address only until then.</summary>
public sealed class HomeNetworkTests
{
    private static readonly Uri Public = new("https://jellyfin.example.com/");
    private static readonly Uri Lan = new("http://192.168.1.10:8096/");

    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly IServerNetwork _server = Substitute.For<IServerNetwork>();
    private readonly FakeNetworkStatus _network = new();
    private readonly FakeTimeProvider _time = new();

    public HomeNetworkTests() => _client.Connection.Returns(new ServerConnection(Public, "user", "token"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HomeNetwork CreateSut() => new(_client, _server, _network, NullLogger<HomeNetwork>.Instance, _time);

    [Fact]
    public async Task A_public_address_is_home_when_the_server_says_so()
    {
        _server.IsInServerNetworkAsync(Arg.Any<CancellationToken>()).Returns(true);

        using var sut = CreateSut();
        await sut.Checking;

        sut.IsAtHome(Public).Should().BeTrue();
    }

    [Fact]
    public async Task A_lan_address_is_away_when_the_server_says_so()
    {
        _client.Connection.Returns(new ServerConnection(Lan, "user", "token"));
        _server.IsInServerNetworkAsync(Arg.Any<CancellationToken>()).Returns(false);

        using var sut = CreateSut();
        await sut.Checking;

        sut.IsAtHome(Lan).Should().BeFalse("the server knows its own networks");
    }

    [Fact]
    public async Task Without_an_answer_the_address_decides()
    {
        _server.IsInServerNetworkAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<bool?>(null),
            _ => Task.FromException<bool?>(new JellyfinException("Not found")));

        using var sut = CreateSut();
        await sut.Checking;
        sut.IsAtHome(Public).Should().BeFalse("an older server doesn't say");
        sut.IsAtHome(Lan).Should().BeTrue();

        _network.IsOnline = true;
        _time.Advance(HomeNetwork.SettleDelay);
        await sut.Checking;
        sut.IsAtHome(Public).Should().BeFalse("the server could not be asked");
    }

    [Fact]
    public async Task A_network_change_forgets_the_answer_and_asks_again_once_the_network_settled()
    {
        _server.IsInServerNetworkAsync(Arg.Any<CancellationToken>()).Returns(true, false);
        using var sut = CreateSut();
        await sut.Checking;
        sut.IsAtHome(Public).Should().BeTrue();

        _network.IsOnline = true;

        sut.IsAtHome(Public).Should().BeFalse("until the server answers again, the address decides");
        await _server.Received(1).IsInServerNetworkAsync(Arg.Any<CancellationToken>());

        _time.Advance(HomeNetwork.SettleDelay);
        await sut.Checking;

        await _server.Received(2).IsInServerNetworkAsync(Arg.Any<CancellationToken>());
        sut.IsAtHome(Public).Should().BeFalse("the PC left home");
    }

    [Fact]
    public async Task Quick_changes_ask_once()
    {
        _server.IsInServerNetworkAsync(Arg.Any<CancellationToken>()).Returns(true);
        using var sut = CreateSut();
        await sut.Checking;

        _network.IsOnline = true;
        _time.Advance(TimeSpan.FromSeconds(1));
        _network.IsOnline = true;
        _time.Advance(HomeNetwork.SettleDelay);
        await sut.Checking;

        await _server.Received(2).IsInServerNetworkAsync(Arg.Any<CancellationToken>());
        sut.IsAtHome(Public).Should().BeTrue();
    }

    [Fact]
    public async Task The_answer_belongs_to_its_server()
    {
        _server.IsInServerNetworkAsync(Arg.Any<CancellationToken>()).Returns(true);
        using var sut = CreateSut();
        await sut.Checking;

        sut.IsAtHome(new Uri("https://other.example.com/")).Should().BeFalse();

        _client.Connection.Returns(new ServerConnection(new Uri("https://other.example.com/"), "user", "token"));
        _client.ConnectionChanged += Raise.Event();

        sut.IsAtHome(Public).Should().BeFalse("the answer was the old server's");
    }

    [Fact]
    public async Task Offline_or_signed_out_the_server_is_not_asked()
    {
        _network.IsOnline = false;
        using (var offline = CreateSut())
        {
            await offline.Checking;
        }

        _network.IsOnline = true;
        _client.Connection.ReturnsNull();
        using (var signedOut = CreateSut())
        {
            await signedOut.Checking;
        }

        await _server.DidNotReceiveWithAnyArgs().IsInServerNetworkAsync(Ct);
    }

    [Fact]
    public async Task Disposed_it_no_longer_listens()
    {
        _server.IsInServerNetworkAsync(Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut();
        await sut.Checking;

        sut.Dispose();
        _network.IsOnline = true;
        _time.Advance(HomeNetwork.SettleDelay);

        await _server.Received(1).IsInServerNetworkAsync(Arg.Any<CancellationToken>());
    }
}
