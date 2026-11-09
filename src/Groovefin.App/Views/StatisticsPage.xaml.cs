using Groovefin.App.Controls;
using Groovefin.Core.Statistics;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>
/// The statistics (#382): a week, month, year or all time, chosen with the segmented control; the arrows step to the
/// periods before and after. Loaded each time it opens, opened from the title bar.
/// </summary>
internal sealed partial class StatisticsPage : Page
{
    public StatisticsPage()
    {
        ViewModel = App.Services.GetRequiredService<StatisticsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public StatisticsViewModel ViewModel { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Cancel();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync();
    }

    private async void OnRangeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selected = sender.SelectedItem;
        var range = selected == WeekRange ? StatisticsRange.Week
            : selected == YearRange ? StatisticsRange.Year
            : selected == AllTimeRange ? StatisticsRange.AllTime
            : StatisticsRange.Month;
        if (IsLoaded)
        {
            await ViewModel.SelectRangeAsync(range);
        }
    }

    private void OnRowLoaded(object sender, RoutedEventArgs e) => Shelf.ScrollNever(sender as ItemsView);

    /// <summary>A row shows as many tiles as fit in its width, as on the start page (#376).</summary>
    private void OnRowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ItemsView { Tag: TileRow row } view)
        {
            row.Fit(TileRow.TilesThatFit(e.NewSize.Width - view.Padding.Left - view.Padding.Right));
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

    /// <summary>Click or Enter on a song plays the top songs from there.</summary>
    private void OnSongClick(object sender, ItemClickEventArgs e)
    {
        // Ctrl+click and Shift+click select (#295).
        if (!TrackSelection.IsSelecting && e.ClickedItem is OnRepeatItem item)
        {
            ViewModel.PlaySongCommand.Execute(item);
        }
    }

    private void OnSongFavouriteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OnRepeatItem item)
        {
            ViewModel.ToggleSongFavouriteCommand.Execute(item);
        }
    }
}
