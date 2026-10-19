using System.Collections.ObjectModel;

namespace Groovefin.Core.Notifications;

/// <summary>
/// Notifications shown by the shell. Thread-safe: <see cref="Show"/> may be called from any thread;
/// <see cref="Notifications"/> only changes on the UI thread.
/// </summary>
public interface INotificationService
{
    /// <summary>Gets the notifications that are currently shown, oldest first.</summary>
    ReadOnlyObservableCollection<AppNotification> Notifications { get; }

    /// <summary>Shows <paramref name="notification"/> unless an equal one is already shown.</summary>
    void Show(AppNotification notification);

    /// <summary>Removes <paramref name="notification"/> (the user closed it).</summary>
    void Dismiss(AppNotification notification);
}
