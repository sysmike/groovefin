using Groovefin.Core.Images;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

/// <summary>
/// The content of a favourite playlist's item in the navigation pane (#299): cover, name and details while
/// the pane is open, and <see cref="Icon"/>, the cover again, for the closed compact pane.
/// </summary>
public sealed partial class PlaylistNavItem : UserControl
{
    /// <summary>The cover's size in the open pane.</summary>
    public const double CoverSize = 40;

    private readonly ImageIcon _coverIcon = new();
    private readonly FontIcon _placeholderIcon = new() { Glyph = "" };
    private ImageRequest? _cover;

    public PlaylistNavItem()
    {
        InitializeComponent();
    }

    /// <summary>Gets the icon for the closed compact pane: the cover, or the playlist glyph without one.</summary>
    public IconElement Icon => _cover is null ? _placeholderIcon : _coverIcon;

    /// <summary>Shows a playlist: <paramref name="cover"/> is its image request, <see langword="null"/> without one.</summary>
    public async void Show(string name, string details, ImageRequest? cover)
    {
        NameText.Text = name;
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
}
