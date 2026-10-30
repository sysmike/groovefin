using Groovefin.App.Controls;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>"On repeat" (#251): the tracks played most in the last 30 days. Loaded each time it opens.</summary>
internal sealed partial class OnRepeatPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();

    public OnRepeatPage()
    {
        ViewModel = App.Services.GetRequiredService<OnRepeatViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public OnRepeatViewModel ViewModel { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.LoadAsync(_cancellation.Token);
    }

    /// <summary>Click or Enter on a track plays the list from there.</summary>
    private void OnEntryClick(object sender, ItemClickEventArgs e)
    {
        // Ctrl+click and Shift+click select (#295).
        if (!TrackSelection.IsSelecting && e.ClickedItem is OnRepeatItem item)
        {
            ViewModel.PlayEntryCommand.Execute(item);
        }
    }

    private void OnEntryFavouriteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OnRepeatItem item)
        {
            ViewModel.ToggleEntryFavouriteCommand.Execute(item);
        }
    }
}
