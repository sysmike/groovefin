using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.Layout;
using Groovefin.Core.Models;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>One artist: header, biography, top tracks, albums, appears on, similar artists. The navigation parameter is the artist ID.</summary>
internal sealed partial class ArtistDetailPage : Page
{
    private const int CollapsedBiographyLines = 4;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly IStringResources _strings;
    private string? _artistId;

    public ArtistDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<ArtistDetailViewModel>();
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>The blurred background covers the header content, which alone sets the header's height.</summary>
    private void OnHeaderContentSizeChanged(object sender, SizeChangedEventArgs e) => HeaderBackground.Height = e.NewSize.Height;

    public ArtistDetailViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _artistId = (e.Parameter as PageNavigationArgs)?.Parameter as string;
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
        if (_artistId is null)
        {
            return;
        }

        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync(_artistId, _cancellation.Token);
    }

    private void OnShelfLoaded(object sender, RoutedEventArgs e) => Shelf.ScrollHorizontally(sender as ItemsView);

    private void OnGridLoaded(object sender, RoutedEventArgs e) => Shelf.ScrollNever(sender as ItemsView);

    /// <summary>The similar artists' row shows as many tiles as fit (#264): the columns of its grid.</summary>
    private void OnSimilarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ItemsView { Layout: UniformGridLayout layout } view)
        {
            var available = e.NewSize.Width - view.Padding.Left - view.Padding.Right;
            ViewModel.SimilarColumns = TileGrid.Columns(available, ((TileSize)Resources["SimilarTile"]).MinWidth, layout.MinColumnSpacing);
        }
    }

    private void OnBiographyToggleClick(object sender, RoutedEventArgs e)
    {
        var expand = BiographyText.MaxLines != 0;
        BiographyText.MaxLines = expand ? 0 : CollapsedBiographyLines;
        BiographyToggle.Content = _strings.GetString(expand ? "Artist_BiographyLess" : "Artist_BiographyMore");
    }

    private void OnTopTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistTrackItem item)
        {
            ViewModel.PlayTopTrackCommand.Execute(item);
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

    private void OnGenreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Genre genre)
        {
            ViewModel.OpenGenreCommand.Execute(genre);
        }
    }
}
