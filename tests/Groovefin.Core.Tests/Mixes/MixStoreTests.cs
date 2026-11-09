using Groovefin.Core.Mixes;
using Groovefin.Core.Servers;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Mixes;

public sealed class MixStoreTests : IDisposable
{
    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly InMemorySettingsService _settings = new();
    private readonly MixStore _sut;
    private int _changes;

    public MixStoreTests()
    {
        SignIn("s1", "u1");
        _sut = new MixStore(_settings, _servers);
        _sut.Changed += (_, _) => _changes++;
    }

    public void Dispose() => _sut.Dispose();

    private static Mix Mix(string id, string name) => new() { Id = id, Rules = new MixRules { Name = name } };

    private void SignIn(string serverId, string userId)
    {
        _servers.IsSignedIn.Returns(true);
        _servers.ActiveServer.Returns(new ServerProfile { Id = serverId, Name = serverId, Address = new Uri("https://music.example"), UserId = userId });
    }

    [Fact]
    public void Saved_mixes_are_listed_by_name_and_found_by_id()
    {
        _sut.Save(Mix("m1", "rock"));
        _sut.Save(Mix("m2", "Ambient"));

        _sut.Mixes.Select(m => m.Rules.Name).Should().Equal("Ambient", "rock");
        _sut.Find("m1")!.Rules.Name.Should().Be("rock");
        _sut.Find("nope").Should().BeNull();
        _changes.Should().Be(2);
    }

    [Fact]
    public void Saving_again_replaces_and_delete_removes()
    {
        _sut.Save(Mix("m1", "rock"));

        _sut.Save(Mix("m1", "Rock 90s"));
        _sut.Mixes.Should().ContainSingle().Which.Rules.Name.Should().Be("Rock 90s");

        _sut.Delete("m1");
        _sut.Mixes.Should().BeEmpty();
        _settings.Current.Browse.Mixes.Should().BeEmpty("an owner without mixes is not kept");
    }

    [Fact]
    public void Each_user_on_each_server_has_their_own_mixes()
    {
        _sut.Save(Mix("m1", "Mine"));

        SignIn("s1", "u2");
        _servers.Changed += Raise.Event();

        _sut.Mixes.Should().BeEmpty();
        _changes.Should().Be(2, "the save and the change of user");
        _settings.Current.Browse.Mixes.Keys.Should().Equal("s1/u1");
    }

    [Fact]
    public void Signed_out_there_are_none_and_none_can_be_saved()
    {
        _sut.Save(Mix("m1", "Mine"));
        _servers.IsSignedIn.Returns(false);

        _sut.Mixes.Should().BeEmpty();
        var save = () => _sut.Save(Mix("m2", "Other"));
        save.Should().Throw<InvalidOperationException>();
        _sut.Delete("m1");
        _settings.Current.Browse.Mixes["s1/u1"].Should().ContainSingle();
    }
}
