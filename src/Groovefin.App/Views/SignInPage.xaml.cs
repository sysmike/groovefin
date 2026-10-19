using Groovefin.App.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Groovefin.App.Views;

/// <summary>Sign in with user name and password. The navigation parameter is the server ID (optional).</summary>
internal sealed partial class SignInPage : Page
{
    public SignInPage()
    {
        ViewModel = App.Services.GetRequiredService<SignInViewModel>();
        InitializeComponent();
    }

    public SignInViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Initialize((e.Parameter as PageNavigationArgs)?.Parameter as string);

        // Focus the first empty field.
        Loaded += (_, _) => (string.IsNullOrEmpty(ViewModel.UserName) ? (Control)UserNameBox : PasswordBox).Focus(FocusState.Programmatic);
    }

    private void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SignInCommand.CanExecute(null))
        {
            ViewModel.SignInCommand.Execute(null);
            e.Handled = true;
        }
    }
}
