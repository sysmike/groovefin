using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Groovefin.App.Controls;

/// <summary>
/// The "Add to playlist" menu: "New playlist…", then the user's playlists. The playlists are loaded when
/// the menu is opened, so it is always current.
/// </summary>
internal static class PlaylistMenu
{
    private const string NewGlyph = "";
    private const string PlaylistGlyph = "";

    /// <summary>Loads the playlists and shows the menu below <paramref name="target"/> for <paramref name="tracks"/>.</summary>
    public static async Task ShowAsync(Microsoft.UI.Xaml.FrameworkElement target, IReadOnlyList<Track> tracks, AddToPlaylist add, IStringResources strings)
    {
        var playlists = await add.GetTargetsAsync();

        var menu = new MenuFlyout();
        var create = new MenuFlyoutItem { Text = strings.GetString("Menu_NewPlaylist"), Icon = new FontIcon { Glyph = NewGlyph } };
        create.Click += async (_, _) => await add.CreateWithAsync(tracks);
        menu.Items.Add(create);

        if (playlists.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (var playlist in playlists)
        {
            var item = new MenuFlyoutItem { Text = playlist.Name, Icon = new FontIcon { Glyph = PlaylistGlyph } };
            item.Click += async (_, _) => await add.AddAsync(playlist, tracks);
            menu.Items.Add(item);
        }

        menu.ShowAt(target, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }
}
