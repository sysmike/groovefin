using Groovefin.App.Services;
using Groovefin.Core.Services;
using Groovefin.Core.Shortcuts;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Groovefin.App.Views;

/// <summary>
/// Records new keys for a shortcut (#68). Each key press shows the keys and, if they cannot be saved, why;
/// Narrator reads both. Tab, Enter and Esc keep their meaning in the dialog (move focus, save, cancel), so
/// it works with the keyboard alone, and they are no shortcut keys anyway. Enter saves the recorded keys
/// whichever button has focus: the dialog opens with focus on Cancel.
/// </summary>
internal static class ShortcutDialog
{
    public static async Task ShowAsync(XamlRoot root, ShortcutsViewModel shortcuts, ShortcutItemViewModel item, IStringResources strings)
    {
        var keys = new TextBlock
        {
            Text = strings.GetString("Shortcuts_Waiting"),
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        };
        var problem = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetLiveSetting(keys, AutomationLiveSetting.Assertive);
        AutomationProperties.SetLiveSetting(problem, AutomationLiveSetting.Assertive);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = strings.GetString("Shortcuts_DialogTitle"),
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { new TextBlock { Text = shortcuts.PromptFor(item), TextWrapping = TextWrapping.Wrap }, keys, problem },
            },
            PrimaryButtonText = strings.GetString("Dialog_Save"),
            CloseButtonText = strings.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        KeyGesture? chosen = null;
        var accepted = false;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && chosen is not null)
            {
                accepted = true;
                e.Handled = true;
                dialog.Hide();
                return;
            }

            if (e.Key is VirtualKey.Tab or VirtualKey.Enter or VirtualKey.Escape)
            {
                return;
            }

            // Handled, so neither the focused button (Space) nor the window's shortcuts act on the key.
            e.Handled = true;
            var gesture = KeyboardInput.Gesture(e.Key);
            var preview = shortcuts.Preview(item, gesture);
            chosen = preview.CanSave ? gesture : null;
            dialog.IsPrimaryButtonEnabled = preview.CanSave;
            keys.Text = preview.Keys;
            problem.Text = preview.Problem ?? string.Empty;
            problem.Visibility = preview.Problem is null ? Visibility.Collapsed : Visibility.Visible;
            Announce(keys);
            if (preview.Problem is not null)
            {
                Announce(problem);
            }
        };

        if ((await dialog.ShowAsync() == ContentDialogResult.Primary || accepted) && chosen is { } saved)
        {
            shortcuts.Assign(item, saved);
        }
    }

    private static void Announce(TextBlock text) =>
        FrameworkElementAutomationPeer.FromElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
}
