using Groovefin.App.Controls;
using Groovefin.App.Views;
using Groovefin.Core.Navigation;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Services;

/// <summary>
/// <see cref="INavigationService"/> backed by the shell's content <see cref="Frame"/>. Pages that are not
/// cached keep their scroll position, tab and focused item for Back and Forward (#284, <see cref="ViewMemory"/>).
/// </summary>
internal sealed partial class FrameNavigationService(ILogger<FrameNavigationService> logger) : INavigationService
{
    private static readonly Dictionary<string, Type> Pages = new(StringComparer.Ordinal)
    {
        [PageKeys.Home] = typeof(HomePage),
        [PageKeys.Albums] = typeof(AlbumsPage),
        [PageKeys.Artists] = typeof(ArtistsPage),
        [PageKeys.Songs] = typeof(SongsPage),
        [PageKeys.Genres] = typeof(GenresPage),
        [PageKeys.Playlists] = typeof(PlaylistsPage),
        [PageKeys.Folders] = typeof(FoldersPage),
        [PageKeys.Favourites] = typeof(FavouritesPage),
        [PageKeys.OnRepeat] = typeof(OnRepeatPage),
        [PageKeys.Search] = typeof(SearchPage),
        [PageKeys.Settings] = typeof(SettingsPage),
        [PageKeys.Album] = typeof(AlbumDetailPage),
        [PageKeys.Artist] = typeof(ArtistDetailPage),
        [PageKeys.Playlist] = typeof(PlaylistDetailPage),
        [PageKeys.Genre] = typeof(GenreDetailPage),
        [PageKeys.Servers] = typeof(ServersPage),
        [PageKeys.SignIn] = typeof(SignInPage),
    };

    private readonly ViewStateStore _views = new();
    private Frame? _frame;
    private PageNavigationArgs? _current;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public bool CanGoForward => _frame?.CanGoForward ?? false;

    public string? CurrentPageKey { get; private set; }

    public event EventHandler<NavigatedEventArgs>? Navigated;

    /// <summary>Connects the service to the shell frame. Called once by the main window.</summary>
    public void Initialize(Frame frame)
    {
        _frame = frame;
        _frame.Navigating += OnFrameNavigating;
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

        var args = new PageNavigationArgs(pageKey, parameter) { EntryId = _views.NewEntryId() };
        return _frame.Navigate(pageType, args, new EntranceNavigationTransitionInfo());
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

    public bool GoForward()
    {
        if (_frame is not { CanGoForward: true })
        {
            return false;
        }

        _frame.GoForward();
        return true;
    }

    /// <summary>The page is about to be left: remember what it shows. Cached pages keep that themselves.</summary>
    private void OnFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (_frame?.Content is Page { NavigationCacheMode: NavigationCacheMode.Disabled } page && _current is { } current)
        {
            _views.Save(current.EntryId, ViewMemory.Capture(page));
        }
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Parameter is not PageNavigationArgs args)
        {
            return;
        }

        _current = args;
        if (e.NavigationMode is NavigationMode.Back or NavigationMode.Forward
            && e.Content is Page { NavigationCacheMode: NavigationCacheMode.Disabled } page
            && _views.Get(args.EntryId) is { } state)
        {
            ViewMemory.Restore(page, state);
        }

        // A new page clears the forward stack: forget the pages that left it.
        if (_frame is not null)
        {
            _views.Retain(
                _frame.BackStack.Concat(_frame.ForwardStack)
                    .Select(entry => entry.Parameter)
                    .OfType<PageNavigationArgs>()
                    .Select(entry => entry.EntryId)
                    .Append(args.EntryId));
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

/// <summary>
/// Frame navigation parameter: the page key plus the caller's parameter. <see cref="EntryId"/> tells the
/// stack entries apart, so two visits of the same album each keep their own scroll position (#284).
/// </summary>
internal sealed record PageNavigationArgs(string PageKey, object? Parameter)
{
    public long EntryId { get; init; }
}
