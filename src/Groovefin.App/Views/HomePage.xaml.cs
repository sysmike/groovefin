using Groovefin.App.Controls;
using Groovefin.Core.Models;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>Landing page: a random album, then rows of albums that fit the window (recently played, recently added).</summary>
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

    private void OnShelfLoaded(object sender, RoutedEventArgs e) => Shelf.ScrollNever(sender as ItemsView);

    /// <summary>A row shows as many albums as fit in its width (#376).</summary>
    private void OnShelfSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ItemsView { Tag: HomeShelf shelf } view)
        {
            shelf.Fit(HomeShelf.TilesThatFit(e.NewSize.Width - view.Padding.Left - view.Padding.Right));
        }
    }

    private void OnShowAllClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HomeShelf shelf)
        {
            ViewModel.ShowAllCommand.Execute(shelf);
        }
    }

    private void OnGenreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Genre genre)
        {
            ViewModel.OpenGenreCommand.Execute(genre);
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
