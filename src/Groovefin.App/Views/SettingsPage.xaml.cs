using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>
/// Settings page in the style of Windows Settings: groups of cards, with details in expanders.
/// </summary>
internal sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Equalizer = App.CreateViewModel<EqualizerViewModel>();
        Shortcuts = App.CreateViewModel<ShortcutsViewModel>();
        InitializeComponent();
        Loaded += OnFirstLoaded;
        Loaded += OnLoaded;
    }

    public SettingsViewModel ViewModel { get; }

    public EqualizerViewModel Equalizer { get; }

    public ShortcutsViewModel Shortcuts { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Equalizer.Dispose();
        Shortcuts.Dispose();
    }

    /// <summary>Change on a shortcut: its Tag is the shortcut (<c>Tag="{x:Bind}"</c>, #172).</summary>
    private async void OnChangeShortcutClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ShortcutItemViewModel item)
        {
            await ShortcutDialog.ShowAsync(XamlRoot, Shortcuts, item, App.Services.GetRequiredService<IStringResources>());
        }
    }

    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        await ViewModel.LoadLibrariesAsync();
    }

    // The cache grows while browsing, so it is measured on every visit.
    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshCacheSizeAsync();
}
