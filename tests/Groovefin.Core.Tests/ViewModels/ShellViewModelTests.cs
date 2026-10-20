using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly InMemorySettingsService _settings = new();

    public ShellViewModelTests()
    {
        _servers.Servers.Returns([new ServerProfile { Id = "id-a", Name = "Alpha", Address = new Uri("http://alpha:8096") }]);
        _servers.IsSignedIn.Returns(true);
    }

    private ShellViewModel CreateSut() => new(_navigation, _notifications, _servers, _settings, NullLogger<ShellViewModel>.Instance);

    [Fact]
    public void Start_restores_the_connection_and_navigates_to_home()
    {
        using var sut = CreateSut();

        sut.Start();

        _servers.Received(1).Restore();
        _navigation.Received(1).NavigateTo(PageKeys.Home);
    }

    [Theory]
    [InlineData(PageKeys.Albums, PageKeys.Albums)]
    [InlineData(PageKeys.Favourites, PageKeys.Favourites)]
    [InlineData(PageKeys.Settings, PageKeys.Home)]
    [InlineData("removed-section", PageKeys.Home)]
    public void Start_opens_the_chosen_start_page_if_it_is_a_main_section(string startPage, string expected)
    {
        _settings.Update(s => s with { Appearance = s.Appearance with { StartPage = startPage } });
        using var sut = CreateSut();

        sut.Start();

        _navigation.Received(1).NavigateTo(expected);
    }

    [Fact]
    public void Start_without_servers_opens_the_server_page()
    {
        _servers.Servers.Returns([]);
        using var sut = CreateSut();

        sut.Start();

        _navigation.Received(1).NavigateTo(PageKeys.Servers);
    }

    [Fact]
    public void Start_without_a_stored_login_opens_sign_in()
    {
        _servers.IsSignedIn.Returns(false);
        using var sut = CreateSut();

        sut.Start();

        _navigation.Received(1).NavigateTo(PageKeys.SignIn);
    }

    [Fact]
    public void Server_page_selects_the_settings_item()
    {
        using var sut = CreateSut();

        RaiseNavigated(PageKeys.Servers);

        sut.SelectedSection.Should().Be(PageKeys.Settings);
    }

    [Fact]
    public void Navigate_goes_to_the_requested_section()
    {
        using var sut = CreateSut();

        sut.NavigateCommand.Execute(PageKeys.Albums);

        _navigation.Received(1).NavigateTo(PageKeys.Albums);
    }

    [Fact]
    public void Navigate_to_the_current_page_does_nothing()
    {
        _navigation.CurrentPageKey.Returns(PageKeys.Albums);
        using var sut = CreateSut();

        sut.NavigateCommand.Execute(PageKeys.Albums);

        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!);
    }

    [Fact]
    public void Navigated_event_updates_back_state_and_selected_section()
    {
        using var sut = CreateSut();
        _navigation.CanGoBack.Returns(true);

        RaiseNavigated(PageKeys.Artists);

        sut.CanGoBack.Should().BeTrue();
        sut.GoBackCommand.CanExecute(null).Should().BeTrue();
        sut.SelectedSection.Should().Be(PageKeys.Artists);
    }

    [Fact]
    public void Navigated_to_a_non_section_page_clears_the_selection()
    {
        using var sut = CreateSut();
        RaiseNavigated(PageKeys.Albums);

        RaiseNavigated(PageKeys.Search);

        sut.SelectedSection.Should().BeNull();
    }

    [Fact]
    public void Settings_page_is_selectable()
    {
        using var sut = CreateSut();

        RaiseNavigated(PageKeys.Settings);

        sut.SelectedSection.Should().Be(PageKeys.Settings);
    }

    [Fact]
    public void GoBack_is_disabled_until_navigation_reports_a_back_stack()
    {
        using var sut = CreateSut();

        sut.GoBackCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void GoBack_delegates_to_navigation()
    {
        using var sut = CreateSut();
        _navigation.CanGoBack.Returns(true);
        RaiseNavigated(PageKeys.Albums);

        sut.GoBackCommand.Execute(null);

        _navigation.Received(1).GoBack();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SubmitSearch_is_disabled_for_blank_text(string text)
    {
        using var sut = CreateSut();

        sut.SearchText = text;

        sut.SubmitSearchCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SubmitSearch_navigates_to_search_with_trimmed_query()
    {
        using var sut = CreateSut();
        sut.SearchText = "  daft punk ";

        sut.SubmitSearchCommand.Execute(null);

        _navigation.Received(1).NavigateTo(PageKeys.Search, "daft punk");
    }

    [Fact]
    public void DismissNotification_removes_it_from_the_notification_service()
    {
        using var sut = CreateSut();
        var notification = new AppNotification(NotificationSeverity.Error, "title", "message");

        sut.DismissNotificationCommand.Execute(notification);

        _notifications.Received(1).Dismiss(notification);
    }

    [Fact]
    public void Dispose_unsubscribes_from_navigation()
    {
        var sut = CreateSut();
        sut.Dispose();
        _navigation.CanGoBack.Returns(true);

        RaiseNavigated(PageKeys.Albums);

        sut.CanGoBack.Should().BeFalse();
    }

    private void RaiseNavigated(string pageKey) =>
        _navigation.Navigated += Raise.EventWith(_navigation, new NavigatedEventArgs(pageKey, null));
}
