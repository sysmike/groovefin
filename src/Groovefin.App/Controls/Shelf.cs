using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

/// <summary>Helpers for horizontal album and artist lists ("shelves").</summary>
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
}
