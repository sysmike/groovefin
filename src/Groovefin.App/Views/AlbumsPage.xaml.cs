using System.ComponentModel;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>
/// All albums in a virtualized grid or list. The next page loads when the view scrolls near the end.
/// </summary>
internal sealed partial class AlbumsPage : Page
{
    /// <summary>Load the next page when less than this many pixels are left below the viewport.</summary>
    private const double LoadMoreThreshold = 800;

    private readonly IStringResources _strings;
    private bool _syncingControls;

    public AlbumsPage()
    {
        ViewModel = App.Services.GetRequiredService<AlbumsViewModel>();
        _strings = App.Services.GetRequiredService<IStringResources>();
        InitializeComponent();

        foreach (var field in ViewModel.SortFields)
        {
            SortBox.Items.Add(new ComboBoxItem { Content = _strings.GetString($"AlbumSort_{field}"), Tag = field });
        }

        GenreBox.Items.Add(new ComboBoxItem { Content = _strings.GetString("Albums_AllGenres"), Tag = null });
        ViewModel.Genres.CollectionChanged += (_, _) => SyncGenres();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncControls();
        ApplyViewMode();
    }

    public AlbumsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Loaded += OnFirstLoaded;
    }

    /// <summary>The scroll view and the XAML root (for the DPI scale) exist only once the page is loaded.</summary>
    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;

        if (AlbumsView.ScrollView is { } scroll)
        {
            scroll.ViewChanged += (_, _) => LoadMoreIfNeeded();
            scroll.ExtentChanged += (_, _) => LoadMoreIfNeeded();
        }

        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.InitializeAsync();
        LoadMoreIfNeeded();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
    }

    private void LoadMoreIfNeeded()
    {
        var scroll = AlbumsView.ScrollView;
        if (scroll is null)
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
            case nameof(AlbumsViewModel.TotalCount):
                CountText.Text = ViewModel.TotalCount > 0
                    ? _strings.GetString("Albums_Count").Replace("{0}", ViewModel.TotalCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal)
                    : string.Empty;
                break;
            case nameof(AlbumsViewModel.ViewMode):
                ApplyViewMode();
                break;
            case nameof(AlbumsViewModel.IsLoading) when !ViewModel.IsLoading:
                LoadMoreIfNeeded();
                break;
            case nameof(AlbumsViewModel.HasFilters):
                SyncControls();
                break;
        }
    }

    private void ApplyViewMode()
    {
        var grid = ViewModel.ViewMode == CollectionViewMode.Grid;
        AlbumsView.ItemTemplate = (DataTemplate)Resources[grid ? "AlbumGridTemplate" : "AlbumListTemplate"];
        AlbumsView.Layout = grid
            ? new UniformGridLayout { MinItemWidth = AlbumsViewModel.TileWidth, MinColumnSpacing = 16, MinRowSpacing = 8 }
            : new StackLayout { Spacing = 2 };
        GridViewToggle.IsChecked = grid;
        ListViewToggle.IsChecked = !grid;
    }

    /// <summary>Copies ViewModel state into the controls that are filled from code (sort, genre, years).</summary>
    private void SyncControls()
    {
        _syncingControls = true;
        SortBox.SelectedItem = SortBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (AlbumSortField)i.Tag == ViewModel.SortBy);
        SyncGenres();
        MinYearBox.Value = ViewModel.MinYear ?? double.NaN;
        MaxYearBox.Value = ViewModel.MaxYear ?? double.NaN;
        _syncingControls = false;
    }

    private void SyncGenres()
    {
        var wasSyncing = _syncingControls;
        _syncingControls = true;
        foreach (var genre in ViewModel.Genres.OfType<Genre>())
        {
            if (!GenreBox.Items.Cast<ComboBoxItem>().Any(i => Equals(i.Tag, genre)))
            {
                GenreBox.Items.Add(new ComboBoxItem { Content = genre.Name, Tag = genre });
            }
        }

        GenreBox.SelectedItem = GenreBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, ViewModel.Genre));
        _syncingControls = wasSyncing;
    }

    private void OnSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingControls && SortBox.SelectedItem is ComboBoxItem { Tag: AlbumSortField field })
        {
            ViewModel.SortBy = field;
        }
    }

    private void OnGenreSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingControls && GenreBox.SelectedItem is ComboBoxItem item)
        {
            ViewModel.Genre = item.Tag as Genre;
        }
    }

    private void OnYearChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncingControls)
        {
            return;
        }

        int? value = double.IsNaN(args.NewValue) ? null : (int)args.NewValue;
        if (sender == MinYearBox)
        {
            ViewModel.MinYear = value;
        }
        else
        {
            ViewModel.MaxYear = value;
        }
    }

    private void OnClearFiltersClick(object sender, RoutedEventArgs e) => ViewModel.ClearFiltersCommand.Execute(null);

    private void OnViewModeClick(object sender, RoutedEventArgs e) =>
        ViewModel.ViewMode = sender == ListViewToggle ? CollectionViewMode.List : CollectionViewMode.Grid;

    private void OnAlbumInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is AlbumItem item)
        {
            ViewModel.OpenAlbumCommand.Execute(item);
        }
    }
}
