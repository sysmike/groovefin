using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private ShellViewModel CreateSut() => new(_navigation, _notifications, NullLogger<ShellViewModel>.Instance);

    [Fact]
    public void Start_navigates_to_home()
    {
        using var sut = CreateSut();

        sut.Start();

        _navigation.Received(1).NavigateTo(PageKeys.Home);
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
