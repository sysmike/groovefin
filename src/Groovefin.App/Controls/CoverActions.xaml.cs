using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Groovefin.App.Controls;

/// <summary>
/// The buttons on an album, artist or playlist cover or a genre card (#185, #191): play, and for albums a
/// down arrow that opens the songs popup (<see cref="AlbumPreviewPanel"/>). Laid over the cover in a tile
/// template with <c>&lt;controls:CoverActions Tag="{x:Bind}" /&gt;</c>; the Tag is the
/// <see cref="AlbumItem"/>, <see cref="ArtistItem"/>, <see cref="PlaylistItem"/> or <see cref="GenreItem"/>,
/// not the DataContext (#172). The buttons show while the mouse is on the cover,
/// one of them has keyboard focus, or the popup is open. Touch has no hover: a tap opens the tile.
/// </summary>
internal sealed partial class CoverActions : UserControl
{
    private readonly IStringResources _strings;
    private bool _pointerOver;
    private bool _focused;
    private Flyout? _songs;

    public CoverActions()
    {
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();
        RegisterPropertyChangedCallback(TagProperty, (_, _) => OnItemChanged());
    }

    private void OnItemChanged()
    {
        // A recycled tile shows another item: start hidden, with that item's names.
        _pointerOver = false;
        _focused = false;
        _songs?.Hide();
        var name = Tag switch
        {
            AlbumItem album => album.Name,
            ArtistItem artist => artist.Name,
            PlaylistItem playlist => playlist.Name,
            GenreItem genre => genre.Name,
            _ => string.Empty,
        };

        var play = _strings.GetString("Cover_Play").Replace("{0}", name, StringComparison.Ordinal);
        AutomationProperties.SetName(PlayButton, play);
        ToolTipService.SetToolTip(PlayButton, play);
        var songs = _strings.GetString("Cover_ShowSongs").Replace("{0}", name, StringComparison.Ordinal);
        AutomationProperties.SetName(SongsButton, songs);
        ToolTipService.SetToolTip(SongsButton, songs);

        // Only albums have the songs popup. Play sits in the middle of an artist's round image, at the
        // right of a genre's card (beside its name) and at the bottom left of other covers.
        SongsButton.Visibility = Tag is AlbumItem ? Visibility.Visible : Visibility.Collapsed;
        (PlayButton.HorizontalAlignment, PlayButton.VerticalAlignment) = Tag switch
        {
            ArtistItem => (HorizontalAlignment.Center, VerticalAlignment.Center),
            GenreItem => (HorizontalAlignment.Right, VerticalAlignment.Center),
            _ => (HorizontalAlignment.Left, VerticalAlignment.Bottom),
        };
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var show = _pointerOver || _focused || _songs?.IsOpen == true;
        Actions.Opacity = show ? 1 : 0;
        Actions.IsHitTestVisible = show;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
        {
            _pointerOver = true;
            UpdateVisibility();
        }
    }

    /// <summary>
    /// The cover's own exit hides the buttons, also through a rounded corner, which is still inside its
    /// bounds (#187). The buttons' exits and lost captures (after every click) bubble up here too, while
    /// the pointer may still be on the cover: then a hit test decides.
    /// </summary>
    private void OnPointerLeft(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver &= !ReferenceEquals(e.OriginalSource, Root)
            && VisualTreeHelper.FindElementsInHostCoordinates(e.GetCurrentPoint(null).Position, Root).Any();
        UpdateVisibility();
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = false;
        UpdateVisibility();
    }

    /// <summary>Keyboard focus on a button shows them; a mouse click focuses it too, but that is the pointer's.</summary>
    private void OnButtonGotFocus(object sender, RoutedEventArgs e)
    {
        _focused = ((Control)sender).FocusState == FocusState.Keyboard;
        UpdateVisibility();
    }

    private void OnButtonLostFocus(object sender, RoutedEventArgs e)
    {
        _focused = false;
        UpdateVisibility();
    }

    /// <summary>A button press is not a click on the tile: the tile must not open its page as well.</summary>
    private void OnButtonTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void OnButtonDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => e.Handled = true;

    private async void OnPlayClick(object sender, RoutedEventArgs e)
    {
        var play = App.Services.GetRequiredService<QuickPlay>();
        switch (Tag)
        {
            case AlbumItem album:
                await play.PlayAlbumAsync(album.Album);
                break;
            case ArtistItem artist:
                await play.PlayArtistAsync(artist.Artist);
                break;
            case PlaylistItem playlist:
                await play.PlayPlaylistAsync(playlist.Playlist);
                break;
            case GenreItem genre:
                await play.PlayGenreAsync(genre.Genre);
                break;
        }
    }

    private async void OnSongsClick(object sender, RoutedEventArgs e)
    {
        if (Tag is not AlbumItem album)
        {
            return;
        }

        var panel = new AlbumPreviewPanel();
        using var loading = new CancellationTokenSource();
        var loaded = false;
        _songs = new Flyout
        {
            Content = panel,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        };
        var flyout = _songs;
        flyout.Closed += (_, _) =>
        {
            // Closed while the songs load: stop loading them. Both run on the UI thread, and once they are
            // loaded the token source is disposed.
            if (!loaded)
            {
                loading.Cancel();
            }

            if (ReferenceEquals(_songs, flyout))
            {
                _songs = null;
            }

            UpdateVisibility();
        };
        flyout.ShowAt(SongsButton);
        UpdateVisibility();
        try
        {
            await panel.ViewModel.LoadAsync(album.Album, loading.Token);
        }
        finally
        {
            loaded = true;
        }
    }
}
