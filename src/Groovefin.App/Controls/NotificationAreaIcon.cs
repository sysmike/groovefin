using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

/// <summary>
/// The optional notification area icon (H.NotifyIcon): a click shows the window, the menu has play/pause,
/// next, previous, show and exit. Created when shown and disposed when hidden, so nothing stays behind.
/// </summary>
internal sealed class NotificationAreaIcon(NowPlayingViewModel player, IStringResources strings, string toolTip, Action show, Action exit) : IDisposable
{
    private TaskbarIcon? _icon;

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
        menu.Items.Add(Item("Tray_Show", new RelayCommand(show)));
        menu.Items.Add(Item("Tray_Exit", new RelayCommand(exit)));
        return menu;
    }

    private MenuFlyoutItem Item(string textKey, System.Windows.Input.ICommand command) =>
        new() { Text = strings.GetString(textKey), Command = command };
}
