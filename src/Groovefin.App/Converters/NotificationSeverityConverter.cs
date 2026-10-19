using Groovefin.Core.Notifications;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Converters;

/// <summary>x:Bind helper: Core notification severity to InfoBar severity.</summary>
internal static class NotificationSeverityConverter
{
    public static InfoBarSeverity ToInfoBarSeverity(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Success => InfoBarSeverity.Success,
        NotificationSeverity.Warning => InfoBarSeverity.Warning,
        NotificationSeverity.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };
}
