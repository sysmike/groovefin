using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

// Public: XAML sets the properties at run time and generates no setters for an internal class (#140).
/// <summary>
/// A track list's # cell, a speaker instead of the number while the track plays (#247):
/// <c>&lt;controls:TrackNumber IsPlaying="{x:Bind IsCurrent, Mode=OneWay}" Text="{x:Bind Number}" /&gt;</c>.
/// </summary>
public sealed partial class TrackNumber : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackNumber), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(TrackNumber), new PropertyMetadata(false, OnIsPlayingChanged));

    public TrackNumber()
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

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TrackNumber)d).NumberText.Text = e.NewValue as string ?? string.Empty;

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        VisualStateManager.GoToState((TrackNumber)d, (bool)e.NewValue ? "Playing" : "NotPlaying", false);
}
