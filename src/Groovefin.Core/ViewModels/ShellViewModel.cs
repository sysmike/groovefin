using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// State of the main window: current section, back and forward navigation, the title-bar search box and the
/// notification area.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly IServerManager _servers;
    private readonly ISettingsService _settings;
    private readonly ILogger<ShellViewModel> _logger;

    public ShellViewModel(
        INavigationService navigation,
        INotificationService notifications,
        IServerManager servers,
        ISettingsService settings,
        ILogger<ShellViewModel> logger)
    {
        _navigation = navigation;
        _notifications = notifications;
        _servers = servers;
        _settings = settings;
        _logger = logger;
        _navigation.Navigated += OnNavigated;
        _settings.Changed += OnSettingsChanged;
        VisibleSections = NavigationLayout.VisibleSections(settings.Current.Appearance.Navigation);
    }

    /// <summary>Gets the keys of the main sections the navigation pane shows, in the user's order (#255).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> VisibleSections { get; private set; }

    /// <summary>Gets the notifications shown above the page content.</summary>
    public ReadOnlyObservableCollection<AppNotification> Notifications => _notifications.Notifications;

    /// <summary>Gets the key of the selected main section, or <see langword="null"/> when another page is shown.</summary>
    [ObservableProperty]
    public partial string? SelectedSection { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    public partial bool CanGoBack { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    public partial bool CanGoForward { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitSearchCommand))]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Reconnects to the active server and shows the chosen start page (home by default), the sign-in
    /// page when no login is stored, or the server page on first run.
    /// Called once when the window is ready.
    /// </summary>
    public void Start()
    {
        _servers.Restore();
        _navigation.NavigateTo(
            _servers.Servers.Count == 0 ? PageKeys.Servers
            : !_servers.IsSignedIn ? PageKeys.SignIn
            : StartPage());
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

    /// <summary>Forward again after going back: the mouse's forward button, Alt+Right (#176).</summary>
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => _navigation.GoForward();

    [RelayCommand(CanExecute = nameof(CanSubmitSearch))]
    private void SubmitSearch()
    {
        // TODO(#41): show grouped search results and live suggestions.
        _logger.LogDebug("Search submitted");
        _navigation.NavigateTo(PageKeys.Search, SearchText.Trim());
    }

    /// <summary>
    /// A start page that is no longer a main section (older settings) falls back to home; one hidden from the
    /// navigation pane to the first section shown (#255).
    /// </summary>
    private string StartPage()
    {
        var page = _settings.Current.Appearance.StartPage;
        page = PageKeys.MainSections.Contains(page) ? page : PageKeys.Home;
        return VisibleSections.Contains(page) ? page : VisibleSections[0];
    }

    private bool CanSubmitSearch() => !string.IsNullOrWhiteSpace(SearchText);

    private void OnNavigated(object? sender, NavigatedEventArgs e)
    {
        CanGoBack = _navigation.CanGoBack;
        CanGoForward = _navigation.CanGoForward;
        SelectedSection = e.PageKey switch
        {
            // Server management is part of Settings.
            PageKeys.Settings or PageKeys.Servers => PageKeys.Settings,
            _ when PageKeys.MainSections.Contains(e.PageKey) => e.PageKey,
            _ => null,
        };
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var sections = NavigationLayout.VisibleSections(e.NewSettings.Appearance.Navigation);
        if (!sections.SequenceEqual(VisibleSections))
        {
            VisibleSections = sections;
        }
    }

    public void Dispose()
    {
        _navigation.Navigated -= OnNavigated;
        _settings.Changed -= OnSettingsChanged;
    }
}
