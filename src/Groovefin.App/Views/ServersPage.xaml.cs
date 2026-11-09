using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Groovefin.App.Views;

/// <summary>Add, edit, remove and switch Jellyfin servers. Also the first-run page.</summary>
internal sealed partial class ServersPage : Page
{
    public ServersPage()
    {
        ViewModel = App.CreateViewModel<ServersViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ServersViewModel ViewModel { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.DiscoverCommand.Cancel();
        ViewModel.Dispose();
    }

    /// <summary>Focuses the address box and looks for servers on the network (#405).</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        AddressBox.Focus(FocusState.Programmatic);
        ViewModel.DiscoverCommand.Execute(null);
    }

    private void OnAddFoundClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DiscoveredServerItem item)
        {
            ViewModel.AddDiscoveredCommand.Execute(item);
        }
    }

    private static ServerItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ServerItem;

    private void OnAddressKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.AddCommand.CanExecute(null))
        {
            ViewModel.AddCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMakeActiveClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.SetActiveCommand.Execute(item);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.EditAddressCommand.Execute(item);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.RemoveCommand.Execute(item);
        }
    }
}
