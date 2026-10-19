using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>The user's playlists as tiles, and "New playlist".</summary>
internal sealed partial class PlaylistsPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();

    public PlaylistsPage()
    {
        ViewModel = App.Services.GetRequiredService<PlaylistsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public PlaylistsViewModel ViewModel { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync(_cancellation.Token);
    }

    private void OnPlaylistInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is PlaylistItem item)
        {
            ViewModel.OpenCommand.Execute(item);
        }
    }
}
