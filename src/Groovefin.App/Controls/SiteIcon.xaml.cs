using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

// Public: XAML sets Site at run time and generates no setter for an internal class (#140).
/// <summary>
/// The logo of a site Groovefin links to, for its link (#198):
/// <c>&lt;controls:SiteIcon Site="MusicBrainz" /&gt;</c> or <c>Site="LastFm"</c>. It takes the
/// foreground of the button it sits in, so it follows the theme and the button's states.
/// </summary>
public sealed partial class SiteIcon : UserControl
{
    public static readonly DependencyProperty SiteProperty = DependencyProperty.Register(
        nameof(Site), typeof(string), typeof(SiteIcon), new PropertyMetadata(string.Empty, OnSiteChanged));

    public SiteIcon()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets the site: <c>MusicBrainz</c> or <c>LastFm</c>.</summary>
    public string Site
    {
        get => (string)GetValue(SiteProperty);
        set => SetValue(SiteProperty, value);
    }

    private static void OnSiteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (SiteIcon)d;
        var site = e.NewValue as string;
        icon.MusicBrainzLogo.Visibility = site == "MusicBrainz" ? Visibility.Visible : Visibility.Collapsed;
        icon.LastFmLogo.Visibility = site == "LastFm" ? Visibility.Visible : Visibility.Collapsed;
    }
}
