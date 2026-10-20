using System.ComponentModel;
using System.Globalization;
using Groovefin.App.Controls;
using Groovefin.Core.Menus;
using Groovefin.Core.Models;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace Groovefin.App.Views;

/// <summary>
/// All songs in a virtualized table. The next page loads when the table scrolls near the end. A header
/// click sorts; dragging a header moves the column and dragging its right edge resizes it. The header's
/// context menu and the Columns button do the same from the keyboard.
/// </summary>
internal sealed partial class SongsPage : Page
{
    /// <summary>Load the next page when less than this many pixels are left below the viewport.</summary>
    private const double LoadMoreThreshold = 800;

    /// <summary>How far a pressed header must move sideways before it is dragged instead of clicked.</summary>
    private const double DragThreshold = 8;

    private readonly IStringResources _strings;
    private readonly List<Button> _headerButtons = [];
    private IReadOnlyList<SongColumnViewModel> _headerColumns = [];
    private ScrollViewer? _scroll;
    private bool _initialized;

    private SongColumnViewModel? _pressedColumn;
    private Point _pressedAt;
    private SongColumnViewModel? _draggedColumn;
    private double _resizeStartWidth;

    public SongsPage()
    {
        ViewModel = App.CreateViewModel<SongsViewModel>();
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.LayoutChanged += (_, _) => RefreshHeader();
        RefreshHeader();
    }

    public SongsViewModel ViewModel { get; }

    private Style CellStyle => (Style)Resources["SongCellStyle"];

    private Style SecondaryCellStyle => (Style)Resources["SongSecondaryCellStyle"];

    /// <summary>
    /// The page is cached: going back returns to the same table and scroll position. Opening it from the
    /// navigation pane reloads it, as the server or music library may have changed.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Loaded -= OnFirstLoaded;
        if (!_initialized)
        {
            Loaded += OnFirstLoaded;
        }
        else if (e.NavigationMode != NavigationMode.Back)
        {
            _ = ViewModel.ReloadAsync();
        }
    }

    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _initialized = true;

        _scroll = FindDescendant<ScrollViewer>(SongsList);
        if (_scroll is not null)
        {
            _scroll.ViewChanged += (_, _) => LoadMoreIfNeeded();
        }

        await ViewModel.InitializeAsync();
        LoadMoreIfNeeded();
    }

    private void LoadMoreIfNeeded()
    {
        if (_scroll is null)
        {
            return;
        }

        var remaining = _scroll.ScrollableHeight - _scroll.VerticalOffset;
        if (remaining < LoadMoreThreshold && ViewModel.LoadMoreCommand.CanExecute(null))
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SongsViewModel.TotalCount):
                CountText.Text = ViewModel.TotalCount > 0
                    ? _strings.GetString("Songs_Count").Replace("{0}", ViewModel.TotalCount.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                    : string.Empty;
                break;
            case nameof(SongsViewModel.IsLoading) when !ViewModel.IsLoading:
                // The first pages may not fill a tall window.
                DispatcherQueue.TryEnqueue(LoadMoreIfNeeded);
                break;
            case nameof(SongsViewModel.SortBy) or nameof(SongsViewModel.Descending):
                RefreshHeader();
                break;
        }
    }

    // ---- Header ----

    /// <summary>Rebuilds the header cells when columns were shown, hidden or moved; otherwise updates widths and sort marks.</summary>
    private void RefreshHeader()
    {
        var columns = ViewModel.VisibleColumns;
        if (!columns.SequenceEqual(_headerColumns))
        {
            _headerColumns = columns;
            HeaderRow.ColumnDefinitions.Clear();
            HeaderRow.Children.Clear();
            _headerButtons.Clear();
            for (var i = 0; i < columns.Count; i++)
            {
                HeaderRow.ColumnDefinitions.Add(new ColumnDefinition());
                var button = HeaderButton(columns[i]);
                var grip = new ColumnResizeGrip { Style = (Style)Resources["ColumnGripStyle"], Tag = columns[i] };
                grip.ResizeStarted += OnGripResizeStarted;
                grip.Resizing += OnGripResizing;
                grip.ResizeCompleted += OnGripResizeCompleted;
                Grid.SetColumn(button, i);
                Grid.SetColumn(grip, i);
                HeaderRow.Children.Add(button);
                HeaderRow.Children.Add(grip);
                _headerButtons.Add(button);
            }
        }

        for (var i = 0; i < columns.Count; i++)
        {
            HeaderRow.ColumnDefinitions[i].Width = new GridLength(columns[i].Width);
            ShowHeader(_headerButtons[i], columns[i]);
        }
    }

    private Button HeaderButton(SongColumnViewModel column)
    {
        var button = new Button
        {
            Style = (Style)Resources["ColumnHeaderButtonStyle"],
            HorizontalContentAlignment = column.IsNumeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Command = ViewModel.SortCommand,
            CommandParameter = column,
            Tag = column,
        };
        var menu = new MenuFlyout();
        menu.Opening += (_, _) => FillColumnMenu(menu, column);
        button.ContextFlyout = menu;

        // The button handles pointer presses itself, so listen to handled events as well.
        button.AddHandler(PointerPressedEvent, new PointerEventHandler(OnHeaderPointerPressed), true);
        button.AddHandler(PointerMovedEvent, new PointerEventHandler(OnHeaderPointerMoved), true);
        button.DragStarting += OnHeaderDragStarting;
        button.DropCompleted += (_, _) => _draggedColumn = null;
        return button;
    }

    /// <summary>Header text, the sort arrow and the name Narrator reads, e.g. "Album, sorted ascending".</summary>
    private void ShowHeader(Button button, SongColumnViewModel column)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(new TextBlock { Text = column.Header, Style = CellStyle, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var name = ColumnName(column);
        if (column.SortDescending is { } descending)
        {
            content.Children.Add(new FontIcon { Glyph = descending ? "" : "", FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            name = _strings.GetString(descending ? "Songs_SortedDescending" : "Songs_SortedAscending").Replace("{0}", name, StringComparison.Ordinal);
        }

        button.Content = content;
        AutomationProperties.SetName(button, name);
    }

    /// <summary>The column's spoken name; "#" is read as "Track number".</summary>
    private string ColumnName(SongColumnViewModel column) =>
        column.Column == SongColumn.TrackNumber ? _strings.GetString("Songs_TrackNumberName") : column.Header;

    /// <summary>Moving and sizing <paramref name="column"/> (when given), then every column to show or hide, then reset.</summary>
    private void FillColumnMenu(MenuFlyout menu, SongColumnViewModel? column)
    {
        menu.Items.Clear();
        if (column is not null)
        {
            menu.Items.Add(MenuItem("Songs_MoveLeft", "", ViewModel.MoveColumnLeftCommand, column));
            menu.Items.Add(MenuItem("Songs_MoveRight", "", ViewModel.MoveColumnRightCommand, column));
            menu.Items.Add(MenuItem("Songs_Widen", "", ViewModel.WidenColumnCommand, column));
            menu.Items.Add(MenuItem("Songs_Narrow", "", ViewModel.NarrowColumnCommand, column));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (var c in ViewModel.Columns)
        {
            menu.Items.Add(new ToggleMenuFlyoutItem
            {
                Text = ColumnName(c),
                IsChecked = c.IsVisible,
                IsEnabled = c.CanHide,
                Command = ViewModel.ToggleColumnCommand,
                CommandParameter = c,
            });
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("Songs_ResetColumns", "", ViewModel.ResetColumnsCommand, null));
    }

    private MenuFlyoutItem MenuItem(string textKey, string glyph, System.Windows.Input.ICommand command, object? parameter) =>
        new() { Text = _strings.GetString(textKey), Icon = new FontIcon { Glyph = glyph }, Command = command, CommandParameter = parameter };

    private void OnColumnsMenuOpening(object sender, object e) => FillColumnMenu(ColumnsMenu, null);

    private void OnGripResizeStarted(object? sender, EventArgs e) =>
        _resizeStartWidth = ((SongColumnViewModel)((FrameworkElement)sender!).Tag).Width;

    private void OnGripResizing(object? sender, double offset) =>
        ViewModel.ResizeColumn((SongColumnViewModel)((FrameworkElement)sender!).Tag, _resizeStartWidth + offset);

    private void OnGripResizeCompleted(object? sender, EventArgs e) => ViewModel.CommitLayout();

    private void OnHeaderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressedColumn = (SongColumnViewModel)((FrameworkElement)sender).Tag;
        _pressedAt = e.GetCurrentPoint(HeaderRow).Position;
    }

    private async void OnHeaderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(HeaderRow);
        if (_pressedColumn is null || !point.IsInContact)
        {
            _pressedColumn = null;
            return;
        }

        if (Math.Abs(point.Position.X - _pressedAt.X) < DragThreshold)
        {
            return;
        }

        _draggedColumn = _pressedColumn;
        _pressedColumn = null;
        var element = (UIElement)sender;
        await element.StartDragAsync(e.GetCurrentPoint(element));
    }

    private void OnHeaderDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (_draggedColumn is null)
        {
            args.Cancel = true;
            return;
        }

        args.Data.SetText(_draggedColumn.Header);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnHeaderDragOver(object sender, DragEventArgs e)
    {
        if (_draggedColumn is not null)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
        }
    }

    private void OnHeaderDrop(object sender, DragEventArgs e)
    {
        if (_draggedColumn is { } dragged && ColumnAt(e.GetPosition(HeaderRow).X) is { } target)
        {
            ViewModel.MoveColumn(dragged, ViewModel.Columns.IndexOf(target));
        }

        _draggedColumn = null;
    }

    /// <summary>The visible column under <paramref name="x"/> (header coordinates); the last one past the end.</summary>
    private SongColumnViewModel? ColumnAt(double x)
    {
        var columns = ViewModel.VisibleColumns;
        var right = 0d;
        foreach (var column in columns)
        {
            right += column.Width;
            if (x < right)
            {
                return column;
            }
        }

        return columns.Count > 0 ? columns[^1] : null;
    }

    // ---- Rows ----

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is SongRow song && args.ItemContainer.ContentTemplateRoot is SongTableRow row)
        {
            row.Show(ViewModel, song, CellStyle, SecondaryCellStyle);
            AutomationProperties.SetName(args.ItemContainer, song.AccessibleName);
        }

        args.Handled = true;
    }

    private void OnSongsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is SongRow row)
        {
            ViewModel.PlayCommand.Execute(row);
            e.Handled = true;
        }
    }

    private void OnSongsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && FocusManager.GetFocusedElement(XamlRoot) is ListViewItem { Content: SongRow row })
        {
            ViewModel.PlayCommand.Execute(row);
            e.Handled = true;
        }
    }

    /// <summary>The menu of the selected songs; a song outside the selection becomes the selection first.</summary>
    private void OnSongsContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: SongRow row } target)
        {
            return;
        }

        if (!SongsList.SelectedItems.Contains(row))
        {
            SongsList.SelectedItems.Clear();
            SongsList.SelectedItems.Add(row);
        }

        var menu = new MenuFlyout();
        ItemMenu.AddTo(menu, App.Services.GetRequiredService<ContextMenus>().ForTracks(SelectedTracks()));
        ItemMenu.Show(menu, target, e);
    }

    /// <summary>The selected songs in table order; ranges keep this fast when everything is selected.</summary>
    private List<Track> SelectedTracks()
    {
        var tracks = new List<Track>();
        foreach (var range in SongsList.SelectedRanges.OrderBy(r => r.FirstIndex))
        {
            for (var i = range.FirstIndex; i <= range.LastIndex && i < ViewModel.Songs.Count; i++)
            {
                tracks.Add(ViewModel.Songs[i].Track);
            }
        }

        return tracks;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (node is T match)
            {
                return match;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                pending.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
        }

        return null;
    }
}
