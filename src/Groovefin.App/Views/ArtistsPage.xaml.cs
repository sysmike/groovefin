using System.ComponentModel;
using System.Globalization;
using Groovefin.Core.Api;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>
/// Album artists or all artists in a virtualized grid or list. The next page loads when the view scrolls
/// near the end.
/// </summary>
internal sealed partial class ArtistsPage : Page
{
    /// <summary>Load the next page when less than this many pixels are left below the viewport.</summary>
    private const double LoadMoreThreshold = 800;

    private readonly IStringResources _strings;
    private bool _syncingControls;

    public ArtistsPage()
    {
        ViewModel = App.CreateViewModel<ArtistsViewModel>();
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();

        foreach (var field in ViewModel.SortFields)
        {
            SortBox.Items.Add(new ComboBoxItem { Content = _strings.GetString($"ArtistSort_{field}"), Tag = field });
        }

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncControls();
        ApplyViewMode();
    }

    public ArtistsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Loaded += OnFirstLoaded;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
    }

    /// <summary>The scroll view and the XAML root (for the DPI scale) exist only once the page is loaded.</summary>
    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;

        if (ArtistsView.ScrollView is { } scroll)
        {
            scroll.ViewChanged += (_, _) => LoadMoreIfNeeded();
            scroll.ExtentChanged += (_, _) => LoadMoreIfNeeded();
        }

        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.InitializeAsync();
        LoadMoreIfNeeded();
    }

    private void LoadMoreIfNeeded()
    {
        if (ArtistsView.ScrollView is not { } scroll)
        {
            return;
        }

        var remaining = scroll.ExtentHeight - scroll.VerticalOffset - scroll.ViewportHeight;
        if (remaining < LoadMoreThreshold && ViewModel.LoadMoreCommand.CanExecute(null))
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ArtistsViewModel.TotalCount):
                CountText.Text = ViewModel.TotalCount > 0
                    ? _strings.GetString("Artists_Count").Replace("{0}", ViewModel.TotalCount.ToString("N0", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                    : string.Empty;
                break;
            case nameof(ArtistsViewModel.ViewMode):
                ApplyViewMode();
                break;
            case nameof(ArtistsViewModel.IsLoading) when !ViewModel.IsLoading:
                LoadMoreIfNeeded();
                break;
        }
    }

    private void ApplyViewMode()
    {
        var grid = ViewModel.ViewMode == CollectionViewMode.Grid;
        ArtistsView.ItemTemplate = (DataTemplate)Resources[grid ? "ArtistGridTemplate" : "ArtistListTemplate"];
        ArtistsView.Layout = grid
            ? new UniformGridLayout { MinItemWidth = ArtistsViewModel.TileWidth, MinColumnSpacing = 16, MinRowSpacing = 8 }
            : new StackLayout { Spacing = 2 };
        GridViewToggle.IsChecked = grid;
        ListViewToggle.IsChecked = !grid;
    }

    /// <summary>Copies ViewModel state into the controls that are filled from code (sort, scope).</summary>
    private void SyncControls()
    {
        _syncingControls = true;
        SortBox.SelectedItem = SortBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (ArtistSortField)i.Tag == ViewModel.SortBy);
        ScopeBar.SelectedItem = ViewModel.AlbumArtistsOnly ? AlbumArtistsItem : AllArtistsItem;
        _syncingControls = false;
    }

    private void OnSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingControls && SortBox.SelectedItem is ComboBoxItem { Tag: ArtistSortField field })
        {
            ViewModel.SortBy = field;
        }
    }

    private void OnScopeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_syncingControls && sender.SelectedItem is { } selected)
        {
            ViewModel.AlbumArtistsOnly = ReferenceEquals(selected, AlbumArtistsItem);
        }
    }

    private void OnViewModeClick(object sender, RoutedEventArgs e) =>
        ViewModel.ViewMode = ReferenceEquals(sender, ListViewToggle) ? CollectionViewMode.List : CollectionViewMode.Grid;

    private void OnArtistInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ArtistItem item)
        {
            ViewModel.OpenArtistCommand.Execute(item);
        }
    }
}
