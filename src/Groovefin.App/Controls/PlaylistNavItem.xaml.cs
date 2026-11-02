using Groovefin.Core.Images;
using Groovefin.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Groovefin.App.Controls;

/// <summary>
/// The content of a favourite playlist's item in the navigation pane (#299): cover, name and details while
/// the pane is open, and <see cref="CreateIcon"/>, the cover again, for the closed compact pane. While the pointer
/// is on the item, a play button shows on the cover (#315).
/// </summary>
public sealed partial class PlaylistNavItem : UserControl
{
    /// <summary>The cover's size in the open pane.</summary>
    public const double CoverSize = 40;

    private readonly IStringResources _strings;
    private const string PlaylistGlyph = "";

    /// <summary>The cover icon handed out last, which gets the cover once it has loaded.</summary>
    private ImageIcon? _coverIcon;
    private BitmapImage? _coverBitmap;
    private ImageRequest? _cover;

    public PlaylistNavItem()
    {
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();
    }

    /// <summary>
    /// Creates the icon for the closed compact pane: the cover, or the playlist glyph without one. A new one
    /// each time: the item got its old icon back after the pane had been open over the page, and every
    /// later resize of the window was an unhandled XAML error (#315).
    /// </summary>
    public IconElement CreateIcon()
    {
        if (_cover is null)
        {
            return new FontIcon { Glyph = PlaylistGlyph };
        }

        _coverIcon = new ImageIcon { Source = _coverBitmap };
        return _coverIcon;
    }

    /// <summary>Raised when the play button on the cover is clicked.</summary>
    public event EventHandler? PlayRequested;

    /// <summary>
    /// Shows the play button on the cover while the pointer is on the item. Out of the window (a playlist
    /// that is no longer a favourite keeps its item), it changes without the fade, which needs the item
    /// on screen.
    /// </summary>
    public void ShowPlay(bool show)
    {
        if (PlayLayer.IsHitTestVisible == show)
        {
            return;
        }

        PlayLayer.IsHitTestVisible = show;
        if (IsLoaded)
        {
            PlayLayer.Opacity = show ? 1 : 0;
            return;
        }

        var fade = PlayLayer.OpacityTransition;
        PlayLayer.OpacityTransition = null;
        PlayLayer.Opacity = show ? 1 : 0;
        PlayLayer.OpacityTransition = fade;
    }

    /// <summary>Whether <paramref name="element"/> is the play button or inside it: a press there is no drag.</summary>
    public bool IsPlayButton(DependencyObject? element)
    {
        for (; element is not null && !ReferenceEquals(element, this); element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, PlayButton))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Shows a playlist: <paramref name="cover"/> is its image request, <see langword="null"/> without one.</summary>
    public async void Show(string name, string details, ImageRequest? cover)
    {
        NameText.Text = name;
        var play = _strings.GetString("Cover_Play").Replace("{0}", name, StringComparison.Ordinal);
        AutomationProperties.SetName(PlayButton, play);
        ToolTipService.SetToolTip(PlayButton, play);
        DetailsText.Text = details;
        DetailsText.Visibility = details.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Equals(cover, _cover))
        {
            return;
        }

        _cover = cover;
        Artwork.SetRequest(CoverImage, cover);
        _coverBitmap = null;
        if (_coverIcon is not null)
        {
            _coverIcon.Source = null;
        }

        if (cover is not null && await Artwork.LoadAsync(cover) is { } bitmap && Equals(cover, _cover))
        {
            _coverBitmap = bitmap;
            if (_coverIcon is not null)
            {
                _coverIcon.Source = bitmap;
            }
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>A click on the button is not one on the item: it must not open the playlist as well.</summary>
    private void OnPlayTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void OnPlayDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => e.Handled = true;
}
