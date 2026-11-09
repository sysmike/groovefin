using Groovefin.App.Controls;
using Groovefin.App.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>A mix (#401): its rules and the songs they pick now, in On repeat's table. Picked each time it opens.</summary>
internal sealed partial class MixPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();
    private string? _mixId;

    public MixPage()
    {
        ViewModel = App.Services.GetRequiredService<MixViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public MixViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _mixId = (e.Parameter as PageNavigationArgs)?.Parameter as string;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.LoadAsync(_mixId, _cancellation.Token);
    }

    /// <summary>Click or Enter on a song plays the mix from there.</summary>
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
