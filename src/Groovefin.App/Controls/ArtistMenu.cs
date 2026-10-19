using Groovefin.Core.Models;
using Groovefin.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Groovefin.App.Controls;

/// <summary>
/// Context menu with "Go to artist" entries for a list row. It opens on right-click and on the Menu key
/// or Shift+F10, so keyboard users reach artists without the in-row links.
/// </summary>
internal static class ArtistMenu
{
    private const string ArtistGlyph = "";

    /// <summary>Shows the menu for <paramref name="artists"/>; returns <see langword="false"/> when there are none.</summary>
    public static bool TryShow(
        ContextRequestedEventArgs e,
        IReadOnlyList<ArtistRef> artists,
        IStringResources strings,
        Action<ArtistRef> open)
    {
        if (artists.Count == 0 || e.OriginalSource is not FrameworkElement target)
        {
            return false;
        }

        var menu = new MenuFlyout();
        foreach (var artist in artists)
        {
            var item = new MenuFlyoutItem
            {
                Text = strings.GetString("Menu_GoToArtist").Replace("{0}", artist.Name, StringComparison.Ordinal),
                Icon = new FontIcon { Glyph = ArtistGlyph },
            };
            item.Click += (_, _) => open(artist);
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
        return true;
    }
}
