using System.Collections.ObjectModel;
using Groovefin.Core.Services;

namespace Groovefin.Core.Notifications;

/// <summary>
/// <see cref="INotificationService"/> that keeps at most <see cref="MaxVisible"/> notifications and
/// drops duplicates, so a failing background task cannot flood the window. Confirmations (success and
/// informational notices) close by themselves after <see cref="AutoCloseDelay"/> (#268); errors and
/// warnings stay until they are closed.
/// </summary>
public sealed class NotificationService : INotificationService
{
    public const int MaxVisible = 3;

    public static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(5);

    private readonly IDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ObservableCollection<AppNotification> _items = [];

    // Only touched on the dispatcher, like _items.
    private readonly Dictionary<AppNotification, ITimer> _timers = [];

    public NotificationService(IDispatcher dispatcher, TimeProvider? time = null)
    {
        _dispatcher = dispatcher;
        _time = time ?? TimeProvider.System;
        Notifications = new ReadOnlyObservableCollection<AppNotification>(_items);
    }

    public ReadOnlyObservableCollection<AppNotification> Notifications { get; }

    public void Show(AppNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _ = _dispatcher.EnqueueAsync(() =>
        {
            if (!_items.Contains(notification))
            {
                _items.Add(notification);
                while (_items.Count > MaxVisible)
                {
                    StopTimer(_items[0]);
                    _items.RemoveAt(0);
                }
            }

            // Shown again: the confirmation stays its full time from now.
            if (_items.Contains(notification) && ClosesByItself(notification))
            {
                StopTimer(notification);
                _timers[notification] = _time.CreateTimer(_ => Dismiss(notification), null, AutoCloseDelay, Timeout.InfiniteTimeSpan);
            }
        });
    }

    public void Dismiss(AppNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _ = _dispatcher.EnqueueAsync(() =>
        {
            StopTimer(notification);
            _items.Remove(notification);
        });
    }

    private static bool ClosesByItself(AppNotification notification) =>
        notification.Severity is NotificationSeverity.Success or NotificationSeverity.Informational;

    private void StopTimer(AppNotification notification)
    {
        if (_timers.Remove(notification, out var timer))
        {
            timer.Dispose();
        }
    }
}
