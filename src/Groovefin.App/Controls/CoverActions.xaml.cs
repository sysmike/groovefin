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
/// Whether the mouse is on the cover is worked out from its position, not from enter and exit events:
/// a round artist image misses some of its exits (#187).
/// </summary>
internal sealed partial class CoverActions : UserControl
{
    private readonly IStringResources _strings;
    private readonly PointerEventHandler _onWindowPointer;
    private bool _pointerOver;

    /// <summary>The window's content, watched while the mouse shows the buttons.</summary>
    private UIElement? _window;
    private bool _focused;
    private Flyout? _songs;

    public CoverActions()
    {
        _strings = App.Services.GetRequiredService<IStringResources>();
        _onWindowPointer = OnWindowPointer;
        InitializeComponent();
        RegisterPropertyChangedCallback(TagProperty, (_, _) => OnItemChanged());
        Unloaded += (_, _) => StopWatching();
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
        if (!show)
        {
            HandFocusToTile();
        }

        // While the mouse shows them, every move in the window is checked: once it is off the cover they
        // go, also when the cover missed the pointer's exit.
        if (_pointerOver)
        {
            StartWatching();
        }
        else
        {
            StopWatching();
        }
    }

    /// <summary>
    /// Hidden while a button still has focus from a click: the tile takes it. A hidden button that kept
    /// focus showed them all again once that focus turned into keyboard focus, wherever the pointer was,
    /// and Space would press it (#236).
    /// </summary>
    private void HandFocusToTile()
    {
        if (PlayButton.FocusState == FocusState.Unfocused && SongsButton.FocusState == FocusState.Unfocused)
        {
            return;
        }

        for (var element = VisualTreeHelper.GetParent(this); element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ItemContainer tile)
            {
                tile.Focus(FocusState.Programmatic);
                return;
            }
        }
    }

    private void SetPointerOver(bool value)
    {
        if (_pointerOver != value)
        {
            _pointerOver = value;
            UpdateVisibility();
        }
    }

    /// <summary>
    /// Whether the pointer is on the cover: within its bounds, and on a round artist image within the
    /// circle. Its clipped corners are inside the bounds but not on the image.
    /// </summary>
    private bool IsOnCover(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root).Position;
        double width = Root.ActualWidth, height = Root.ActualHeight;
        if (point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height)
        {
            return false;
        }

        if (Tag is not ArtistItem)
        {
            return true;
        }

        var radius = Math.Min(width, height) / 2;
        double dx = point.X - (width / 2), dy = point.Y - (height / 2);
        return (dx * dx) + (dy * dy) < radius * radius;
    }

    /// <summary>Entered or moved on the cover; a round image's corners count as off it.</summary>
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
        {
            SetPointerOver(IsOnCover(e));
        }
    }

    /// <summary>
    /// Exited or lost capture. The buttons' own exits and lost captures (after every click) bubble up here
    /// too, while the pointer is still on the cover: its position decides.
    /// </summary>
    private void OnPointerLeft(object sender, PointerRoutedEventArgs e) => SetPointerOver(_pointerOver && IsOnCover(e));

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e) => SetPointerOver(false);

    /// <summary>A move anywhere in the window, or the pointer leaving it, while the buttons show for the mouse.</summary>
    private void OnWindowPointer(object sender, PointerRoutedEventArgs e)
    {
        if (!IsOnCover(e))
        {
            SetPointerOver(false);
        }
    }

    private void StartWatching()
    {
        if (_window is null && XamlRoot?.Content is UIElement content)
        {
            _window = content;
            content.AddHandler(PointerMovedEvent, _onWindowPointer, handledEventsToo: true);
            content.AddHandler(PointerExitedEvent, _onWindowPointer, handledEventsToo: true);
        }
    }

    private void StopWatching()
    {
        if (_window is { } content)
        {
            content.RemoveHandler(PointerMovedEvent, _onWindowPointer);
            content.RemoveHandler(PointerExitedEvent, _onWindowPointer);
            _window = null;
        }
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
