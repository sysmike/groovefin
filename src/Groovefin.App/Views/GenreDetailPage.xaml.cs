using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>One genre: play, shuffle, instant mix, its albums and songs. The navigation parameter is the genre ID.</summary>
internal sealed partial class GenreDetailPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();
    private string? _genreId;

    public GenreDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<GenreDetailViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public GenreDetailViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _genreId = (e.Parameter as PageNavigationArgs)?.Parameter as string;
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
        if (_genreId is null)
        {
            return;
        }

        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync(_genreId, _cancellation.Token);
    }

    private void OnShelfLoaded(object sender, RoutedEventArgs e) => Shelf.ScrollHorizontally(sender as ItemsView);

    private void OnAlbumInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is AlbumItem item)
        {
            ViewModel.OpenAlbumCommand.Execute(item);
        }
    }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        // Ctrl+click and Shift+click select (#295).
        if (!TrackSelection.IsSelecting && e.ClickedItem is TrackRow row)
        {
            ViewModel.PlayTrackCommand.Execute(row);
        }
    }
}
