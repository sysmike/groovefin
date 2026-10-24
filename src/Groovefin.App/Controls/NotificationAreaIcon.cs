using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Privacy;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

/// <summary>
/// The optional notification area icon (H.NotifyIcon): a click shows the window, the menu has play/pause,
/// next, previous, privacy mode (checked while on, #234), show and exit. Created when shown and disposed when
/// hidden, so nothing stays behind.
/// </summary>
internal sealed class NotificationAreaIcon(NowPlayingViewModel player, PrivacyMode privacy, IStringResources strings, string toolTip, Action show, Action exit) : IDisposable
{
    private TaskbarIcon? _icon;
    private ToggleMenuFlyoutItem? _privacyItem;

    public bool IsVisible => _icon is not null;

    public void SetVisible(bool visible)
    {
        if (visible && _icon is null)
        {
            _icon = new TaskbarIcon
            {
                ToolTipText = toolTip,
                Icon = new System.Drawing.Icon(Path.Join(AppContext.BaseDirectory, "Assets", "AppIcon.ico")),
                ContextMenuMode = ContextMenuMode.PopupMenu,
                LeftClickCommand = new RelayCommand(show),
                NoLeftClickDelay = true,
                ContextFlyout = Menu(),
            };

            // Without efficiency mode: the app keeps running normally next to its icon.
            _icon.ForceCreate(false);
        }
        else if (!visible && _icon is not null)
        {
            privacy.PropertyChanged -= OnPrivacyChanged;
            _privacyItem = null;
            _icon.Dispose();
            _icon = null;
        }
    }

    public void Dispose() => SetVisible(false);

    private MenuFlyout Menu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Tray_PlayPause", player.PlayPauseCommand));
        menu.Items.Add(Item("Tray_Next", player.NextCommand));
        menu.Items.Add(Item("Tray_Previous", player.PreviousCommand));
        menu.Items.Add(new MenuFlyoutSeparator());
        _privacyItem = new ToggleMenuFlyoutItem
        {
            Text = strings.GetString("Tray_PrivacyMode"),
            IsChecked = privacy.IsOn,
            Command = privacy.ToggleCommand,
        };
        privacy.PropertyChanged += OnPrivacyChanged;
        menu.Items.Add(_privacyItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Tray_Show", new RelayCommand(show)));
        menu.Items.Add(Item("Tray_Exit", new RelayCommand(exit)));
        return menu;
    }

    /// <summary>Turned on or off elsewhere (the title bar, the shortcut): the menu's check follows.</summary>
    private void OnPrivacyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrivacyMode.IsOn) && _privacyItem is { } item)
        {
            item.IsChecked = privacy.IsOn;
        }
    }

    private MenuFlyoutItem Item(string textKey, System.Windows.Input.ICommand command) =>
        new() { Text = strings.GetString(textKey), Command = command };
}
