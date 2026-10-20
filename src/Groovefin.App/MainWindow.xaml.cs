using System.ComponentModel;
using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Shortcuts;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Groovefin.App;

/// <summary>
/// Shell window: Mica backdrop, title bar with search, navigation pane and content frame.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private readonly FrameNavigationService _navigation;
    private readonly AddToPlaylist _addToPlaylist;
    private readonly TrackDrag _trackDrag;
    private readonly IStringResources _strings;

    public MainWindow(
        ShellViewModel viewModel,
        FrameNavigationService navigation,
        ThemeService themeService,
        ContentDialogService dialogs,
        QueueViewModel queue,
        SearchSuggestionsViewModel suggestions,
        LyricsViewModel lyrics,
        NowPlayingViewModel nowPlaying,
        AddToPlaylist addToPlaylist,
        TrackDrag trackDrag,
        IStringResources strings)
    {
        _navigation = navigation;
        _addToPlaylist = addToPlaylist;
        _trackDrag = trackDrag;
        _strings = strings;
        ViewModel = viewModel;
        Queue = queue;
        Lyrics = lyrics;
        Player = nowPlaying;
        Suggestions = suggestions;
        InitializeComponent();

        Title = new ResourceLoader().GetString("AppDisplayName");
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SystemBackdrop = CreateBackdrop();

        navigation.Initialize(ContentFrame);
        themeService.Attach(this);
        RootGrid.Loaded += (_, _) => dialogs.Attach(RootGrid.XamlRoot);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // The full-screen player puts the window into full screen; leaving it by other means closes the player.
        Player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NowPlayingViewModel.IsFullScreen))
            {
                AppWindow.SetPresenter(Player.IsFullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
            }
        };
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPresenterChange)
            {
                Player.IsFullScreen = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
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
    }

    public ShellViewModel ViewModel { get; }

    public QueueViewModel Queue { get; }

    public SearchSuggestionsViewModel Suggestions { get; }

    public LyricsViewModel Lyrics { get; }

    /// <summary>Gets the now playing state, also shown by the full-screen player. (The bar itself is named NowPlaying.)</summary>
    public NowPlayingViewModel Player { get; }

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
    /// The app-wide shortcuts (<see cref="KeyboardShortcuts"/>), before the focused control sees the key.
    /// Nothing fires while the user types in a text box, Space is left to controls that use it (buttons,
    /// list items), and held keys repeat only the volume.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Esc leaves the full-screen player (popups such as menus handle their own Esc first).
        if (e.Key == VirtualKey.Escape && Player.IsFullScreen)
        {
            Player.ExitFullScreenCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (IsDown(VirtualKey.Shift) || IsDown(VirtualKey.Menu) || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)
            || KeyboardShortcuts.Find(e.Key.ToString(), IsDown(VirtualKey.Control)) is not { } action)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        if (focused is TextBox or PasswordBox or RichEditBox || (action == ShortcutAction.PlayPause && UsesSpace(focused)))
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
            case ShortcutAction.Search:
                SearchBox.Focus(FocusState.Keyboard);
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

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>Controls that act on Space themselves: buttons, toggles, list and menu items, combo boxes.</summary>
    private static bool UsesSpace(object? focused) =>
        focused is ButtonBase or ToggleSwitch or SelectorItem or ComboBox or ItemContainer or NavigationViewItemBase or MenuFlyoutItemBase or BreadcrumbBarItem;
}
