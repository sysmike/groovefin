using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

/// <summary>Helpers for horizontal album and artist lists ("shelves") and grids inside a scrolling page.</summary>
internal static class Shelf
{
    /// <summary>
    /// ItemsView scrolls vertically by default. A shelf scrolls sideways only, so vertical wheel input
    /// chains to the page. Call once the view is loaded (its ScrollView exists then).
    /// </summary>
    public static void ScrollHorizontally(ItemsView? view)
    {
        if (view?.ScrollView is { } scroll)
        {
            scroll.ContentOrientation = ScrollingContentOrientation.Horizontal;
            scroll.HorizontalScrollMode = ScrollingScrollMode.Enabled;
            scroll.HorizontalScrollBarVisibility = ScrollingScrollBarVisibility.Auto;
            scroll.VerticalScrollMode = ScrollingScrollMode.Disabled;
            scroll.VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;
        }
    }

    /// <summary>
    /// For a grid inside a scrolling page: the view never scrolls itself, so wheel input reaches the page,
    /// and its height follows its content's. Left to size itself there, it would get no room to lay out
    /// tiles in and show none, so give it a first height in XAML, about one row. Call once the view is
    /// loaded (its ScrollView exists then).
    /// </summary>
    public static void ScrollNever(ItemsView? view)
    {
        if (view?.ScrollView is not { } scroll)
        {
            return;
        }

        scroll.ContentOrientation = ScrollingContentOrientation.Vertical;
        scroll.HorizontalScrollMode = ScrollingScrollMode.Disabled;
        scroll.HorizontalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;
        scroll.VerticalScrollMode = ScrollingScrollMode.Disabled;
        scroll.VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;

        // The rows laid out so far give the extent; as the view grows, it lays out the rest.
        scroll.ExtentChanged += (_, _) => FitHeight(view, scroll);
        FitHeight(view, scroll);
    }

    private static void FitHeight(ItemsView view, ScrollView scroll)
    {
        var height = scroll.ExtentHeight + view.Padding.Top + view.Padding.Bottom;
        if (scroll.ExtentHeight > 0 && (double.IsNaN(view.Height) || Math.Abs(view.Height - height) > 0.5))
        {
            view.Height = height;
        }
    }
}
