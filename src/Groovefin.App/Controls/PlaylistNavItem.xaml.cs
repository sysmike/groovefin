using Groovefin.Core.Images;
using Groovefin.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Groovefin.App.Controls;

/// <summary>
/// The content of a favourite playlist's item in the navigation pane (#299): cover, name and details while
/// the pane is open, and <see cref="Icon"/>, the cover again, for the closed compact pane. While the pointer
/// is on the item, a play button shows on the cover (#315).
/// </summary>
public sealed partial class PlaylistNavItem : UserControl
{
    /// <summary>The cover's size in the open pane.</summary>
    public const double CoverSize = 40;

    private readonly IStringResources _strings;
    private readonly ImageIcon _coverIcon = new();
    private readonly FontIcon _placeholderIcon = new() { Glyph = "" };
    private ImageRequest? _cover;

    public PlaylistNavItem()
    {
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();
    }

    /// <summary>Gets the icon for the closed compact pane: the cover, or the playlist glyph without one.</summary>
    public IconElement Icon => _cover is null ? _placeholderIcon : _coverIcon;

    /// <summary>Raised when the play button on the cover is clicked.</summary>
    public event EventHandler? PlayRequested;

    /// <summary>Shows the play button on the cover while the pointer is on the item.</summary>
    public void ShowPlay(bool show) => PlayLayer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

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
        _coverIcon.Source = null;
        if (cover is not null && await Artwork.LoadAsync(cover) is { } bitmap && Equals(cover, _cover))
        {
            _coverIcon.Source = bitmap;
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>A click on the button is not one on the item: it must not open the playlist as well.</summary>
    private void OnPlayTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void OnPlayDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => e.Handled = true;
}
