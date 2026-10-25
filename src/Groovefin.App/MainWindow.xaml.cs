using System.ComponentModel;
using Groovefin.App.Controls;
using Groovefin.App.Services;
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

    private readonly FrameNavigationService _navigation;
    private readonly AddToPlaylist _addToPlaylist;
    private readonly TrackDrag _trackDrag;
    private readonly IStringResources _strings;
    private readonly ISettingsService _settings;
    private readonly ShortcutService _shortcuts;
    private readonly NotificationAreaIcon _tray;
    /// <summary>The navigation pane's section items by page key, and the separator after Home (#255).</summary>
    private readonly Dictionary<string, NavigationViewItem> _sectionItems;
    private readonly NavigationViewItemSeparator _homeSeparator;
    /// <summary>The normal window's presenter, set again when the mini or full-screen player closes.</summary>
    private readonly OverlappedPresenter? _windowPresenter;
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
        AddToPlaylist addToPlaylist,
        TrackDrag trackDrag,
        IStringResources strings,
        ISettingsService settings,
        ShortcutService shortcuts)
    {
        _navigation = navigation;
        _shortcuts = shortcuts;
        _addToPlaylist = addToPlaylist;
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

        _settings = settings;
        Title = new ResourceLoader().GetString("AppDisplayName");
        AppWindow.SetIcon(Path.Join(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SystemBackdrop = CreateBackdrop();
        _windowPresenter = AppWindow.Presenter as OverlappedPresenter;

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

        // The built-in Settings item has no x:Uid; its access key comes from the resources like the others.
        NavView.Loaded += (_, _) =>
        {
            if (NavView.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.AccessKey = _strings.GetString("Nav_SettingsAccessKey");
            }
        };

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
        if (e.PropertyName == nameof(ShellViewModel.SelectedSection))
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
    /// page stays reachable by its links and shortcuts; then no item is selected.
    /// </summary>
    private void ApplyNavigationLayout()
    {
        var sections = ViewModel.VisibleSections;
        NavView.MenuItems.Clear();
        foreach (var key in sections)
        {
            if (_sectionItems.TryGetValue(key, out var item))
            {
                NavView.MenuItems.Add(item);
            }
        }

        if (sections.Count > 1 && sections[0] == PageKeys.Home)
        {
            NavView.MenuItems.Insert(1, _homeSeparator);
        }

        SyncSelection(ViewModel.SelectedSection);
    }

    private void SyncSelection(string? pageKey)
    {
        if (pageKey == PageKeys.Settings)
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        NavView.SelectedItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == pageKey);
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is Playlist playlist)
        {
            _navigation.NavigateTo(PageKeys.Playlist, playlist.Id);
            return;
        }

        var pageKey = args.IsSettingsInvoked ? PageKeys.Settings : args.InvokedItemContainer?.Tag as string;
        if (pageKey is not null)
        {
            ViewModel.NavigateCommand.Execute(pageKey);
        }
    }

    /// <summary>Loads the playlists under the Playlists item each time it opens, so new and renamed ones show.</summary>
    private async void OnNavigationExpanding(NavigationView sender, NavigationViewItemExpandingEventArgs args)
    {
        if (args.ExpandingItemContainer != PlaylistsNavItem)
        {
            return;
        }

        var playlists = await _addToPlaylist.GetTargetsAsync();
        PlaylistsNavItem.MenuItems.Clear();
        foreach (var playlist in playlists)
        {
            var item = new NavigationViewItem { Content = playlist.Name, Tag = playlist, AllowDrop = true, Icon = new FontIcon { Glyph = "\uEC4F" } };
            item.DragOver += OnPlaylistNavDragOver;
            item.Drop += OnPlaylistNavDrop;
            PlaylistsNavItem.MenuItems.Add(item);
        }
    }

    /// <summary>Dragging tracks over Playlists opens it, so they can be dropped on a playlist.</summary>
    private void OnPlaylistsNavDragEnter(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(TrackDrag.DataFormat))
        {
            PlaylistsNavItem.IsExpanded = true;
        }
    }

    private void OnPlaylistNavDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(TrackDrag.DataFormat) && sender is FrameworkElement { Tag: Playlist playlist })
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = _strings.GetString("Drag_AddToPlaylist").Replace("{0}", playlist.Name, StringComparison.Ordinal);
            e.Handled = true;
        }
    }

    private async void OnPlaylistNavDrop(object sender, DragEventArgs e)
    {
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
