using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>The rule-based mixes (#401): make, open, play, change and delete them.</summary>
internal sealed partial class MixesPage : Page
{
    public MixesPage()
    {
        ViewModel = App.CreateViewModel<MixesViewModel>();
        InitializeComponent();
    }

    public MixesViewModel ViewModel { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private static MixItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as MixItem;

    private void OnMixClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MixItem item)
        {
            ViewModel.OpenCommand.Execute(item);
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.PlayCommand.Execute(item);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.EditCommand.Execute(item);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ViewModel.DeleteCommand.Execute(item);
        }
    }
}
