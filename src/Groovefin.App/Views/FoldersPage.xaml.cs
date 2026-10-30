using Groovefin.App.Controls;
using Groovefin.Core.Models;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>
/// The library by folder: a breadcrumb of the path, the open folder's subfolders and tracks. Alt+Up goes
/// to the parent folder.
/// </summary>
internal sealed partial class FoldersPage : Page
{
    private bool _loaded;

    public FoldersPage()
    {
        ViewModel = App.Services.GetRequiredService<FoldersViewModel>();
        InitializeComponent();
    }

    public FoldersViewModel ViewModel { get; }

    /// <summary>
    /// The page is cached: going back returns to the same folder. Opening it from the navigation pane
    /// starts at the top again, as the server or music library may have changed.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!_loaded)
        {
            Loaded += OnFirstLoaded;
        }
        else if (e.NavigationMode != NavigationMode.Back)
        {
            _ = ViewModel.LoadAsync();
        }
    }

    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _loaded = true;
        ViewModel.DisplayScale = XamlRoot?.RasterizationScale ?? 1;
        await ViewModel.LoadAsync();
    }

    private void OnCrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderCrumb crumb)
        {
            ViewModel.GoToCommand.Execute(crumb);
        }
    }

    private void OnFolderClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Folder folder)
        {
            ViewModel.OpenCommand.Execute(folder);
        }
    }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        // Ctrl+click and Shift+click select (#295).
        if (!TrackSelection.IsSelecting && e.ClickedItem is TrackRow row)
        {
            ViewModel.PlayTrackCommand.Execute(row);
        }
    }
}
