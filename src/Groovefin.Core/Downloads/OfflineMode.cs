using System.ComponentModel;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;

namespace Groovefin.Core.Downloads;

/// <summary>
/// Whether the pages show the downloads because the server cannot be reached (#335). Meanwhile the shell shows a
/// notice; it goes once a request reaches the server again or the network comes back. While "Work offline" is on
/// (#400) a notice says so instead, until it is turned off.
/// </summary>
public sealed class OfflineMode : IDisposable
{
    private readonly OfflineSwitch _network;
    private readonly INotificationService _notifications;
    private readonly IDispatcher _dispatcher;
    private readonly AppNotification _notice;
    private readonly AppNotification _workingOffline;
    private readonly Lock _gate = new();
    private bool _isOffline;

    public OfflineMode(OfflineSwitch network, INotificationService notifications, IStringResources strings, IDispatcher dispatcher)
    {
        _network = network;
        _notifications = notifications;
        _dispatcher = dispatcher;
        _notice = new AppNotification(NotificationSeverity.Informational, strings.GetString("Offline_Title"), strings.GetString("Offline_Message"));
        _workingOffline = new AppNotification(NotificationSeverity.Informational, strings.GetString("WorkOffline_Title"), strings.GetString("WorkOffline_Message"));
        _network.Changed += OnNetworkChanged;
        _network.PropertyChanged += OnSwitchChanged;
        if (_network.IsOn)
        {
            _ = _dispatcher.EnqueueAsync(() => _notifications.Show(_workingOffline));
        }
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

    public void Dispose()
    {
        _network.Changed -= OnNetworkChanged;
        _network.PropertyChanged -= OnSwitchChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (_network.IsOnline)
        {
            Set(false);
        }
    }

    /// <summary>On the UI thread, as the switch changes there.</summary>
    private void OnSwitchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OfflineSwitch.IsOn))
        {
            return;
        }

        if (_network.IsOn)
        {
            _notifications.Dismiss(_notice);
            _notifications.Show(_workingOffline);
        }
        else
        {
            _notifications.Dismiss(_workingOffline);
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
            // While working offline its own notice says why the pages show the downloads.
            if (offline && !_network.IsOn)
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
