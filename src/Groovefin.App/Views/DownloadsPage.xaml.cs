using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>
/// Every download with its size and status (#388): Retry for the failed, Remove for each, and what was removed with
/// Download again. Opened from Settings → Downloads; read each time it opens.
/// </summary>
internal sealed partial class DownloadsPage : Page
{
    public DownloadsPage()
    {
        ViewModel = App.CreateViewModel<DownloadListViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public DownloadListViewModel ViewModel { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.LoadAsync();
    }
}
