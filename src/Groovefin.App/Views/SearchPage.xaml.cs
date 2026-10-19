using System.ComponentModel;
using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>Search results grouped by kind. The navigation parameter is the search text.</summary>
internal sealed partial class SearchPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();
    private string? _query;

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public SearchViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _query = (e.Parameter as PageNavigationArgs)?.Parameter as string;
    }

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
        if (_query is null)
        {
            return;
        }

        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync(_query, _cancellation.Token);
    }

    /// <summary>Changing a TextBlock's text does not announce it by itself; the live region needs the event.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.Summary) && ViewModel.Summary.Length > 0)
        {
            FrameworkElementAutomationPeer.FromElement(SummaryText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void OnShelfLoaded(object sender, RoutedEventArgs e) => Shelf.ScrollHorizontally(sender as ItemsView);

    private void OnArtistInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ArtistItem item)
        {
            ViewModel.OpenArtistCommand.Execute(item);
        }
    }

    private void OnAlbumInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is AlbumItem item)
        {
            ViewModel.OpenAlbumCommand.Execute(item);
        }
    }

    private void OnPlaylistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistItem item)
        {
            ViewModel.OpenPlaylistCommand.Execute(item);
        }
    }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchTrackItem item)
        {
            ViewModel.PlayTrackCommand.Execute(item);
        }
    }
}
