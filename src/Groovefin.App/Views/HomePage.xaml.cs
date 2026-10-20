using Groovefin.App.Controls;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>Landing page: horizontal album lists (recently added, recently played, …).</summary>
internal sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.CreateViewModel<HomeViewModel>();
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Loaded += OnFirstLoaded;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    /// <summary>The XAML root (for the DPI scale) exists only once the page is loaded.</summary>
    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.RefreshAsync();
    }

    private void OnShelfLoaded(object sender, RoutedEventArgs e) => Shelf.ScrollHorizontally(sender as ItemsView);

    private void OnAlbumInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is AlbumItem item)
        {
            ViewModel.OpenAlbumCommand.Execute(item);
        }
    }
}
