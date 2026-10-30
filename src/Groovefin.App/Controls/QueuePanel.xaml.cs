using System.ComponentModel;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Groovefin.App.Controls;

/// <summary>
/// The queue on the right of the window: the playing entry, up next (click plays, drag or the context
/// menu reorders, the context menu removes) and history. Opened with Ctrl+Q or the now playing bar.
/// Tracks and albums dragged onto the panel are inserted into up next where they are dropped.
/// </summary>
internal sealed partial class QueuePanel : UserControl
{
    private const string PlayGlyph = "";
    private const string UpGlyph = "";
    private const string DownGlyph = "";
    private const string RemoveGlyph = "";

    private readonly IStringResources _strings;

    public QueuePanel()
    {
        ViewModel = App.Services.GetRequiredService<QueueViewModel>();
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();

        // Also when the up next list handled the event first (it reorders its own rows).
        PanelRoot.AddHandler(DragOverEvent, new DragEventHandler(OnTrackDragOver), true);
        PanelRoot.AddHandler(DropEvent, new DragEventHandler(OnTrackDrop), true);

        Loaded += OnLoaded;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public QueueViewModel ViewModel { get; }

    /// <summary>Moves keyboard focus into the panel, e.g. after Ctrl+Q opened it.</summary>
    public void FocusPanel()
    {
        var list = IsHistorySelected ? HistoryList : UpNextList;
        if (!list.Focus(FocusState.Keyboard))
        {
            CloseButton.Focus(FocusState.Keyboard);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateSections();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QueueViewModel.HasUpcoming) or nameof(QueueViewModel.HasHistory))
        {
            UpdateSections();
        }
    }

    private void OnSectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => UpdateSections();

    /// <summary>
    /// Whether the History tab is selected, from the bar's selected item: in
    /// <see cref="SelectorBar.SelectionChanged"/> the items' <c>IsSelected</c> still has the old values (#202).
    /// </summary>
    private bool IsHistorySelected => ReferenceEquals(Sections.SelectedItem, HistorySection);

    /// <summary>Shows the list of the selected section, or its empty text.</summary>
    private void UpdateSections()
    {
        var history = IsHistorySelected;
        UpNextList.Visibility = Show(!history && ViewModel.HasUpcoming);
        UpNextEmpty.Visibility = Show(!history && !ViewModel.HasUpcoming);
        HistoryList.Visibility = Show(history && ViewModel.HasHistory);
        HistoryEmpty.Visibility = Show(history && !ViewModel.HasHistory);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnUpNextClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is QueueEntry entry)
        {
            ViewModel.PlayCommand.Execute(entry);
        }
    }

    private void OnHistoryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is QueueEntry entry)
        {
            ViewModel.PlayAgainCommand.Execute(entry);
        }
    }

    /// <summary>The list already moved the row; the queue follows.</summary>
    private void OnUpNextDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.Move && args.Items.Count == 1 && args.Items[0] is QueueEntry entry)
        {
            ViewModel.MoveUpcoming(entry, ViewModel.Upcoming.IndexOf(entry));
        }
    }

    private void OnTrackDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(TrackDrag.DataFormat))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = _strings.GetString("Drag_AddToQueue");
        }
    }

    private async void OnTrackDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(TrackDrag.DataFormat))
        {
            return;
        }

        var position = DropPosition(e.GetPosition(UpNextList));
        var deferral = e.GetDeferral();
        try
        {
            await App.Services.GetRequiredService<TrackDrag>().DropOnQueueAsync(await TrackDragSource.TokenAsync(e.DataView), position);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>The up next position under <paramref name="point"/> (list coordinates): before the row whose upper half it is in, else last.</summary>
    private int DropPosition(Point point)
    {
        if (UpNextList.Visibility != Visibility.Visible)
        {
            return ViewModel.Upcoming.Count;
        }

        for (var i = 0; i < ViewModel.Upcoming.Count; i++)
        {
            if (UpNextList.ContainerFromIndex(i) is FrameworkElement row)
            {
                var top = row.TransformToVisual(UpNextList).TransformPoint(default).Y;
                if (point.Y < top + (row.ActualHeight / 2))
                {
                    return i;
                }
            }
        }

        return ViewModel.Upcoming.Count;
    }

    private void OnCurrentContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (ViewModel.Current is { } entry)
        {
            ShowMenu(e, (RemoveGlyph, "Menu_RemoveFromQueue", () => ViewModel.RemoveCommand.Execute(entry)));
        }
    }

    private void OnUpNextContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source || ItemMenu.ItemOf(source) is not QueueEntry entry)
        {
            return;
        }

        var index = ViewModel.Upcoming.IndexOf(entry);
        var actions = new List<(string, string, Action)> { (PlayGlyph, "Menu_Play", () => ViewModel.PlayCommand.Execute(entry)) };
        if (index > 0)
        {
            actions.Add((UpGlyph, "Menu_MoveUp", () => ViewModel.MoveUpCommand.Execute(entry)));
        }

        if (index < ViewModel.Upcoming.Count - 1)
        {
            actions.Add((DownGlyph, "Menu_MoveDown", () => ViewModel.MoveDownCommand.Execute(entry)));
        }

        actions.Add((RemoveGlyph, "Menu_RemoveFromQueue", () => ViewModel.RemoveCommand.Execute(entry)));
        ShowMenu(e, [.. actions]);
    }

    private void OnHistoryContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement source && ItemMenu.ItemOf(source) is QueueEntry entry)
        {
            ShowMenu(e, (PlayGlyph, "Menu_PlayAgain", () => ViewModel.PlayAgainCommand.Execute(entry)));
        }
    }

    /// <summary>Shows a menu at the pointer, or at the element for the Menu key and Shift+F10.</summary>
    private void ShowMenu(ContextRequestedEventArgs e, params (string Glyph, string TextKey, Action Invoke)[] actions)
    {
        if (e.OriginalSource is not FrameworkElement target)
        {
            return;
        }

        var menu = new MenuFlyout();
        foreach (var (glyph, textKey, invoke) in actions)
        {
            var item = new MenuFlyoutItem { Text = _strings.GetString(textKey), Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => invoke();
            menu.Items.Add(item);
        }

        if (e.TryGetPosition(target, out var point))
        {
            menu.ShowAt(target, point);
        }
        else
        {
            menu.ShowAt(target);
        }

        e.Handled = true;
    }
}
