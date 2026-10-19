using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// State of the main window: current section, back navigation, the title-bar search box and the
/// notification area.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly IServerManager _servers;
    private readonly ILogger<ShellViewModel> _logger;

    public ShellViewModel(
        INavigationService navigation,
        INotificationService notifications,
        IServerManager servers,
        ILogger<ShellViewModel> logger)
    {
        _navigation = navigation;
        _notifications = notifications;
        _servers = servers;
        _logger = logger;
        _navigation.Navigated += OnNavigated;
    }

    /// <summary>Gets the notifications shown above the page content.</summary>
    public ReadOnlyObservableCollection<AppNotification> Notifications => _notifications.Notifications;

    /// <summary>Gets the key of the selected main section, or <see langword="null"/> when another page is shown.</summary>
    [ObservableProperty]
    public partial string? SelectedSection { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    public partial bool CanGoBack { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitSearchCommand))]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Reconnects to the active server and shows the start page, the sign-in page when no login is
    /// stored, or the server page on first run.
    /// Called once when the window is ready.
    /// </summary>
    public void Start()
    {
        _servers.Restore();
        _navigation.NavigateTo(
            _servers.Servers.Count == 0 ? PageKeys.Servers
            : !_servers.IsSignedIn ? PageKeys.SignIn
            : PageKeys.Home);
    }

    [RelayCommand]
    private void Navigate(string pageKey)
    {
        if (pageKey == _navigation.CurrentPageKey)
        {
            return;
        }

        _navigation.NavigateTo(pageKey);
    }

    [RelayCommand]
    private void DismissNotification(AppNotification notification) => _notifications.Dismiss(notification);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _navigation.GoBack();

    [RelayCommand(CanExecute = nameof(CanSubmitSearch))]
    private void SubmitSearch()
    {
        // TODO(#41): show grouped search results and live suggestions.
        _logger.LogDebug("Search submitted");
        _navigation.NavigateTo(PageKeys.Search, SearchText.Trim());
    }

    private bool CanSubmitSearch() => !string.IsNullOrWhiteSpace(SearchText);

    private void OnNavigated(object? sender, NavigatedEventArgs e)
    {
        CanGoBack = _navigation.CanGoBack;
        SelectedSection = e.PageKey switch
        {
            // Server management is part of Settings.
            PageKeys.Settings or PageKeys.Servers => PageKeys.Settings,
            _ when PageKeys.MainSections.Contains(e.PageKey) => e.PageKey,
            _ => null,
        };
    }

    public void Dispose() => _navigation.Navigated -= OnNavigated;
}
