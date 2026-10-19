using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>The library's genres as cards.</summary>
internal sealed partial class GenresPage : Page
{
    private readonly CancellationTokenSource _cancellation = new();

    public GenresPage()
    {
        ViewModel = App.Services.GetRequiredService<GenresViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public GenresViewModel ViewModel { get; }

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

    private void OnGenreInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is GenreItem item)
        {
            ViewModel.OpenCommand.Execute(item);
        }
    }
}
