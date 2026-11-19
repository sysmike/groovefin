using System.ComponentModel;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>The library's genres as cards or rows (#450).</summary>
internal sealed partial class GenresPage : Page
{
    /// <summary>The cards' smallest width, as the <c>GenreTile</c> resource has it.</summary>
    private const double CardWidth = 200;

    private readonly CancellationTokenSource _cancellation = new();

    public GenresPage()
    {
        ViewModel = App.Services.GetRequiredService<GenresViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyViewMode();
        Loaded += OnLoaded;
    }

    public GenresViewModel ViewModel { get; }

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
        await ViewModel.LoadAsync(_cancellation.Token);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenresViewModel.ViewMode))
        {
            ApplyViewMode();
        }
    }

    private void ApplyViewMode()
    {
        var grid = ViewModel.ViewMode == CollectionViewMode.Grid;
        GenresView.ItemTemplate = (DataTemplate)Resources[grid ? "GenreTileTemplate" : "GenreListTemplate"];
        GenresView.Layout = grid
            ? new UniformGridLayout { MinItemWidth = CardWidth, MinColumnSpacing = 12, MinRowSpacing = 12 }
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

    private void OnGenreInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is GenreItem item)
        {
            ViewModel.OpenCommand.Execute(item);
        }
    }
}
