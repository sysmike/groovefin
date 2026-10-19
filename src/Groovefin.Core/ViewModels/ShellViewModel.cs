using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Navigation;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// State of the main window: current section, back navigation and the title-bar search box.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly ILogger<ShellViewModel> _logger;

    public ShellViewModel(INavigationService navigation, ILogger<ShellViewModel> logger)
    {
        _navigation = navigation;
        _logger = logger;
        _navigation.Navigated += OnNavigated;
    }

    /// <summary>Gets the key of the selected main section, or <see langword="null"/> when another page is shown.</summary>
    [ObservableProperty]
    public partial string? SelectedSection { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    public partial bool CanGoBack { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitSearchCommand))]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>Shows the start page. Called once when the window is ready.</summary>
    public void Start() => _navigation.NavigateTo(PageKeys.Home);

    [RelayCommand]
    private void Navigate(string pageKey)
    {
        if (pageKey == _navigation.CurrentPageKey)
        {
            return;
        }

        _navigation.NavigateTo(pageKey);
    }

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
        SelectedSection = PageKeys.MainSections.Contains(e.PageKey) || e.PageKey == PageKeys.Settings
            ? e.PageKey
            : null;
    }

    public void Dispose() => _navigation.Navigated -= OnNavigated;
}
