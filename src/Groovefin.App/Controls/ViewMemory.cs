using Groovefin.Core.Navigation;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Groovefin.App.Controls;

// Public: XAML sets these properties at run time and generates no setter for an internal class (#140).
/// <summary>
/// Marks what a page keeps when it is left and gets back on Back and Forward (#284):
/// <c>&lt;ScrollViewer controls:ViewMemory.IsEnabled="True"&gt;</c>. A <see cref="ScrollViewer"/>, a list
/// (<see cref="ListViewBase"/>) or an <see cref="ItemsView"/> keeps its scroll position, a list also the
/// item that had focus, and a <see cref="SelectorBar"/> its tab. The navigation service captures the marked
/// elements when the page is left and restores them, in the same order, once the page is back: the scroll
/// position as the content loads (<see cref="PendingScroll"/>), then the focus.
/// </summary>
public static class ViewMemory
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ViewMemory), new PropertyMetadata(false));

    /// <summary>How often a restore checks whether the content has grown.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long the item to focus may take to appear once the position is back.</summary>
    private static readonly TimeSpan FocusTimeout = TimeSpan.FromSeconds(2);

    public static bool GetIsEnabled(UIElement element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(UIElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>The state of the page's marked elements, in the page's order.</summary>
    public static ViewState Capture(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var focused = page.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as DependencyObject : null;
        return new ViewState([.. Marked(page).Select(element => CaptureElement(element, focused))]);
    }

    /// <summary>Puts the state back once the page is loaded; nothing happens if the page now marks other elements.</summary>
    public static void Restore(Page page, ViewState state)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(state);
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            page.Loaded -= OnLoaded;
            var elements = Marked(page).ToList();
            if (elements.Count != state.Elements.Count)
            {
                return;
            }

            for (var i = 0; i < elements.Count; i++)
            {
                RestoreElement(page, elements[i], state.Elements[i]);
            }
        }

        page.Loaded += OnLoaded;
    }

    private static ElementState CaptureElement(UIElement element, DependencyObject? focused)
    {
        switch (element)
        {
            case SelectorBar bar:
                var tab = bar.SelectedItem is { } selected ? bar.Items.IndexOf(selected) : -1;
                return new ElementState { SelectedIndex = tab >= 0 ? tab : null };
            case ListViewBase or ItemsView:
                var index = FocusedIndex(element, focused);
                return new ElementState
                {
                    VerticalOffset = Scroller.Find(element)?.Offset ?? 0,
                    FocusedIndex = index,
                    FocusedByKeyboard = index is not null && focused is UIElement { FocusState: FocusState.Keyboard },
                };
            default:
                return new ElementState { VerticalOffset = Scroller.Find(element)?.Offset ?? 0 };
        }
    }

    private static void RestoreElement(Page page, UIElement element, ElementState state)
    {
        if (element is SelectorBar bar)
        {
            if (state.SelectedIndex is { } tab && tab < bar.Items.Count)
            {
                bar.SelectedItem = bar.Items[tab];
            }

            return;
        }

        var pending = new PendingScroll(state.VerticalOffset);
        if (pending.IsDone && state.FocusedIndex is null)
        {
            return;
        }

        // Scrolling by the user ends it; the page's own scrolling does not raise these.
        void OnInput(object sender, RoutedEventArgs e) => pending.Cancel();
        var onPointer = new PointerEventHandler(OnInput);
        var onKey = new KeyEventHandler(OnInput);
        element.AddHandler(UIElement.PointerWheelChangedEvent, onPointer, true);
        element.AddHandler(UIElement.PointerPressedEvent, onPointer, true);
        element.AddHandler(UIElement.KeyDownEvent, onKey, true);

        var timer = page.DispatcherQueue.CreateTimer();
        timer.Interval = Interval;
        var focusUntil = DateTimeOffset.MinValue;
        void Stop()
        {
            timer.Stop();
            element.RemoveHandler(UIElement.PointerWheelChangedEvent, onPointer);
            element.RemoveHandler(UIElement.PointerPressedEvent, onPointer);
            element.RemoveHandler(UIElement.KeyDownEvent, onKey);
        }

        void OnTick(DispatcherQueueTimer sender, object args)
        {
            if (!page.IsLoaded)
            {
                Stop();
                return;
            }

            if (!pending.IsDone && Scroller.Find(element) is { } scroller && pending.Next(scroller.ScrollableHeight) is { } offset)
            {
                scroller.ScrollTo(offset);
            }

            if (!pending.IsDone)
            {
                return;
            }

            // The position is back (or the user took over): give the item its focus back, once it exists.
            if (!pending.Reached || state.FocusedIndex is not { } index)
            {
                Stop();
                return;
            }

            if (focusUntil == DateTimeOffset.MinValue)
            {
                focusUntil = DateTimeOffset.UtcNow + FocusTimeout;
            }

            if (ItemAt(element, index) is { } item)
            {
                item.Focus(state.FocusedByKeyboard ? FocusState.Keyboard : FocusState.Programmatic);
                Stop();
            }
            else if (DateTimeOffset.UtcNow > focusUntil)
            {
                Stop();
            }
        }

        timer.Tick += OnTick;
        timer.Start();
    }

    /// <summary>The marked elements in tree order; the inside of a marked element is not searched.</summary>
    private static IEnumerable<UIElement> Marked(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement element && GetIsEnabled(element))
            {
                yield return element;
                continue;
            }

            foreach (var marked in Marked(child))
            {
                yield return marked;
            }
        }
    }

    /// <summary>The index of the list's item that holds the focused element, or <see langword="null"/>.</summary>
    private static int? FocusedIndex(UIElement list, DependencyObject? focused)
    {
        for (var node = focused; node is not null && !ReferenceEquals(node, list); node = VisualTreeHelper.GetParent(node))
        {
            if (list is ListViewBase listView && node is SelectorItem container
                && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(container), listView))
            {
                var index = listView.IndexFromContainer(container);
                return index >= 0 ? index : null;
            }

            if (list is ItemsView && node is ItemContainer tile && VisualTreeHelper.GetParent(tile) is ItemsRepeater repeater)
            {
                var index = repeater.GetElementIndex(tile);
                return index >= 0 ? index : null;
            }
        }

        return null;
    }

    /// <summary>The list's item at <paramref name="index"/> if it exists (virtualized lists create only those near the view).</summary>
    private static Control? ItemAt(UIElement list, int index) => list switch
    {
        ListViewBase listView => listView.ContainerFromIndex(index) as Control,
        ItemsView view => Descendant<ItemsRepeater>(view)?.TryGetElement(index) as Control,
        _ => null,
    };

    private static T? Descendant<T>(DependencyObject root)
        where T : class
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if ((child as T ?? Descendant<T>(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The vertical scrolling of a marked element: a <see cref="ScrollViewer"/>, the one in a list, or an <see cref="ItemsView"/>'s <see cref="ScrollView"/>.</summary>
    private sealed class Scroller
    {
        private readonly ScrollViewer? _viewer;
        private readonly ScrollView? _view;

        private Scroller(ScrollViewer? viewer, ScrollView? view)
        {
            _viewer = viewer;
            _view = view;
        }

        public double Offset => _viewer?.VerticalOffset ?? _view!.VerticalOffset;

        public double ScrollableHeight => _viewer?.ScrollableHeight ?? _view!.ScrollableHeight;

        /// <summary>Null while the element has no scrolling yet (a list's template applies once it is shown).</summary>
        public static Scroller? Find(UIElement element) => element switch
        {
            ScrollViewer viewer => new Scroller(viewer, null),
            ItemsView { ScrollView: { } view } => new Scroller(null, view),
            ListViewBase list when Descendant<ScrollViewer>(list) is { } viewer => new Scroller(viewer, null),
            _ => null,
        };

        public void ScrollTo(double offset)
        {
            if (_viewer is not null)
            {
                _viewer.ChangeView(null, offset, null, disableAnimation: true);
            }
            else
            {
                _view!.ScrollTo(_view.HorizontalOffset, offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
            }
        }
    }
}
