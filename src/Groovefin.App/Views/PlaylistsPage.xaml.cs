using System.ComponentModel;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>The user's playlists as tiles or rows (#449), and "New playlist".</summary>
internal sealed partial class PlaylistsPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();

    public PlaylistsPage()
    {
        ViewModel = App.Services.GetRequiredService<PlaylistsViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyViewMode();
        Loaded += OnLoaded;
    }

    public PlaylistsViewModel ViewModel { get; }

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
        if (e.PropertyName == nameof(PlaylistsViewModel.ViewMode))
        {
            ApplyViewMode();
        }
    }

    private void ApplyViewMode()
    {
        var grid = ViewModel.ViewMode == CollectionViewMode.Grid;
        PlaylistsView.ItemTemplate = (DataTemplate)Resources[grid ? "PlaylistTileTemplate" : "PlaylistListTemplate"];
        PlaylistsView.Layout = grid
            ? new UniformGridLayout { MinItemWidth = PlaylistsViewModel.TileWidth, MinColumnSpacing = 16, MinRowSpacing = 8 }
            : new StackLayout { Spacing = 2 };
        GridViewToggle.IsChecked = grid;
        ListViewToggle.IsChecked = !grid;
    }

    private void OnViewModeClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ViewMode = ReferenceEquals(sender, ListViewToggle) ? CollectionViewMode.List : CollectionViewMode.Grid;

        // A click on the toggle that is already on turns it off, but the view stays: keep it on.
        GridViewToggle.IsChecked = ViewModel.ViewMode == CollectionViewMode.Grid;
        ListViewToggle.IsChecked = ViewModel.ViewMode == CollectionViewMode.List;
    }

    private void OnPlaylistInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is PlaylistItem item)
        {
            ViewModel.OpenCommand.Execute(item);
        }
    }
}
