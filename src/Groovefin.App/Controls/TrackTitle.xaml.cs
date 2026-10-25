using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

// Public: XAML sets the properties at run time and generates no setters for an internal class (#140).
/// <summary>
/// A track's title in a track list, in the accent colour while the track plays (#247):
/// <c>&lt;controls:TrackTitle IsPlaying="{x:Bind IsCurrent, Mode=OneWay}" Text="{x:Bind Name}" /&gt;</c>.
/// <see cref="ShowsIcon"/> adds a speaker before it, for rows without a <see cref="TrackNumber"/>.
/// </summary>
public sealed partial class TrackTitle : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackTitle), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(TrackTitle), new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty ShowsIconProperty = DependencyProperty.Register(
        nameof(ShowsIcon), typeof(bool), typeof(TrackTitle), new PropertyMetadata(false, OnStateChanged));

    public TrackTitle()
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

    /// <summary>Gets or sets whether a speaker shows before the title while the track plays.</summary>
    public bool ShowsIcon
    {
        get => (bool)GetValue(ShowsIconProperty);
        set => SetValue(ShowsIconProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TrackTitle)d).TitleText.Text = e.NewValue as string ?? string.Empty;

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var title = (TrackTitle)d;
        var state = !title.IsPlaying ? "NotPlaying" : title.ShowsIcon ? "PlayingWithIcon" : "Playing";
        VisualStateManager.GoToState(title, state, false);
    }
}
