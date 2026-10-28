using Groovefin.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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

    public async Task ShowDetailsAsync(string title, IReadOnlyList<DetailSection> sections, string closeButtonText)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var panel = new StackPanel { Spacing = 8 };
        foreach (var section in sections)
        {
            var heading = new TextBlock
            {
                Text = section.Heading,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = new Thickness(0, panel.Children.Count > 0 ? 12 : 0, 0, 0),
            };
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
            panel.Children.Add(heading);
            panel.Children.Add(Rows(section.Rows, secondary));
        }

        var dialog = Create(title, string.Empty, closeButtonText);
        dialog.DefaultButton = ContentDialogButton.Close;
        // Long lists scroll; the scroll bar's own column keeps it off the values.
        dialog.Content = new ScrollViewer { Content = panel, Padding = new Thickness(0, 0, 16, 0) };
        await dialog.ShowAsync();
    }

    /// <summary>Label/value rows; values can be selected and copied, and Narrator reads each with its label.</summary>
    private static Grid Rows(IReadOnlyList<KeyValuePair<string, string>> rows, Microsoft.UI.Xaml.Media.Brush labelBrush)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[i].Key, Foreground = labelBrush, MaxWidth = 200, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
            var value = new TextBlock { Text = rows[i].Value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            AutomationProperties.SetName(value, $"{rows[i].Key}: {rows[i].Value}");
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        return grid;
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
