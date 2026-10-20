using System.ComponentModel;
using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;

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

    /// <summary>Ctrl+L opens the lyrics pane and moves focus into it, or closes it.</summary>
    private void OnToggleLyricsInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Lyrics.ToggleCommand.Execute(null);
        if (Lyrics.IsOpen)
        {
            _ = DispatcherQueue.TryEnqueue(LyricsPane.FocusPanel);
        }

        args.Handled = true;
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

    private void OnFocusSearchInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    /// <summary>Ctrl+Q opens the queue panel and moves focus into it, or closes it.</summary>
    private void OnToggleQueueInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Queue.ToggleCommand.Execute(null);
        if (Queue.IsOpen)
        {
            // The panel becomes visible with the next layout pass; focus it once it is there.
            _ = DispatcherQueue.TryEnqueue(QueuePane.FocusPanel);
        }

        args.Handled = true;
    }
}
