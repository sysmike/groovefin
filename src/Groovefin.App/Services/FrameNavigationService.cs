using Groovefin.App.Views;
using Groovefin.Core.Navigation;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Services;

/// <summary>
/// <see cref="INavigationService"/> backed by the shell's content <see cref="Frame"/>.
/// </summary>
internal sealed partial class FrameNavigationService(ILogger<FrameNavigationService> logger) : INavigationService
{
    // TODO(#20): replace the placeholder pages as the sections land: Home #20, Albums #21, Artists #23,
    // Songs #37, Genres #38, Playlists #36, Folders #39, Favourites #40, Search #41.
    private static readonly Dictionary<string, Type> Pages = new(StringComparer.Ordinal)
    {
        [PageKeys.Home] = typeof(SectionPage),
        [PageKeys.Albums] = typeof(SectionPage),
        [PageKeys.Artists] = typeof(SectionPage),
        [PageKeys.Songs] = typeof(SectionPage),
        [PageKeys.Genres] = typeof(SectionPage),
        [PageKeys.Playlists] = typeof(SectionPage),
        [PageKeys.Folders] = typeof(SectionPage),
        [PageKeys.Favourites] = typeof(SectionPage),
        [PageKeys.Search] = typeof(SectionPage),
        [PageKeys.Settings] = typeof(SettingsPage),
        [PageKeys.Servers] = typeof(ServersPage),
    };

    private Frame? _frame;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public string? CurrentPageKey { get; private set; }

    public event EventHandler<NavigatedEventArgs>? Navigated;

    /// <summary>Connects the service to the shell frame. Called once by the main window.</summary>
    public void Initialize(Frame frame)
    {
        _frame = frame;
        _frame.Navigated += OnFrameNavigated;
        _frame.NavigationFailed += OnNavigationFailed;
    }

    public bool NavigateTo(string pageKey, object? parameter = null)
    {
        if (_frame is null)
        {
            throw new InvalidOperationException("Navigation service is not initialized.");
        }

        if (!Pages.TryGetValue(pageKey, out var pageType))
        {
            LogUnknownPage(pageKey);
            return false;
        }

        return _frame.Navigate(pageType, new PageNavigationArgs(pageKey, parameter), new EntranceNavigationTransitionInfo());
    }

    public bool GoBack()
    {
        if (_frame is not { CanGoBack: true })
        {
            return false;
        }

        _frame.GoBack();
        return true;
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Parameter is not PageNavigationArgs args)
        {
            return;
        }

        CurrentPageKey = args.PageKey;
        Navigated?.Invoke(this, new NavigatedEventArgs(args.PageKey, args.Parameter));
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        LogNavigationFailed(e.Exception, e.SourcePageType?.FullName);
        e.Handled = true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No page registered for key {PageKey}")]
    private partial void LogUnknownPage(string pageKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Navigation to {PageType} failed")]
    private partial void LogNavigationFailed(Exception exception, string? pageType);
}

/// <summary>Frame navigation parameter: the page key plus the caller's parameter.</summary>
internal sealed record PageNavigationArgs(string PageKey, object? Parameter);
