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

namespace Groovefin.App.Controls;

/// <summary>
/// The buttons on an album or artist cover (#185): play, and for albums a down arrow that opens the songs
/// popup (<see cref="AlbumPreviewPanel"/>). Laid over the cover in a tile template with
/// <c>&lt;controls:CoverActions Tag="{x:Bind}" /&gt;</c>; the Tag is the <see cref="AlbumItem"/> or
/// <see cref="ArtistItem"/>, not the DataContext (#172). The buttons show while the mouse is on the cover,
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
            _ => string.Empty,
        };

        var play = _strings.GetString("Cover_Play").Replace("{0}", name, StringComparison.Ordinal);
        AutomationProperties.SetName(PlayButton, play);
        ToolTipService.SetToolTip(PlayButton, play);
        var songs = _strings.GetString("Cover_ShowSongs").Replace("{0}", name, StringComparison.Ordinal);
        AutomationProperties.SetName(SongsButton, songs);
        ToolTipService.SetToolTip(SongsButton, songs);

        // Artists: one button in the middle of the round image.
        var isArtist = Tag is ArtistItem;
        SongsButton.Visibility = isArtist ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.HorizontalAlignment = isArtist ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        PlayButton.VerticalAlignment = isArtist ? VerticalAlignment.Center : VerticalAlignment.Bottom;
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
    /// The buttons' own exits and lost captures (after every click) bubble up here too, while the pointer
    /// is still on the cover: only a pointer outside it hides the buttons.
    /// </summary>
    private void OnPointerLeft(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root).Position;
        _pointerOver &= point.X >= 0 && point.Y >= 0 && point.X < Root.ActualWidth && point.Y < Root.ActualHeight;
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
