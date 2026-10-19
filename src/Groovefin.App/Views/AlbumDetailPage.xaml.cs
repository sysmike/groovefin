using Groovefin.App.Services;
using Groovefin.Core.Models;
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
    }

    private void OnArtistClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ArtistRef artist)
        {
            ViewModel.OpenArtistCommand.Execute(artist);
        }
    }
}
