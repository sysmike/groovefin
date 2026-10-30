using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.Models;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    /// <summary>The blurred background covers the header content, which alone sets the header's height.</summary>
    private void OnHeaderContentSizeChanged(object sender, SizeChangedEventArgs e) => HeaderBackground.Height = e.NewSize.Height;

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
        // Only the Albums page flies the cover back into its tile. Prepared for any other page, nothing
        // takes it, and it stays painted over that page until it times out (#196).
        if (e.NavigationMode == NavigationMode.Back && e.SourcePageType == typeof(AlbumsPage) && ViewModel.HasAlbum)
        {
            CoverAnimation.PrepareToList(CoverImage);
        }
    }

    /// <summary>Click or Enter on a track plays the album from there.</summary>
    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        // Ctrl+click and Shift+click select (#295).
        if (!TrackSelection.IsSelecting && e.ClickedItem is TrackItem item)
        {
            ViewModel.PlayTrackCommand.Execute(item);
        }
    }

    /// <summary>The album's tracks to a playlist picked from a menu.</summary>
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

    /// <summary>
    /// An artist link: its Tag is the artist (<c>Tag="{x:Bind}"</c>). DataContext is not: the template uses x:Bind,
    /// and the links did nothing (#172).
    /// </summary>
    private void OnArtistClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ArtistRef artist)
        {
            ViewModel.OpenArtistCommand.Execute(artist);
        }
    }

    /// <summary>A column header: its Tag names the column (<see cref="AlbumTrackSort"/>).</summary>
    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag && Enum.TryParse<AlbumTrackSort>(tag, out var column))
        {
            ViewModel.SortCommand.Execute(column);
        }
    }

    /// <summary>A genre link: its Tag is the genre, like the artist links'.</summary>
    private void OnGenreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Genre genre)
        {
            ViewModel.OpenGenreCommand.Execute(genre);
        }
    }
}
