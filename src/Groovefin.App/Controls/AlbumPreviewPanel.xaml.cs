using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

/// <summary>
/// The content of an album tile's songs popup (<see cref="CoverActions"/>, #185). A click on a song plays
/// the album from it; the button at the top plays it from the start.
/// </summary>
internal sealed partial class AlbumPreviewPanel : UserControl
{
    public AlbumPreviewPanel()
    {
        ViewModel = App.Services.GetRequiredService<AlbumPreviewViewModel>();
        InitializeComponent();
    }

    public AlbumPreviewViewModel ViewModel { get; }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackItem item)
        {
            ViewModel.PlayTrackCommand.Execute(item);
        }
    }
}
