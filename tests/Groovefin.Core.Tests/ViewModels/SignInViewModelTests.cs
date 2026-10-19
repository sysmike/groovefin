using System.Net;
using Groovefin.Core.Api;
using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class SignInViewModelTests
{
    private static readonly ServerProfile Alpha = new() { Id = "id-a", Name = "Alpha", Address = new Uri("http://alpha:8096/"), UserName = "mike" };
    private static readonly ServerProfile Beta = new() { Id = "id-b", Name = "Beta", Address = new Uri("http://beta:8096/") };

    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public SignInViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _servers.Servers.Returns([Alpha, Beta]);
        _servers.ActiveServer.Returns(Alpha);
    }

    private SignInViewModel CreateSut(string? serverId = null)
    {
        var sut = new SignInViewModel(_servers, _navigation, _strings);
        sut.Initialize(serverId);
        return sut;
    }

    [Fact]
    public void Defaults_to_the_active_server_and_its_last_user()
    {
        var sut = CreateSut();

        sut.ServerName.Should().Be("Alpha");
        sut.ServerAddress.Should().Be("http://alpha:8096");
        sut.UserName.Should().Be("mike");
    }

    [Fact]
    public void Uses_the_requested_server()
    {
        var sut = CreateSut("id-b");

        sut.ServerName.Should().Be("Beta");
        sut.UserName.Should().BeEmpty();
        sut.SignInCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Successful_sign_in_clears_the_password_and_opens_home()
    {
        var sut = CreateSut();
        sut.Password = "secret";

        await sut.SignInCommand.ExecuteAsync(null);

        await _servers.Received(1).SignInAsync("id-a", "mike", "secret", Arg.Any<CancellationToken>());
        sut.Password.Should().BeEmpty();
        _navigation.Received(1).NavigateTo(PageKeys.Home);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "SignIn_WrongCredentials")]
    [InlineData(HttpStatusCode.InternalServerError, "SignIn_Failed")]
    [InlineData(null, "SignIn_Unreachable")]
    public async Task Failures_are_shown_on_the_page(HttpStatusCode? status, string expectedKey)
    {
        _servers.SignInAsync(default!, default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(_ => throw new JellyfinException("x", status));
        var sut = CreateSut();

        await sut.SignInCommand.ExecuteAsync(null);

        sut.Error.Should().Be(expectedKey);
        sut.HasError.Should().BeTrue();
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);
    }

    [Fact]
    public void ChooseServer_opens_the_server_page()
    {
        CreateSut().ChooseServerCommand.Execute(null);

        _navigation.Received(1).NavigateTo(PageKeys.Servers);
    }
}
