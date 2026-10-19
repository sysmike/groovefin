using System.Collections.ObjectModel;
using Groovefin.Core.Services;

namespace Groovefin.Core.Notifications;

/// <summary>
/// <see cref="INotificationService"/> that keeps at most <see cref="MaxVisible"/> notifications and
/// drops duplicates, so a failing background task cannot flood the window.
/// </summary>
public sealed class NotificationService : INotificationService
{
    public const int MaxVisible = 3;

    private readonly IDispatcher _dispatcher;
    private readonly ObservableCollection<AppNotification> _items = [];

    public NotificationService(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Notifications = new ReadOnlyObservableCollection<AppNotification>(_items);
    }

    public ReadOnlyObservableCollection<AppNotification> Notifications { get; }

    public void Show(AppNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _ = _dispatcher.EnqueueAsync(() =>
        {
            if (_items.Contains(notification))
            {
                return;
            }

            _items.Add(notification);
            while (_items.Count > MaxVisible)
            {
                _items.RemoveAt(0);
            }
        });
    }

    public void Dismiss(AppNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _ = _dispatcher.EnqueueAsync(() => _items.Remove(notification));
    }
}
