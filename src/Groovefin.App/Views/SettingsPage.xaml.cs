using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Views;

/// <summary>
/// Settings page in the style of Windows Settings: groups of cards, with details in expanders.
/// </summary>
internal sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnFirstLoaded;
        Loaded += OnLoaded;
    }

    public SettingsViewModel ViewModel { get; }

    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        await ViewModel.LoadLibrariesAsync();
    }

    // The cache grows while browsing, so it is measured on every visit.
    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshCacheSizeAsync();
}
