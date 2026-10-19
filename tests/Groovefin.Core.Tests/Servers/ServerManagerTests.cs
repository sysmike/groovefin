using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Security;
using Groovefin.Core.Servers;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.Servers;

public sealed class ServerManagerTests
{
    private readonly InMemorySettingsService _settings = new();
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ICredentialStore _credentials = Substitute.For<ICredentialStore>();
    private readonly ServerManager _sut;

    public ServerManagerTests()
    {
        _sut = new ServerManager(_settings, _client, _credentials, NullLogger<ServerManager>.Instance);
        StubServer("http://alpha:8096/", "id-a", "Alpha");
        StubServer("http://beta:8096/", "id-b", "Beta");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void StubServer(string address, string id, string name) =>
        _client.GetServerInfoAsync(new Uri(address), Arg.Any<CancellationToken>())
            .Returns(new ServerInfo(id, name, "10.11.0", new Uri(address)));

    private void SignIn(string serverId, string userId)
    {
        _settings.Update(s => s with
        {
            Servers = s.Servers with
            {
                Known = s.Servers.Known.Select(p => p.Id == serverId ? p with { UserId = userId } : p).ToList(),
            },
        });
        _credentials.GetAccessToken(serverId, userId).Returns("token-" + serverId);
    }

    [Fact]
    public async Task First_server_is_added_validated_and_active()
    {
        var raised = 0;
        _sut.Changed += (_, _) => raised++;

        var profile = await _sut.AddAsync("alpha:8096", Ct);

        profile.Should().BeEquivalentTo(new { Id = "id-a", Name = "Alpha", Address = new Uri("http://alpha:8096/"), Version = "10.11.0" });
        _sut.Servers.Should().ContainSingle();
        _sut.ActiveServer!.Id.Should().Be("id-a");
        raised.Should().Be(1);
    }

    [Fact]
    public async Task Second_server_does_not_change_the_active_one()
    {
        await _sut.AddAsync("alpha:8096", Ct);

        await _sut.AddAsync("beta:8096", Ct);

        _sut.Servers.Select(s => s.Name).Should().Equal("Alpha", "Beta");
        _sut.ActiveServer!.Id.Should().Be("id-a");
    }

    [Fact]
    public async Task Adding_the_same_server_under_another_address_updates_it()
    {
        await _sut.AddAsync("alpha:8096", Ct);
        StubServer("https://alpha.example.com/", "id-a", "Alpha (renamed)");

        await _sut.AddAsync("https://alpha.example.com", Ct);

        _sut.Servers.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Name = "Alpha (renamed)",
            Address = new Uri("https://alpha.example.com/"),
        });
    }

    [Fact]
    public async Task Invalid_address_is_rejected_without_contacting_a_server()
    {
        var act = () => _sut.AddAsync("my server", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
        await _client.DidNotReceiveWithAnyArgs().GetServerInfoAsync(default!, Ct);
    }

    [Fact]
    public async Task Unreachable_server_is_not_added()
    {
        _client.GetServerInfoAsync(new Uri("http://gamma:8096/"), Arg.Any<CancellationToken>())
            .Returns<ServerInfo>(_ => throw new JellyfinException("down", statusCode: null));

        var act = () => _sut.AddAsync("gamma:8096", Ct);

        await act.Should().ThrowAsync<JellyfinException>();
        _sut.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAddress_requires_the_same_server_id()
    {
        await _sut.AddAsync("alpha:8096", Ct);

        var act = () => _sut.UpdateAddressAsync("id-a", "beta:8096", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _sut.Servers.Single().Address.Should().Be(new Uri("http://alpha:8096/"));
    }

    [Fact]
    public async Task UpdateAddress_reconnects_the_active_server()
    {
        await _sut.AddAsync("alpha:8096", Ct);
        SignIn("id-a", "user-1");
        StubServer("https://alpha.example.com/", "id-a", "Alpha");

        await _sut.UpdateAddressAsync("id-a", "https://alpha.example.com", Ct);

        _client.Received().Connect(new ServerConnection(new Uri("https://alpha.example.com/"), "user-1", "token-id-a"));
    }

    [Fact]
    public async Task Remove_deletes_tokens_and_activates_the_next_server()
    {
        await _sut.AddAsync("alpha:8096", Ct);
        await _sut.AddAsync("beta:8096", Ct);
        SignIn("id-b", "user-2");

        _sut.Remove("id-a");

        _credentials.Received(1).RemoveServer("id-a");
        _sut.Servers.Should().ContainSingle().Which.Id.Should().Be("id-b");
        _sut.ActiveServer!.Id.Should().Be("id-b");
        _client.Received().Connect(new ServerConnection(new Uri("http://beta:8096/"), "user-2", "token-id-b"));
    }

    [Fact]
    public async Task Removing_the_last_server_disconnects()
    {
        await _sut.AddAsync("alpha:8096", Ct);

        _sut.Remove("id-a");

        _sut.Servers.Should().BeEmpty();
        _sut.ActiveServer.Should().BeNull();
        _client.Received().Disconnect();
    }

    [Fact]
    public async Task SetActive_connects_when_a_token_is_stored()
    {
        await _sut.AddAsync("alpha:8096", Ct);
        await _sut.AddAsync("beta:8096", Ct);
        SignIn("id-b", "user-2");

        _sut.SetActive("id-b");

        _sut.ActiveServer!.Id.Should().Be("id-b");
        _client.Received(1).Connect(new ServerConnection(new Uri("http://beta:8096/"), "user-2", "token-id-b"));
    }

    [Fact]
    public async Task SetActive_without_login_disconnects()
    {
        await _sut.AddAsync("alpha:8096", Ct);

        _sut.SetActive("id-a");

        _client.Received(1).Disconnect();
        _client.DidNotReceiveWithAnyArgs().Connect(default!);
    }

    [Fact]
    public async Task Restore_connects_the_active_server()
    {
        await _sut.AddAsync("alpha:8096", Ct);
        SignIn("id-a", "user-1");

        _sut.Restore();

        _client.Received(1).Connect(new ServerConnection(new Uri("http://alpha:8096/"), "user-1", "token-id-a"));
    }

    [Fact]
    public void Unknown_server_ids_are_rejected_or_ignored()
    {
        var act = () => _sut.SetActive("nope");

        act.Should().Throw<InvalidOperationException>();
        _sut.Invoking(s => s.Remove("nope")).Should().NotThrow();
    }
}
