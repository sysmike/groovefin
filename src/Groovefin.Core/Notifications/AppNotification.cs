namespace Groovefin.Core.Notifications;

/// <summary>Severity of an <see cref="AppNotification"/>; maps to the InfoBar severity.</summary>
public enum NotificationSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>A message shown to the user in the shell's notification area (InfoBar).</summary>
public sealed record AppNotification(NotificationSeverity Severity, string Title, string Message);
