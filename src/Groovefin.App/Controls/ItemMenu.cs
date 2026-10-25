using Groovefin.Core.Menus;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Groovefin.App.Controls;

// Public: XAML sets these properties at run time and generates no setter for an internal class (#140).
/// <summary>
/// The shared context menu (<see cref="ContextMenus"/>) for list rows and tiles of tracks, albums,
/// artists, playlists, genres and folders: <c>&lt;ListView controls:ItemMenu.IsEnabled="True" /&gt;</c>. It opens on
/// right-click, the Menu key and Shift+F10, for the item under the pointer or with focus.
/// </summary>
public static class ItemMenu
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ItemMenu), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(UIElement element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(UIElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Gets the shared actions for a list item, or <see langword="null"/> when it has none.</summary>
    public static IReadOnlyList<MenuAction>? ActionsFor(object? item)
    {
        var menus = App.Services.GetRequiredService<ContextMenus>();
        return item switch
        {
            Track track => menus.ForTrack(track),
            TrackItem row => menus.ForTrack(row.Track),
            ArtistTrackItem row => menus.ForTrack(row.Track),
            SearchTrackItem row => menus.ForTrack(row.Track),
            FavouriteTrackItem row => menus.ForTrack(row.Track),
            PlaylistEntryItem row => menus.ForTrack(row.Entry.Track),
            OnRepeatItem row => menus.ForTrack(row.Track),
            TrackRow row => menus.ForTrack(row.Track),
            AlbumItem tile => menus.ForAlbum(tile.Album),
            ArtistItem tile => menus.ForArtist(tile.Artist),
            PlaylistItem tile => menus.ForPlaylist(tile.Playlist),
            GenreItem tile => menus.ForGenre(tile.Genre),
            Folder folder => menus.ForFolder(folder),
            _ => null,
        };
    }

    /// <summary>Adds <paramref name="actions"/> to <paramref name="menu"/>, a separator between groups.</summary>
    public static void AddTo(MenuFlyout menu, IReadOnlyList<MenuAction> actions)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(actions);
        int? group = null;
        foreach (var action in actions)
        {
            if (group is { } previous && previous != Group(action.Kind))
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            group = Group(action.Kind);
            menu.Items.Add(action.Kind == MenuActionKind.AddToPlaylist ? PlaylistSubmenu(action) : Item(action));
        }
    }

    /// <summary>Shows a menu at the pointer, or at the element for the Menu key and Shift+F10.</summary>
    public static void Show(MenuFlyout menu, FrameworkElement target, ContextRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(e);
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

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.ContextRequested -= OnContextRequested;
            if ((bool)e.NewValue)
            {
                element.ContextRequested += OnContextRequested;
            }
        }
    }

    private static void OnContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement target || ActionsFor(target.DataContext) is not { Count: > 0 } actions)
        {
            return;
        }

        var menu = new MenuFlyout();
        AddTo(menu, actions);
        Show(menu, target, e);
    }

    /// <summary>Playback, then going somewhere, then the rest.</summary>
    private static int Group(MenuActionKind kind) => kind switch
    {
        MenuActionKind.PlayNow or MenuActionKind.PlayNext or MenuActionKind.AddToQueue or MenuActionKind.AddToPlaylist => 0,
        MenuActionKind.GoToAlbum or MenuActionKind.GoToArtist => 1,
        _ => 2,
    };

    private static string Glyph(MenuActionKind kind) => kind switch
    {
        MenuActionKind.PlayNow => "",
        MenuActionKind.PlayNext => "",
        MenuActionKind.AddToQueue => "",
        MenuActionKind.AddToPlaylist => "",
        MenuActionKind.GoToAlbum => "",
        MenuActionKind.GoToArtist => "",
        MenuActionKind.InstantMix => "",
        MenuActionKind.Favourite => "",
        MenuActionKind.Unfavourite => "",
        _ => "",
    };

    private static MenuFlyoutItem Item(MenuAction action)
    {
        var item = new MenuFlyoutItem { Text = action.Text, Icon = new FontIcon { Glyph = Glyph(action.Kind) } };
        item.Click += async (_, _) => await action.InvokeAsync();
        return item;
    }

    /// <summary>"New playlist…" and the user's playlists, filled in once they are loaded.</summary>
    private static MenuFlyoutSubItem PlaylistSubmenu(MenuAction action)
    {
        var add = App.Services.GetRequiredService<AddToPlaylist>();
        var strings = App.Services.GetRequiredService<IStringResources>();
        var submenu = new MenuFlyoutSubItem { Text = action.Text, Icon = new FontIcon { Glyph = Glyph(action.Kind) } };

        var create = new MenuFlyoutItem { Text = strings.GetString("Menu_NewPlaylist"), Icon = new FontIcon { Glyph = "" } };
        create.Click += async (_, _) => await add.CreateWithAsync(await action.Tracks!());
        submenu.Items.Add(create);

        var loading = new MenuFlyoutItem { Text = strings.GetString("Menu_LoadingPlaylists"), IsEnabled = false };
        submenu.Items.Add(loading);
        _ = FillAsync();
        return submenu;

        async Task FillAsync()
        {
            var playlists = await add.GetTargetsAsync();
            submenu.Items.Remove(loading);
            if (playlists.Count > 0)
            {
                submenu.Items.Add(new MenuFlyoutSeparator());
            }

            foreach (var playlist in playlists)
            {
                var item = new MenuFlyoutItem { Text = playlist.Name, Icon = new FontIcon { Glyph = "" } };
                item.Click += async (_, _) => await add.AddAsync(playlist, await action.Tracks!());
                submenu.Items.Add(item);
            }
        }
    }
}
