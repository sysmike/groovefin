using System.Text.Json;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Updates;

/// <summary>How a check for a newer release ended.</summary>
public enum UpdateCheckResult
{
    /// <summary>Not checked: the automatic check is off, or there is no network.</summary>
    Skipped,

    UpToDate,

    /// <summary>A newer final release is out: <see cref="UpdateChecker.Available"/>.</summary>
    Available,

    /// <summary>The release source could not be asked.</summary>
    Failed,
}

/// <summary>
/// Tells the user when a newer final release of Groovefin is out (#432): a released package never gets a newer
/// .NET runtime or a fix by itself. Asks the <see cref="IReleaseSource"/> <see cref="FirstCheckDelay"/> after
/// start-up and every <see cref="CheckInterval"/>, while <see cref="UpdateSettings.CheckAutomatically"/> is on and
/// there is a network; and whenever the user asks in Settings → About. A newer release shows one notification per
/// version and stays in <see cref="Available"/> for the settings page. A check that fails is only logged.
/// </summary>
public sealed partial class UpdateChecker : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly IReleaseSource _source;
    private readonly AppVersion _version;
    private readonly ISettingsService _settings;
    private readonly INetworkStatus _network;
    private readonly INotificationService _notifications;
    private readonly IStringResources _strings;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<UpdateChecker> _logger;
    private readonly ITimer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposing = new();
    private ReleaseInfo? _available;
    private bool _checked;

    public UpdateChecker(
        IReleaseSource source,
        AppVersion version,
        ISettingsService settings,
        INetworkStatus network,
        INotificationService notifications,
        IStringResources strings,
        IDispatcher dispatcher,
        TimeProvider time,
        ILogger<UpdateChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(time);
        _source = source;
        _version = version;
        _settings = settings;
        _network = network;
        _notifications = notifications;
        _strings = strings;
        _dispatcher = dispatcher;
        _logger = logger;
        _timer = time.CreateTimer(_ => _ = CheckAsync(manual: false), null, FirstCheckDelay, CheckInterval);
    }

    /// <summary>Raised on any thread after a check changed <see cref="Available"/> or <see cref="HasChecked"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the newer release the last check found, or <see langword="null"/>.</summary>
    public ReleaseInfo? Available => Volatile.Read(ref _available);

    /// <summary>Gets a value indicating whether a check has reached the release source since the app started.</summary>
    public bool HasChecked => Volatile.Read(ref _checked);

    /// <summary>
    /// Asks for the newest release. The check of the timer (<paramref name="manual"/> off) is skipped while the
    /// setting is off or there is no network; the user's own check always asks.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(bool manual = true, CancellationToken cancellationToken = default)
    {
        if (!manual && (!_settings.Current.Updates.CheckAutomatically || !_network.IsOnline))
        {
            return UpdateCheckResult.Skipped;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposing.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ReleaseInfo? latest;
            try
            {
                latest = await _source.GetLatestAsync(linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException
                || (ex is OperationCanceledException && !linked.IsCancellationRequested))
            {
                // No network to GitHub, its rate limit, or a time-out: the next check asks again.
                LogCheckFailed(ex);
                return UpdateCheckResult.Failed;
            }

            var newer = latest is not null && ReleaseVersion.IsNewer(latest.Version, _version.Version) ? latest : null;
            Volatile.Write(ref _available, newer);
            Volatile.Write(ref _checked, true);
            Changed?.Invoke(this, EventArgs.Empty);
            if (newer is null)
            {
                LogUpToDate(_version.Version, latest?.Version ?? "none");
                return UpdateCheckResult.UpToDate;
            }

            LogAvailable(newer.Version, _version.Version);
            await _dispatcher.EnqueueAsync(() => Notify(newer)).ConfigureAwait(false);
            return UpdateCheckResult.Available;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _disposing.Cancel();
        _disposing.Dispose();
        _gate.Dispose();
    }

    /// <summary>Shows the notification once per release: a later start does not repeat it. Runs on the UI thread.</summary>
    private void Notify(ReleaseInfo release)
    {
        if (_settings.Current.Updates.NotifiedVersion == release.Version)
        {
            return;
        }

        _notifications.Show(new AppNotification(
            NotificationSeverity.Informational,
            _strings.GetString("Updates_NotificationTitle").Replace("{0}", release.Version, StringComparison.Ordinal),
            _strings.GetString("Updates_NotificationMessage")));
        _settings.Update(s => s with { Updates = s.Updates with { NotifiedVersion = release.Version } });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking for a newer release failed")]
    private partial void LogCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Groovefin {Installed} is up to date (newest final release: {Newest})")]
    private partial void LogUpToDate(string installed, string newest);

    [LoggerMessage(Level = LogLevel.Information, Message = "Groovefin {Newest} is available; {Installed} is installed")]
    private partial void LogAvailable(string newest, string installed);
}
