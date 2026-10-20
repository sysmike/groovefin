using Groovefin.Core.Api;
using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class ServersViewModelTests
{
    private static readonly ServerProfile Alpha = new() { Id = "id-a", Name = "Alpha", Address = new Uri("http://alpha:8096/") };
    private static readonly ServerProfile Beta = new() { Id = "id-b", Name = "Beta", Address = new Uri("http://beta:8096/") };

    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private IDispatcher _dispatcher = new ImmediateDispatcher();

    public ServersViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _servers.Servers.Returns([Alpha, Beta]);
        _servers.ActiveServer.Returns(Alpha);
    }

    private ServersViewModel CreateSut() => new(_servers, _dialogs, _navigation, _strings, _dispatcher);

    private void RaiseChanged() => _servers.Changed += Raise.Event();

    [Fact]
    public void Lists_servers_and_marks_the_active_one()
    {
        using var sut = CreateSut();

        sut.Servers.Select(s => (s.Name, s.IsActive)).Should().Equal(("Alpha", true), ("Beta", false));
        sut.Servers[0].Address.Should().Be("http://alpha:8096");
        sut.HasServers.Should().BeTrue();
    }

    [Fact]
    public void List_follows_server_manager_changes()
    {
        using var sut = CreateSut();
        _servers.Servers.Returns([Beta]);
        _servers.ActiveServer.Returns(Beta);

        RaiseChanged();

        sut.Servers.Should().ContainSingle().Which.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Add_clears_the_address_on_success()
    {
        using var sut = CreateSut();
        sut.NewAddress = "gamma:8096";

        await sut.AddCommand.ExecuteAsync(null);

        await _servers.Received(1).AddAsync("gamma:8096", Arg.Any<CancellationToken>());
        sut.NewAddress.Should().BeEmpty();
        sut.HasFormError.Should().BeFalse();
    }

    [Fact]
    public async Task First_server_continues_to_sign_in()
    {
        _servers.Servers.Returns([]);
        using var sut = CreateSut();
        _servers.AddAsync("alpha", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _servers.Servers.Returns([Alpha]);
            RaiseChanged();
            return Alpha;
        });
        sut.NewAddress = "alpha";

        await sut.AddCommand.ExecuteAsync(null);

        _navigation.Received(1).NavigateTo(PageKeys.SignIn, "id-a");
    }

    [Theory]
    [InlineData("argument", "Servers_InvalidAddress")]
    [InlineData("unreachable", "Servers_Unreachable")]
    [InlineData("not-jellyfin", "Servers_NotJellyfin")]
    public async Task Add_failures_are_shown_inline(string failure, string expectedKey)
    {
        Exception exception = failure switch
        {
            "argument" => new ArgumentException("bad"),
            "unreachable" => new JellyfinException("down", statusCode: null),
            _ => new JellyfinException("404", System.Net.HttpStatusCode.NotFound),
        };
        _servers.AddAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<ServerProfile>(_ => throw exception);
        using var sut = CreateSut();
        sut.NewAddress = "something";

        await sut.AddCommand.ExecuteAsync(null);

        sut.FormError.Should().Be(expectedKey);
        sut.HasFormError.Should().BeTrue();
        sut.NewAddress.Should().Be("something");
    }

    [Fact]
    public void Add_is_disabled_for_a_blank_address()
    {
        using var sut = CreateSut();

        sut.NewAddress = "  ";

        sut.AddCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Remove_asks_for_confirmation()
    {
        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(false, true);
        _strings.GetString("Servers_RemoveMessage").Returns("Remove {0}?");
        using var sut = CreateSut();

        await sut.RemoveCommand.ExecuteAsync(sut.Servers[1]);
        _servers.DidNotReceiveWithAnyArgs().Remove(default!);

        await sut.RemoveCommand.ExecuteAsync(sut.Servers[1]);
        _servers.Received(1).Remove("id-b");
        await _dialogs.Received().ConfirmAsync(Arg.Any<string>(), "Remove Beta?", Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task EditAddress_updates_the_server_with_the_entered_address()
    {
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs("https://alpha.example.com");
        using var sut = CreateSut();

        await sut.EditAddressCommand.ExecuteAsync(sut.Servers[0]);

        await _servers.Received(1).UpdateAddressAsync("id-a", "https://alpha.example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditAddress_to_a_different_server_shows_an_error()
    {
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs("beta:8096");
        _servers.UpdateAddressAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs<ServerProfile>(_ => throw new InvalidOperationException());
        using var sut = CreateSut();

        await sut.EditAddressCommand.ExecuteAsync(sut.Servers[0]);

        sut.FormError.Should().Be("Servers_DifferentServer");
    }

    [Fact]
    public async Task Cancelled_edit_does_nothing()
    {
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs((string?)null);
        using var sut = CreateSut();

        await sut.EditAddressCommand.ExecuteAsync(sut.Servers[0]);

        await _servers.DidNotReceiveWithAnyArgs().UpdateAddressAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void SetActive_switches_to_an_inactive_server_only()
    {
        _servers.IsSignedIn.Returns(true);
        using var sut = CreateSut();

        sut.SetActiveCommand.Execute(sut.Servers[0]);
        sut.SetActiveCommand.Execute(sut.Servers[1]);

        _servers.Received(1).SetActive("id-b");
        _servers.DidNotReceive().SetActive("id-a");
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);
    }

    [Fact]
    public void SetActive_without_a_stored_login_opens_sign_in()
    {
        _servers.IsSignedIn.Returns(false);
        using var sut = CreateSut();

        sut.SetActiveCommand.Execute(sut.Servers[1]);

        _navigation.Received(1).NavigateTo(PageKeys.SignIn, "id-b");
    }

    [Fact]
    public void A_change_from_a_background_thread_updates_the_list_on_the_ui_thread()
    {
        var ui = new QueuedDispatcher();
        _dispatcher = ui;
        using var sut = CreateSut();
        _servers.Servers.Returns([Alpha]);

        RaiseChanged();

        sut.Servers.Should().HaveCount(2, "the list is bound to the UI and must not change off its thread");
        ui.Pending.Should().Be(1);

        ui.RunAll();

        sut.Servers.Select(s => s.Name).Should().Equal("Alpha");
    }
}
