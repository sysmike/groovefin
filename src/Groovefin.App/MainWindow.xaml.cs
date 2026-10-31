using System.ComponentModel;
using System.Runtime.InteropServices;
using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.Favourites;
using Groovefin.Core.Images;
using Groovefin.Core.Layout;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Shortcuts;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace Groovefin.App;

/// <summary>
/// Shell window: Mica backdrop, title bar with search, navigation pane and content frame.
/// </summary>
internal sealed partial class MainWindow : Window
{
    /// <summary>
    /// The mini player's start size in effective pixels, of the client area: a 32 px drag strip, then the 96 px
    /// cover with 12 px margins and the text beside it.
    /// </summary>
    private const int MiniPlayerWidth = 420;
    private const int MiniPlayerHeight = 140;

    /// <summary>How far the mini player's client area can be resized (#183): from just the layout to where it still makes sense.</summary>
    private const int MiniPlayerMinWidth = 320;
    private const int MiniPlayerMinHeight = 140;
    private const int MiniPlayerMaxWidth = 720;
    private const int MiniPlayerMaxHeight = 200;

    /// <summary>
    /// The smallest normal window's client area in effective pixels (#241). The now playing bar needs about 815
    /// for its columns (200, the transport and seek bar, 340), and beside the 400 px queue or lyrics the page
    /// keeps about 412 with the navigation folded to 48.
    /// </summary>
    private const int MinWindowWidth = 860;
    private const int MinWindowHeight = 600;

    /// <summary>The drag data of a favourite playlist being moved in the pane (#306): its ID.</summary>
    private const string PlaylistMoveFormat = "Groovefin.FavouritePlaylist";
    /// <summary>How far the mouse moves with the button held before a favourite playlist's drag starts.</summary>
    private const double PlaylistDragThreshold = 4;
    private const string UpGlyph = "\uE74A";
    private const string DownGlyph = "\uE74B";

    private readonly FrameNavigationService _navigation;
    private readonly FavouritePlaylists _favouritePlaylists;
    private readonly TrackDrag _trackDrag;
    private readonly IStringResources _strings;
    private readonly ISettingsService _settings;
    private readonly ShortcutService _shortcuts;
    private readonly NotificationAreaIcon _tray;
    /// <summary>The navigation pane's section items by page key, and the separator after Home (#255).</summary>
    private readonly Dictionary<string, NavigationViewItem> _sectionItems;
    private readonly NavigationViewItemSeparator _homeSeparator;
    /// <summary>The favourite playlists' items by playlist ID, after their separator at the end of the pane (#297).</summary>
    private readonly Dictionary<string, NavigationViewItem> _playlistItems = new(StringComparer.Ordinal);
    /// <summary>Whether the pane shows names: the favourite playlists then show their cover beside them, else as their icon (#299).</summary>
    private bool _paneOpen = true;
    /// <summary>The favourite playlist being dragged to another place in the pane (#306), or <see langword="null"/>.</summary>
    private string? _movingPlaylistId;
    /// <summary>Where the left mouse button went down on a favourite playlist, until it moves far enough to drag (#308).</summary>
    private Windows.Foundation.Point? _playlistPressPoint;
    private readonly NavigationViewItemSeparator _playlistsSeparator = new();
    /// <summary>The normal window's presenter, set again when the mini or full-screen player closes.</summary>
    private readonly OverlappedPresenter? _windowPresenter;
    /// <summary>The full window's place and size, saved on closing for the next start (#272).</summary>
    private readonly WindowPlacement _placement = new();
    private RectInt32? _restoreBounds;
    private bool _restoreMaximized;

    public MainWindow(
        ShellViewModel viewModel,
        FrameNavigationService navigation,
        ThemeService themeService,
        ContentDialogService dialogs,
        QueueViewModel queue,
        SearchSuggestionsViewModel suggestions,
        LyricsViewModel lyrics,
        NowPlayingViewModel nowPlaying,
        PrivacyMode privacy,
        FavouritePlaylists favouritePlaylists,
        TrackDrag trackDrag,
        IStringResources strings,
        ISettingsService settings,
        ShortcutService shortcuts)
    {
        _navigation = navigation;
        _shortcuts = shortcuts;
        _favouritePlaylists = favouritePlaylists;
        _trackDrag = trackDrag;
        _strings = strings;
        ViewModel = viewModel;
        Queue = queue;
        Lyrics = lyrics;
        Player = nowPlaying;
        Privacy = privacy;
        Suggestions = suggestions;
        InitializeComponent();

        _sectionItems = NavView.MenuItems.OfType<NavigationViewItem>().ToDictionary(item => (string)item.Tag, StringComparer.Ordinal);
        _homeSeparator = NavView.MenuItems.OfType<NavigationViewItemSeparator>().Single();
        ApplyNavigationLayout();
        favouritePlaylists.Items.CollectionChanged += (_, _) => ApplyNavigationLayout();

        _settings = settings;
        Title = new ResourceLoader().GetString("AppDisplayName");
        AppWindow.SetIcon(Path.Join(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SystemBackdrop = CreateBackdrop();
        _windowPresenter = AppWindow.Presenter as OverlappedPresenter;

        // Where the window was last (#272), before it first shows, so it doesn't jump.
        RestorePlacement(settings.Current.Window);
        ObservePlacement();
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
            {
                ObservePlacement();
            }
        };

        navigation.Initialize(ContentFrame);
        themeService.Attach(this);
        RootGrid.Loaded += (_, _) =>
        {
            dialogs.Attach(RootGrid.XamlRoot);
            ApplyMinimumSize();
            RootGrid.XamlRoot.Changed += (_, _) => ApplyMinimumSize();
        };

        // Also when a control under the pointer (a list, a button) has handled the press itself.
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), handledEventsToo: true);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // The full-screen and mini players switch the window's presenter; leaving them by other means
        // (Windows' own controls) closes them too.
        Player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NowPlayingViewModel.IsFullScreen) or nameof(NowPlayingViewModel.IsMiniPlayer))
            {
                ApplyPresenter();
            }
        };
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPresenterChange)
            {
                Player.IsFullScreen = CurrentMode == WindowMode.FullScreen;
                Player.IsMiniPlayer = CurrentMode == WindowMode.MiniPlayer;
            }

            // Minimized with "minimize to tray": only the notification area icon stays.
            if (_tray is { IsVisible: true } && _settings.Current.Appearance.MinimizeToTray && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            {
                AppWindow.Hide();
            }
        };

        // The optional notification area icon follows its setting.
        _tray = new NotificationAreaIcon(Player, Privacy, strings, Title, ShowFromTray, Close);
        _tray.SetVisible(settings.Current.Appearance.ShowTrayIcon);
        settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            settings.Changed -= OnSettingsChanged;
            _tray.Dispose();

            // Before the app's shutdown flushes the settings.
            if (_placement.ToSettings() is { } placement)
            {
                settings.Update(s => s with { Window = placement });
            }
        };

        // The queue and the lyrics share the right side: opening one closes the other.
        Queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QueueViewModel.IsOpen) && Queue.IsOpen)
            {
                Lyrics.IsOpen = false;
            }
        };
        Lyrics.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LyricsViewModel.IsOpen) && Lyrics.IsOpen)
            {
                Queue.IsOpen = false;
            }
        };

        ViewModel.Start();
        _ = favouritePlaylists.StartAsync();
    }

    public ShellViewModel ViewModel { get; }

    public QueueViewModel Queue { get; }

    public SearchSuggestionsViewModel Suggestions { get; }

    public LyricsViewModel Lyrics { get; }

    /// <summary>Gets the now playing state, also shown by the full-screen player. (The bar itself is named NowPlaying.)</summary>
    public NowPlayingViewModel Player { get; }

    /// <summary>Gets privacy mode, which the title bar button next to the search box turns on and off (#234).</summary>
    public PrivacyMode Privacy { get; }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => _tray.SetVisible(_settings.Current.Appearance.ShowTrayIcon));

    /// <summary>Brings the window back from the notification area (or from minimized) and to the front.</summary>
    private void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    /// <summary>What the window shows: the app, the mini player or the full-screen player.</summary>
    private enum WindowMode
    {
        Normal,
        MiniPlayer,
        FullScreen,
    }

    /// <summary>Gets the mode of the presenter in place; only the mini player's window stays on top.</summary>
    private WindowMode CurrentMode => AppWindow.Presenter switch
    {
        { Kind: AppWindowPresenterKind.FullScreen } => WindowMode.FullScreen,
        OverlappedPresenter { IsAlwaysOnTop: true } => WindowMode.MiniPlayer,
        _ => WindowMode.Normal,
    };

    /// <summary>
    /// Shows the presenter the players ask for: the mini player's small window, full screen or the normal
    /// window. The normal window's place and size are kept while the mini player is open and come back after.
    /// </summary>
    private void ApplyPresenter()
    {
        var wanted = Player.IsMiniPlayer ? WindowMode.MiniPlayer
            : Player.IsFullScreen ? WindowMode.FullScreen
            : WindowMode.Normal;

        // The full-screen player covers the title bar, but its row kept the height and pushed the queue and
        // lyrics panes down: in full screen it is collapsed. Also when Windows' own controls left full screen.
        AppTitleBar.Visibility = wanted == WindowMode.FullScreen ? Visibility.Collapsed : Visibility.Visible;
        var current = CurrentMode;
        if (wanted == current)
        {
            return;
        }

        if (wanted == WindowMode.MiniPlayer && current == WindowMode.Normal && AppWindow.Presenter is OverlappedPresenter overlapped)
        {
            _restoreBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            _restoreMaximized = overlapped.State == OverlappedPresenterState.Maximized;
        }

        switch (wanted)
        {
            case WindowMode.MiniPlayer:
                var mini = CreateMiniPlayerPresenter();
                AppWindow.SetPresenter(mini);
                SizeMiniPlayer(mini);
                break;
            case WindowMode.FullScreen:
                AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                break;
            default:
                if (_windowPresenter is not null)
                {
                    AppWindow.SetPresenter(_windowPresenter);
                }
                else
                {
                    AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                }

                break;
        }

        // Also after mini player → full screen → window.
        if (wanted == WindowMode.Normal && _restoreBounds is { } bounds)
        {
            AppWindow.MoveAndResize(bounds);
            if (_restoreMaximized && AppWindow.Presenter is OverlappedPresenter restored)
            {
                restored.Maximize();
            }

            _restoreBounds = null;
        }

        // Only the mini player's top strip can move the small window; its caption buttons fit that strip.
        SetTitleBar(Player.IsMiniPlayer ? MiniTitleBar : AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = Player.IsMiniPlayer ? TitleBarHeightOption.Standard : TitleBarHeightOption.Tall;
    }

    /// <summary>
    /// The mini player's window (#183): always on top, resizable between its limits, with only a close button.
    /// A normal window rather than Windows' compact overlay, which sets its own size limits.
    /// </summary>
    private static OverlappedPresenter CreateMiniPlayerPresenter()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = true;
        return presenter;
    }

    /// <summary>
    /// Gives the mini player's client area, where the layout goes, its start size and limits (#222). The window's
    /// size also counts Windows 11's invisible resize borders, which took the cover's bottom margin, so the frame
    /// is measured and added. Not <c>ResizeClient</c>: it adds a caption the window, with its content in the
    /// title bar, doesn't have (23 px too high in CI).
    /// </summary>
    private void SizeMiniPlayer(OverlappedPresenter presenter)
    {
        var client = Scaled(MiniPlayerWidth, MiniPlayerHeight);
        AppWindow.Resize(client);
        var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        AppWindow.Resize(new SizeInt32(client.Width + frameWidth, client.Height + frameHeight));

        // The limits apply to the whole window too.
        var minimum = Scaled(MiniPlayerMinWidth, MiniPlayerMinHeight);
        var maximum = Scaled(MiniPlayerMaxWidth, MiniPlayerMaxHeight);
        presenter.PreferredMinimumWidth = minimum.Width + frameWidth;
        presenter.PreferredMinimumHeight = minimum.Height + frameHeight;
        presenter.PreferredMaximumWidth = maximum.Width + frameWidth;
        presenter.PreferredMaximumHeight = maximum.Height + frameHeight;
    }

    /// <summary>
    /// Gives the normal window its smallest size (#241), again whenever its XamlRoot changes (size, display
    /// scale). Like the mini player's limits (#222), they apply to the whole window, so its frame (Windows 11's
    /// invisible resize borders) is measured and added; that works only while the normal window shows, not
    /// maximized, so a window that starts maximized gets them once it is restored.
    /// </summary>
    private void ApplyMinimumSize()
    {
        if (_windowPresenter is null || CurrentMode != WindowMode.Normal || _windowPresenter.State != OverlappedPresenterState.Restored)
        {
            return;
        }

        var minimum = Scaled(MinWindowWidth, MinWindowHeight);
        var frameWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var frameHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        _windowPresenter.PreferredMinimumWidth = minimum.Width + frameWidth;
        _windowPresenter.PreferredMinimumHeight = minimum.Height + frameHeight;

        // A window from a smaller screen or an earlier size grows to the minimum at once.
        if (AppWindow.ClientSize.Width < minimum.Width || AppWindow.ClientSize.Height < minimum.Height)
        {
            AppWindow.Resize(new SizeInt32(
                Math.Max(AppWindow.Size.Width, minimum.Width + frameWidth),
                Math.Max(AppWindow.Size.Height, minimum.Height + frameHeight)));
        }
    }

    /// <summary>
    /// Opens the window with its saved place, size and maximized state (#272): on the screen it was on if that
    /// is still there, otherwise centred on the primary screen; see <see cref="WindowPlacement.Restore"/>.
    /// </summary>
    private void RestorePlacement(WindowSettings? saved)
    {
        if (saved is null || _windowPresenter is null)
        {
            return;
        }

        var bounds = WindowPlacement.Restore(saved, Screens(), MinWindowWidth, MinWindowHeight);
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));

        // Noted before maximizing: un-maximizing gives this size back, and it is what closing saves.
        _placement.Observe(true, WindowShowState.Normal, bounds, WindowDpi());
        if (saved.IsMaximized)
        {
            _windowPresenter.Maximize();
        }
    }

    /// <summary>Notes the window's place and state; only the full window's count (see <see cref="WindowPlacement"/>).</summary>
    private void ObservePlacement()
    {
        var state = _windowPresenter?.State switch
        {
            OverlappedPresenterState.Maximized => WindowShowState.Maximized,
            OverlappedPresenterState.Minimized => WindowShowState.Minimized,
            _ => WindowShowState.Normal,
        };
        var bounds = new PixelRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        _placement.Observe(CurrentMode == WindowMode.Normal, state, bounds, WindowDpi());
    }

    private int WindowDpi() => (int)GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));

    /// <summary>The screens' work areas and DPIs. By index: enumerating the list fails in some Windows App SDK versions.</summary>
    private static List<ScreenArea> Screens()
    {
        var areas = DisplayArea.FindAll();
        var screens = new List<ScreenArea>(areas.Count);
        for (var i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            var work = area.WorkArea;
            var monitor = Microsoft.UI.Win32Interop.GetMonitorFromDisplayId(area.DisplayId);
            var dpi = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? (int)dpiX : 96;
            screens.Add(new ScreenArea(new PixelRect(work.X, work.Y, work.Width, work.Height), dpi, area.IsPrimary));
        }

        return screens;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForWindow(IntPtr window);

    /// <summary>MDT_EFFECTIVE_DPI (0): the DPI the screen's scaling setting gives.</summary>
    [DllImport("shcore.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Effective pixels in the physical pixels the window APIs take.</summary>
    private SizeInt32 Scaled(int width, int height)
    {
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
        return new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    /// <summary>Mica when supported, Desktop Acrylic as fallback, otherwise the default solid background.</summary>
    private static SystemBackdrop? CreateBackdrop()
    {
        if (MicaController.IsSupported())
        {
            return new MicaBackdrop { Kind = MicaKind.Base };
        }

        return DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop() : null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedSection) or nameof(ShellViewModel.SelectedPlaylistId))
        {
            SyncSelection(ViewModel.SelectedSection);
        }
        else if (e.PropertyName == nameof(ShellViewModel.VisibleSections))
        {
            ApplyNavigationLayout();
        }
    }

    /// <summary>
    /// Shows the sections the user keeps in the navigation pane, in their order (#255). A hidden section's
    /// page stays reachable by its links and shortcuts; then no item is selected. Only the items that
    /// differ are moved, added or removed: the others stay in the pane (with their state, such as
    /// focus). The favourite playlists follow the sections, after a separator (#297).
    /// </summary>
    private void ApplyNavigationLayout()
    {
        var sections = ViewModel.VisibleSections;
        List<object> wanted = [.. sections.Where(_sectionItems.ContainsKey).Select(key => _sectionItems[key])];
        if (wanted.Count > 1 && sections[0] == PageKeys.Home)
        {
            wanted.Insert(1, _homeSeparator);
        }

        var playlists = _favouritePlaylists.Items;
        foreach (var id in _playlistItems.Keys.Where(id => !playlists.Any(p => p.Id == id)).ToList())
        {
            _playlistItems.Remove(id);
        }

        if (playlists.Count > 0)
        {
            wanted.Add(_playlistsSeparator);
            wanted.AddRange(playlists.Select(NavPlaylistItem));
        }

        var items = NavView.MenuItems;
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < items.Count && ReferenceEquals(items[i], wanted[i]))
            {
                continue;
            }

            items.Remove(wanted[i]);
            items.Insert(i, wanted[i]);
        }

        while (items.Count > wanted.Count)
        {
            items.RemoveAt(items.Count - 1);
        }

        SyncSelection(ViewModel.SelectedSection);
    }

    /// <summary>
    /// Selects the section's item; on another page nothing, except a favourite playlist's own item on its page.
    /// Only section items have a page key: a favourite playlist's tag is the playlist (#301). Settings is the
    /// title bar's button (#254).
    /// </summary>
    private void SyncSelection(string? pageKey)
    {
        SettingsButton.IsChecked = pageKey == PageKeys.Settings;
        NavView.SelectedItem = pageKey is not null
            ? NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => item.Tag is string key && key == pageKey)
            : ViewModel.SelectedPlaylistId is { } playlistId && _playlistItems.TryGetValue(playlistId, out var playlist) ? playlist : null;
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is Playlist playlist)
        {
            _navigation.NavigateTo(PageKeys.Playlist, playlist.Id);
            return;
        }

        if (args.InvokedItemContainer?.Tag is string pageKey)
        {
            ViewModel.NavigateCommand.Execute(pageKey);
        }
    }

    /// <summary>A click toggles the button: it shows whether Settings is shown, also when it already was.</summary>
    private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateCommand.Execute(PageKeys.Settings);
        SyncSelection(ViewModel.SelectedSection);
    }

    /// <summary>
    /// A favourite playlist's item (#297): opens the playlist, takes dropped tracks and albums. Kept per
    /// playlist, so a rename only changes its name. It shows the cover, name and details (#299). It can be
    /// dragged to another place among the favourites, or moved with its menu (#306).
    /// </summary>
    private NavigationViewItem NavPlaylistItem(Playlist playlist)
    {
        if (!_playlistItems.TryGetValue(playlist.Id, out var item))
        {
            item = new NavigationViewItem { AllowDrop = true, CanDrag = true, Content = new PlaylistNavItem() };
            AutomationProperties.SetAutomationId(item, $"NavPlaylist_{playlist.Id}");
            item.DragStarting += OnPlaylistNavDragStarting;
            item.DropCompleted += (_, _) => _movingPlaylistId = null;
            item.DragOver += OnPlaylistNavDragOver;
            item.Drop += OnPlaylistNavDrop;
            item.ContextRequested += OnPlaylistNavContextRequested;
            item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPlaylistNavPointerPressed), handledEventsToo: true);
            item.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPlaylistNavPointerMoved), handledEventsToo: true);
            item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => _playlistPressPoint = null), handledEventsToo: true);
            _playlistItems[playlist.Id] = item;
        }

        var details = PlaylistItem.DetailsText(playlist.TrackCount, playlist.Duration, _strings);
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
        var content = (PlaylistNavItem)item.Content;
        content.Show(playlist.Name, details, ImageRequest.Primary(playlist.Id, playlist.PrimaryImageTag, PlaylistNavItem.CoverSize, scale));
        item.Tag = playlist;
        AutomationProperties.SetName(item, playlist.Name);
        AutomationProperties.SetHelpText(item, details);
        ToolTipService.SetToolTip(item, playlist.Name);
        ShowPlaylistIcon(item);
        return item;
    }

    /// <summary>The cover goes beside the name in the open pane, and is the icon in the closed compact pane.</summary>
    private void ShowPlaylistIcon(NavigationViewItem item) =>
        item.Icon = _paneOpen ? null : ((PlaylistNavItem)item.Content).Icon;

    private void OnPaneOpening(NavigationView sender, object args) => SetPaneOpen(true);

    private void OnPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => SetPaneOpen(false);

    /// <summary>Only the expanded mode starts open; compact and minimal open over the page later.</summary>
    private void OnDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) =>
        SetPaneOpen(args.DisplayMode == NavigationViewDisplayMode.Expanded);

    private void SetPaneOpen(bool open)
    {
        if (open == _paneOpen)
        {
            return;
        }

        _paneOpen = open;
        foreach (var item in _playlistItems.Values)
        {
            ShowPlaylistIcon(item);
        }
    }

    private void OnPlaylistNavPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint((UIElement)sender);
        _playlistPressPoint = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && point.Properties.IsLeftButtonPressed ? point.Position : null;
    }

    /// <summary>
    /// The item captures the pointer when it is pressed, for its pressed look, and then never sees a drag
    /// (#308): once the mouse has moved far enough, the capture goes and the drag starts here. Losing the
    /// capture also ends the press, so letting go does not open the playlist.
    /// </summary>
    private async void OnPlaylistNavPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_playlistPressPoint is not { } start || sender is not NavigationViewItem item)
        {
            return;
        }

        var point = e.GetCurrentPoint(item);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _playlistPressPoint = null;
            return;
        }

        if (Math.Abs(point.Position.X - start.X) < PlaylistDragThreshold && Math.Abs(point.Position.Y - start.Y) < PlaylistDragThreshold)
        {
            return;
        }

        _playlistPressPoint = null;
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            (element as UIElement)?.ReleasePointerCapture(e.Pointer);
            if (ReferenceEquals(element, item))
            {
                break;
            }
        }

        await item.StartDragAsync(point);
    }

    private void OnPlaylistNavDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: Playlist playlist })
        {
            args.Cancel = true;
            return;
        }

        _movingPlaylistId = playlist.Id;
        args.Data.SetData(PlaylistMoveFormat, playlist.Id);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    /// <summary>Tracks and albums are added to the playlist; another favourite moves above or below it (#306).</summary>
    private void OnPlaylistNavDragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Playlist playlist } item)
        {
            return;
        }

        if (e.DataView.Contains(PlaylistMoveFormat))
        {
            e.Handled = true;
            if (_movingPlaylistId is null || _movingPlaylistId == playlist.Id)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            var key = DropsBelow(item, e) ? "Drag_MoveBelow" : "Drag_MoveAbove";
            e.DragUIOverride.Caption = _strings.GetString(key).Replace("{0}", playlist.Name, StringComparison.Ordinal);
        }
        else if (e.DataView.Contains(TrackDrag.DataFormat))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = _strings.GetString("Drag_AddToPlaylist").Replace("{0}", playlist.Name, StringComparison.Ordinal);
            e.Handled = true;
        }
    }

    /// <summary>The lower half of a playlist's item puts the dragged one below it.</summary>
    private static bool DropsBelow(FrameworkElement item, DragEventArgs e) => e.GetPosition(item).Y > item.ActualHeight / 2;

    private async void OnPlaylistNavDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Playlist target } item && e.DataView.Contains(PlaylistMoveFormat))
        {
            e.Handled = true;
            if (_movingPlaylistId is { } moving)
            {
                _favouritePlaylists.MoveNextTo(moving, target.Id, DropsBelow(item, e));
            }

            _movingPlaylistId = null;
            return;
        }

        if (!e.DataView.Contains(TrackDrag.DataFormat) || sender is not FrameworkElement { Tag: Playlist playlist })
        {
            return;
        }

        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            await _trackDrag.DropOnPlaylistAsync(await TrackDragSource.TokenAsync(e.DataView), playlist);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Move up and Move down, for the keyboard and Narrator (#306); disabled at the ends.</summary>
    private void OnPlaylistNavContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Playlist playlist } item)
        {
            return;
        }

        var menu = new MenuFlyout();
        foreach (var (glyph, key, offset) in new[] { (UpGlyph, "Menu_MoveUp", -1), (DownGlyph, "Menu_MoveDown", 1) })
        {
            var entry = new MenuFlyoutItem
            {
                Text = _strings.GetString(key),
                Icon = new FontIcon { Glyph = glyph },
                IsEnabled = _favouritePlaylists.CanMove(playlist.Id, offset),
            };
            entry.Click += (_, _) =>
            {
                _favouritePlaylists.MoveBy(playlist.Id, offset);
                _ = DispatcherQueue.TryEnqueue(() => _playlistItems.GetValueOrDefault(playlist.Id)?.Focus(FocusState.Keyboard));
            };
            menu.Items.Add(entry);
        }

        ItemMenu.Show(menu, item, e);
    }

    private void OnTitleBarBackRequested(TitleBar sender, object args) => ViewModel.GoBackCommand.Execute(null);

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    /// <summary>Typing asks for suggestions; picking one from the list (which also changes the text) does not.</summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _ = Suggestions.UpdateAsync(sender.Text);
        }
    }

    /// <summary>Enter on a suggestion opens or plays it; Enter on the text shows the results page.</summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchSuggestion suggestion)
        {
            Suggestions.Choose(suggestion);
            return;
        }

        Suggestions.Cancel();
        ViewModel.SearchText = args.QueryText;
        if (ViewModel.SubmitSearchCommand.CanExecute(null))
        {
            ViewModel.SubmitSearchCommand.Execute(null);
        }
    }

    private void OnNotificationCloseButtonClick(InfoBar sender, object args)
    {
        if (sender.DataContext is AppNotification notification)
        {
            ViewModel.DismissNotificationCommand.Execute(notification);
        }
    }

    /// <summary>
    /// The app-wide shortcuts (<see cref="ShortcutService.Map"/>), before the focused control sees the key.
    /// Nothing fires while the user types in a text box, Space is left to controls that use it (buttons,
    /// list items), and held keys repeat only the volume.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Esc leaves the full-screen or mini player (popups such as menus handle their own Esc first).
        if (e.Key == VirtualKey.Escape && (Player.IsFullScreen || Player.IsMiniPlayer))
        {
            Player.ExitFullScreenCommand.Execute(null);
            Player.ExitMiniPlayerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // The keyboard's own Back and Forward keys, like the mouse buttons (#176).
        if (e.Key is VirtualKey.GoBack or VirtualKey.GoForward)
        {
            Execute(e.Key == VirtualKey.GoBack ? ViewModel.GoBackCommand : ViewModel.GoForwardCommand);
            e.Handled = true;
            return;
        }

        // The keys in effect, which the user may have changed in Settings (#68).
        var gesture = KeyboardInput.Gesture(e.Key);
        if (KeyboardInput.IsWindowsKeyDown || _shortcuts.Map.Find(gesture) is not { } action)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        if (focused is TextBox or PasswordBox or RichEditBox || (gesture == new KeyGesture("Space") && UsesSpace(focused)))
        {
            return;
        }

        e.Handled = true;
        if (e.KeyStatus.WasKeyDown && action is not (ShortcutAction.VolumeUp or ShortcutAction.VolumeDown))
        {
            return;
        }

        Run(action);
    }

    private void Run(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.PlayPause:
                Execute(Player.PlayPauseCommand);
                break;
            case ShortcutAction.Next:
                Execute(Player.NextCommand);
                break;
            case ShortcutAction.Previous:
                Execute(Player.PreviousCommand);
                break;
            case ShortcutAction.VolumeUp:
                Execute(Player.VolumeUpCommand);
                break;
            case ShortcutAction.VolumeDown:
                Execute(Player.VolumeDownCommand);
                break;
            case ShortcutAction.Shuffle:
                Execute(Player.ToggleShuffleCommand);
                break;
            case ShortcutAction.Repeat:
                Execute(Player.CycleRepeatCommand);
                break;
            case ShortcutAction.FullScreen:
                Player.ToggleFullScreenCommand.Execute(null);
                break;
            case ShortcutAction.MiniPlayer:
                Player.ToggleMiniPlayerCommand.Execute(null);
                break;
            case ShortcutAction.Search:
                SearchBox.Focus(FocusState.Keyboard);
                break;
            case ShortcutAction.PrivacyMode:
                Privacy.ToggleCommand.Execute(null);
                break;
            case ShortcutAction.Settings:
                ViewModel.NavigateCommand.Execute(PageKeys.Settings);
                break;
            case ShortcutAction.GoBack:
                Execute(ViewModel.GoBackCommand);
                break;
            case ShortcutAction.GoForward:
                Execute(ViewModel.GoForwardCommand);
                break;
            case ShortcutAction.Queue:
                // The panel becomes visible with the next layout pass; focus it once it is there.
                Queue.ToggleCommand.Execute(null);
                if (Queue.IsOpen)
                {
                    _ = DispatcherQueue.TryEnqueue(QueuePane.FocusPanel);
                }

                break;
            case ShortcutAction.Lyrics:
                Lyrics.ToggleCommand.Execute(null);
                if (Lyrics.IsOpen)
                {
                    _ = DispatcherQueue.TryEnqueue(LyricsPane.FocusPanel);
                }

                break;
        }
    }

    /// <summary>The mouse's back and forward buttons (XButton1, XButton2) go back and forward (#176).</summary>
    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(RootGrid).Properties;
        if (properties.IsXButton1Pressed || properties.IsXButton2Pressed)
        {
            Execute(properties.IsXButton1Pressed ? ViewModel.GoBackCommand : ViewModel.GoForwardCommand);
            e.Handled = true;
        }
    }

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    /// <summary>Controls that act on Space themselves: buttons, toggles, list and menu items, combo boxes.</summary>
    private static bool UsesSpace(object? focused) =>
        focused is ButtonBase or ToggleSwitch or SelectorItem or ComboBox or ItemContainer or NavigationViewItemBase or MenuFlyoutItemBase or BreadcrumbBarItem;
}
