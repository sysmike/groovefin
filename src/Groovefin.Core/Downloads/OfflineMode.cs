using Groovefin.Core.Notifications;
using Groovefin.Core.Services;

namespace Groovefin.Core.Downloads;

/// <summary>
/// Whether the pages show the downloads because the server cannot be reached (#335). Meanwhile the shell shows a
/// notice; it goes once a request reaches the server again or the network comes back.
/// </summary>
public sealed class OfflineMode : IDisposable
{
    private readonly INetworkStatus _network;
    private readonly INotificationService _notifications;
    private readonly IDispatcher _dispatcher;
    private readonly AppNotification _notice;
    private readonly Lock _gate = new();
    private bool _isOffline;

    public OfflineMode(INetworkStatus network, INotificationService notifications, IStringResources strings, IDispatcher dispatcher)
    {
        _network = network;
        _notifications = notifications;
        _dispatcher = dispatcher;
        _notice = new AppNotification(NotificationSeverity.Informational, strings.GetString("Offline_Title"), strings.GetString("Offline_Message"));
        _network.Changed += OnNetworkChanged;
    }

    public bool IsOffline
    {
        get
        {
            lock (_gate)
            {
                return _isOffline;
            }
        }
    }

    /// <summary>A page got its answer from the downloads.</summary>
    public void ServedFromDownloads() => Set(true);

    /// <summary>A request reached the server.</summary>
    public void ReachedServer() => Set(false);

    public void Dispose() => _network.Changed -= OnNetworkChanged;

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (_network.IsOnline)
        {
            Set(false);
        }
    }

    private void Set(bool offline)
    {
        lock (_gate)
        {
            if (_isOffline == offline)
            {
                return;
            }

            _isOffline = offline;
        }

        _ = _dispatcher.EnqueueAsync(() =>
        {
            if (offline)
            {
                _notifications.Show(_notice);
            }
            else
            {
                _notifications.Dismiss(_notice);
            }
        });
    }
}
