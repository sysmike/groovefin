using System.ComponentModel;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>Favourite songs, albums and artists, one tab each.</summary>
internal sealed partial class FavouritesPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IStringResources _strings;

    public FavouritesPage()
    {
        ViewModel = App.Services.GetRequiredService<FavouritesViewModel>();
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public FavouritesViewModel ViewModel { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync(_cancellation.Token);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FavouritesViewModel.HasTracks) or nameof(FavouritesViewModel.HasAlbums)
            or nameof(FavouritesViewModel.HasArtists) or nameof(FavouritesViewModel.IsLoading))
        {
            UpdateSection();
        }
    }

    private void OnSectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => UpdateSection();

    /// <summary>Shows the selected tab's list, or its empty text once loaded.</summary>
    private void UpdateSection()
    {
        var songs = SongsSection.IsSelected;
        var albums = AlbumsSection.IsSelected;
        var artists = ArtistsSection.IsSelected;

        SongsList.Visibility = Show(songs && ViewModel.HasTracks);
        AlbumsView.Visibility = Show(albums && ViewModel.HasAlbums);
        ArtistsView.Visibility = Show(artists && ViewModel.HasArtists);

        var hasItems = songs ? ViewModel.HasTracks : albums ? ViewModel.HasAlbums : ViewModel.HasArtists;
        var empty = !ViewModel.IsLoading && !ViewModel.LoadFailed && !hasItems;
        EmptyText.Visibility = Show(empty);
        EmptyText.Text = empty
            ? _strings.GetString(songs ? "Favourites_NoSongs" : albums ? "Favourites_NoAlbums" : "Favourites_NoArtists")
            : string.Empty;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FavouriteTrackItem item)
        {
            ViewModel.PlayTrackCommand.Execute(item);
        }
    }

    private void OnUnfavouriteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FavouriteTrackItem item)
        {
            ViewModel.UnfavouriteTrackCommand.Execute(item);
        }
    }

    private void OnAlbumInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is AlbumItem item)
        {
            ViewModel.OpenAlbumCommand.Execute(item);
        }
    }

    private void OnArtistInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ArtistItem item)
        {
            ViewModel.OpenArtistCommand.Execute(item);
        }
    }
}
