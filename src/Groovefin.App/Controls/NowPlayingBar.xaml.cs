using Groovefin.Core.Models;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Groovefin.App.Controls;

/// <summary>
/// The bar at the bottom of the window: current track, transport, seek bar, volume. Ctrl+P plays or
/// pauses, Ctrl+F and Ctrl+B skip forward and back.
/// </summary>
internal sealed partial class NowPlayingBar : UserControl
{
    /// <summary>Slider moves further than this from the playing position are the user's seeks.</summary>
    private const double SeekThresholdSeconds = 0.5;

    public NowPlayingBar()
    {
        ViewModel = App.Services.GetRequiredService<NowPlayingViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
    }

    public NowPlayingViewModel ViewModel { get; }

    /// <summary>
    /// The slider follows the playing position (one-way binding). A value that differs from it comes
    /// from the user (drag, click, arrow keys) and seeks.
    /// </summary>
    private void OnSeekValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (Math.Abs(e.NewValue - ViewModel.PositionSeconds) > SeekThresholdSeconds)
        {
            ViewModel.Seek(e.NewValue);
        }
    }

    private void OnArtistClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ArtistRef artist)
        {
            ViewModel.OpenArtistCommand.Execute(artist);
        }
    }
}
