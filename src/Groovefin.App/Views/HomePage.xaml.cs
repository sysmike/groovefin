using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>Landing page: horizontal album lists (recently added, recently played, …).</summary>
internal sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
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

    /// <summary>
    /// ItemsView scrolls vertically by default. The shelves scroll sideways; vertical wheel input then
    /// chains to the page.
    /// </summary>
    private void OnShelfLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsView { ScrollView: { } scroll })
        {
            scroll.ContentOrientation = ScrollingContentOrientation.Horizontal;
            scroll.HorizontalScrollMode = ScrollingScrollMode.Enabled;
            scroll.HorizontalScrollBarVisibility = ScrollingScrollBarVisibility.Auto;
            scroll.VerticalScrollMode = ScrollingScrollMode.Disabled;
            scroll.VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;
        }
    }

    private void OnAlbumInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is AlbumItem item)
        {
            ViewModel.OpenAlbumCommand.Execute(item);
        }
    }
}
