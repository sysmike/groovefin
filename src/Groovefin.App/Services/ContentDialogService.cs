using Groovefin.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Services;

/// <summary><see cref="IDialogService"/> using <see cref="ContentDialog"/> in the main window.</summary>
internal sealed class ContentDialogService : IDialogService
{
    private XamlRoot? _root;

    /// <summary>Sets the XAML root that dialogs are shown in. Called by the main window once loaded.</summary>
    public void Attach(XamlRoot root) => _root = root;

    public async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string closeButtonText)
    {
        var dialog = Create(title, primaryButtonText, closeButtonText);
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<string?> PromptAsync(string title, string label, string initialText, string primaryButtonText, string closeButtonText)
    {
        var box = new TextBox { Header = label, Text = initialText, InputScope = Url() };
        box.SelectAll();
        var dialog = Create(title, primaryButtonText, closeButtonText);
        dialog.Content = box;
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    private ContentDialog Create(string title, string primaryButtonText, string closeButtonText) => new()
    {
        XamlRoot = _root ?? throw new InvalidOperationException("Dialogs are not available before the main window is loaded."),
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        Title = title,
        PrimaryButtonText = primaryButtonText,
        CloseButtonText = closeButtonText,
        DefaultButton = ContentDialogButton.Primary,
    };

    private static Microsoft.UI.Xaml.Input.InputScope Url() => new()
    {
        Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Url) },
    };
}
