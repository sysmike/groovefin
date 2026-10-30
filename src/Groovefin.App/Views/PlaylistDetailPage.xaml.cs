using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.Menus;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace Groovefin.App.Views;

/// <summary>One playlist: header with actions and the editable entries. The navigation parameter is the playlist ID.</summary>
internal sealed partial class PlaylistDetailPage : Page
{
    private const string PlayGlyph = "";
    private const string UpGlyph = "";
    private const string DownGlyph = "";
    private const string RemoveGlyph = "";

    private readonly CancellationTokenSource _cancellation = new();
    private readonly IStringResources _strings;
    private string? _playlistId;

    public PlaylistDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<PlaylistDetailViewModel>();
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public PlaylistDetailViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _playlistId = (e.Parameter as PageNavigationArgs)?.Parameter as string;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (_playlistId is null)
        {
            return;
        }

        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync(_playlistId, _cancellation.Token);
    }

    private void OnEntryClick(object sender, ItemClickEventArgs e)
    {
        // Ctrl+click and Shift+click select (#295).
        if (!TrackSelection.IsSelecting && e.ClickedItem is PlaylistEntryItem item)
        {
            ViewModel.PlayEntryCommand.Execute(item);
        }
    }

    private void OnEntryFavouriteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PlaylistEntryItem item)
        {
            ViewModel.ToggleEntryFavouriteCommand.Execute(item);
        }
    }

    /// <summary>
    /// Several selected entries are only dragged out (to the queue or another playlist), not reordered: the
    /// server moves one entry at a time (#295).
    /// </summary>
    private void OnEntriesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var canReorder = ViewModel.CanReorder && EntriesList.SelectedItems.Count <= 1;
        EntriesList.CanReorderItems = canReorder;
        EntriesList.AllowDrop = canReorder;
    }

    /// <summary>The list already moved the row; the server follows.</summary>
    private async void OnEntriesDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.Move && args.Items.Count == 1 && args.Items[0] is PlaylistEntryItem item)
        {
            await ViewModel.MoveEntryAsync(item, ViewModel.Entries.IndexOf(item));
        }
    }

    private void OnEntryContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement target || ItemMenu.ItemOf(target) is not PlaylistEntryItem item)
        {
            return;
        }

        var menu = new MenuFlyout();
        var selected = TrackSelection.ForMenu(EntriesList, item).OfType<PlaylistEntryItem>().ToList();
        if (selected.Count > 1)
        {
            // Several entries (#295): removing them, then what several tracks offer. Moves are for one.
            Add(menu, RemoveGlyph, "Menu_RemoveFromPlaylist", () => _ = ViewModel.RemoveEntriesAsync(selected));
            menu.Items.Add(new MenuFlyoutSeparator());
            ItemMenu.AddTo(menu, App.Services.GetRequiredService<ContextMenus>().ForTracks([.. selected.Select(s => s.Entry.Track)]));
            ItemMenu.Show(menu, target, e);
            return;
        }

        var index = ViewModel.Entries.IndexOf(item);
        Add(menu, PlayGlyph, "Menu_Play", () => ViewModel.PlayEntryCommand.Execute(item));
        if (ViewModel.CanReorder && index > 0)
        {
            Add(menu, UpGlyph, "Menu_MoveUp", () => ViewModel.MoveUpCommand.Execute(item));
        }

        if (ViewModel.CanReorder && index < ViewModel.Entries.Count - 1)
        {
            Add(menu, DownGlyph, "Menu_MoveDown", () => ViewModel.MoveDownCommand.Execute(item));
        }

        Add(menu, RemoveGlyph, "Menu_RemoveFromPlaylist", () => ViewModel.RemoveCommand.Execute(item));

        // Then what every track offers, except playing it alone: here "Play" plays from this entry.
        if (ItemMenu.ActionsFor(item) is { } shared)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            ItemMenu.AddTo(menu, [.. shared.Where(a => a.Kind != MenuActionKind.PlayNow)]);
        }

        ItemMenu.Show(menu, target, e);
    }

    private void Add(MenuFlyout menu, string glyph, string textKey, Action invoke)
    {
        var item = new MenuFlyoutItem { Text = _strings.GetString(textKey), Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => invoke();
        menu.Items.Add(item);
    }
}
