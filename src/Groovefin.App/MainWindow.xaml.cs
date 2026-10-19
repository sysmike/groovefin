using System.ComponentModel;
using Groovefin.App.Services;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Groovefin.App;

/// <summary>
/// Shell window: Mica backdrop, title bar with search, navigation pane and content frame.
/// </summary>
internal sealed partial class MainWindow : Window
{
    public MainWindow(
        ShellViewModel viewModel,
        FrameNavigationService navigation,
        ThemeService themeService,
        ContentDialogService dialogs,
        SearchSuggestionsViewModel suggestions)
    {
        ViewModel = viewModel;
        Suggestions = suggestions;
        InitializeComponent();

        Title = new ResourceLoader().GetString("AppDisplayName");
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SystemBackdrop = CreateBackdrop();

        navigation.Initialize(ContentFrame);
        themeService.Attach(this);
        RootGrid.Loaded += (_, _) => dialogs.Attach(RootGrid.XamlRoot);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        ViewModel.Start();
    }

    public ShellViewModel ViewModel { get; }

    public SearchSuggestionsViewModel Suggestions { get; }

    /// <summary>Mica when supported, Desktop Acrylic as fallback, otherwise the default solid background.</summary>
    private static SystemBackdrop? CreateBackdrop()
    {
        if (MicaController.IsSupported())
        {
            return new MicaBackdrop { Kind = MicaKind.Base };
        }

        return DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop() : null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SelectedSection))
        {
            SyncSelection(ViewModel.SelectedSection);
        }
    }

    private void SyncSelection(string? pageKey)
    {
        if (pageKey == PageKeys.Settings)
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        NavView.SelectedItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == pageKey);
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var pageKey = args.IsSettingsInvoked ? PageKeys.Settings : args.InvokedItemContainer?.Tag as string;
        if (pageKey is not null)
        {
            ViewModel.NavigateCommand.Execute(pageKey);
        }
    }

    private void OnTitleBarBackRequested(TitleBar sender, object args) => ViewModel.GoBackCommand.Execute(null);

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    /// <summary>Typing asks for suggestions; picking one from the list (which also changes the text) does not.</summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _ = Suggestions.UpdateAsync(sender.Text);
        }
    }

    /// <summary>Enter on a suggestion opens or plays it; Enter on the text shows the results page.</summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchSuggestion suggestion)
        {
            Suggestions.Choose(suggestion);
            return;
        }

        Suggestions.Cancel();
        ViewModel.SearchText = args.QueryText;
        if (ViewModel.SubmitSearchCommand.CanExecute(null))
        {
            ViewModel.SubmitSearchCommand.Execute(null);
        }
    }

    private void OnNotificationCloseButtonClick(InfoBar sender, object args)
    {
        if (sender.DataContext is AppNotification notification)
        {
            ViewModel.DismissNotificationCommand.Execute(notification);
        }
    }

    private void OnFocusSearchInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }
}
