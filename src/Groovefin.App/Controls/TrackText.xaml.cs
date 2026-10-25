using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

// Public: XAML sets the properties at run time and generates no setters for an internal class (#140).
/// <summary>
/// A track's title, artists or album in a track list, in the accent colour while the track plays (#247, #249):
/// <c>&lt;controls:TrackText IsPlaying="{x:Bind IsCurrent, Mode=OneWay}" Text="{x:Bind Name}" /&gt;</c>.
/// <see cref="IsSecondary"/> draws it grey otherwise (artists, album); <see cref="ShowsIcon"/> adds a speaker
/// before the title, for rows without a <see cref="TrackNumber"/>. It takes the font size of its element
/// (12 for a caption line).
/// </summary>
public sealed partial class TrackText : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackText), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(TrackText), new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty ShowsIconProperty = DependencyProperty.Register(
        nameof(ShowsIcon), typeof(bool), typeof(TrackText), new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty IsSecondaryProperty = DependencyProperty.Register(
        nameof(IsSecondary), typeof(bool), typeof(TrackText), new PropertyMetadata(false, OnStateChanged));

    public TrackText()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets whether the track is playing.</summary>
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>Gets or sets whether a speaker shows before the text while the track plays.</summary>
    public bool ShowsIcon
    {
        get => (bool)GetValue(ShowsIconProperty);
        set => SetValue(ShowsIconProperty, value);
    }

    /// <summary>Gets or sets whether the text is grey while the track does not play, like a row's artists and album.</summary>
    public bool IsSecondary
    {
        get => (bool)GetValue(IsSecondaryProperty);
        set => SetValue(IsSecondaryProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TrackText)d).Label.Text = e.NewValue as string ?? string.Empty;

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var text = (TrackText)d;
        var state = text.IsPlaying
            ? text.ShowsIcon ? "PlayingWithIcon" : "Playing"
            : text.IsSecondary ? "NotPlayingSecondary" : "NotPlaying";
        VisualStateManager.GoToState(text, state, false);
    }
}
