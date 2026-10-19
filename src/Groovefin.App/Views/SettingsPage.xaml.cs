using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Views;

/// <summary>
/// Settings page. The full settings scope is tracked in #53.
/// </summary>
internal sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnFirstLoaded;
    }

    public SettingsViewModel ViewModel { get; }

    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        await ViewModel.LoadLibrariesAsync();
    }
}
