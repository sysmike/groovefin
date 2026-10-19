using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    private readonly IThemeService _themeService = Substitute.For<IThemeService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IServerManager _servers = Substitute.For<IServerManager>();

    private SettingsViewModel CreateSut() => new(_themeService, _navigation, _servers, new AppVersion("1.0.0", "abc1234"));

    [Fact]
    public void Account_shows_the_signed_in_user()
    {
        _servers.IsSignedIn.Returns(true);
        _servers.ActiveServer.Returns(new ServerProfile { Id = "id-a", Name = "Alpha", Address = new Uri("http://alpha"), UserName = "Mike" });

        var sut = CreateSut();

        sut.UserName.Should().Be("Mike");
        sut.ServerName.Should().Be("Alpha");
        sut.SignOutCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SignOut_signs_out_and_opens_sign_in()
    {
        _servers.IsSignedIn.Returns(true);
        var sut = CreateSut();

        await sut.SignOutCommand.ExecuteAsync(null);

        await _servers.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo(PageKeys.SignIn);
    }

    [Fact]
    public void SignOut_is_disabled_when_nobody_is_signed_in()
    {
        CreateSut().SignOutCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ManageServers_opens_the_server_page()
    {
        var sut = CreateSut();

        sut.ManageServersCommand.Execute(null);

        _navigation.Received(1).NavigateTo(PageKeys.Servers);
    }

    [Fact]
    public void Initial_theme_comes_from_the_theme_service()
    {
        _themeService.Theme.Returns(AppTheme.Dark);

        var sut = CreateSut();

        sut.Theme.Should().Be(AppTheme.Dark);
        sut.ThemeIndex.Should().Be(2);
        _themeService.DidNotReceiveWithAnyArgs().SetTheme(default);
    }

    [Fact]
    public void Changing_the_theme_applies_it()
    {
        var sut = CreateSut();

        sut.Theme = AppTheme.Light;

        _themeService.Received(1).SetTheme(AppTheme.Light);
    }

    [Fact]
    public void ThemeIndex_maps_to_themes_and_raises_change_notification()
    {
        var sut = CreateSut();
        using var monitor = sut.Monitor();

        sut.ThemeIndex = 2;

        sut.Theme.Should().Be(AppTheme.Dark);
        monitor.Should().RaisePropertyChangeFor(x => x.ThemeIndex);
        _themeService.Received(1).SetTheme(AppTheme.Dark);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ThemeIndex_ignores_out_of_range_values(int index)
    {
        var sut = CreateSut();

        sut.ThemeIndex = index;

        sut.Theme.Should().Be(AppTheme.System);
    }
}
