using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>One album: hero header and tracks. The navigation parameter is the album ID.</summary>
internal sealed partial class AlbumDetailPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();
    private string? _albumId;

    public AlbumDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<AlbumDetailViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public AlbumDetailViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _albumId = (e.Parameter as PageNavigationArgs)?.Parameter as string;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (_albumId is null)
        {
            return;
        }

        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync(_albumId, _cancellation.Token);

        // The header is shown once the album has loaded; fly the list cover in after that layout pass.
        DispatcherQueue.TryEnqueue(() => CoverAnimation.StartOnDetail(CoverImage));
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        if (e.NavigationMode == NavigationMode.Back && ViewModel.HasAlbum)
        {
            CoverAnimation.PrepareToList(CoverImage);
        }
    }

    /// <summary>Click or Enter on a track plays the album from there.</summary>
    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackItem item)
        {
            ViewModel.PlayTrackCommand.Execute(item);
        }
    }

    /// <summary>Right-click or Menu key on a track: go to one of its artists.</summary>
    private void OnTrackContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TrackItem item)
        {
            ArtistMenu.TryShow(e, item.Track.Artists, App.Services.GetRequiredService<IStringResources>(), artist => ViewModel.OpenArtistCommand.Execute(artist));
        }
    }

    private async void OnAddToPlaylistClick(object sender, RoutedEventArgs e) =>
        await PlaylistMenu.ShowAsync(
            AddToPlaylistButton,
            ViewModel.Tracks,
            App.Services.GetRequiredService<AddToPlaylist>(),
            App.Services.GetRequiredService<IStringResources>());

    private void OnTrackFavouriteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackItem item)
        {
            ViewModel.ToggleTrackFavouriteCommand.Execute(item);
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
