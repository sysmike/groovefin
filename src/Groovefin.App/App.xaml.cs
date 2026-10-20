using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Groovefin.App.Hosting;
using Groovefin.Core;
using Groovefin.Core.Discord;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Session;
using Groovefin.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Groovefin.App;

/// <summary>
/// Application entry point. Builds the host, logs unhandled exceptions and shows the main window.
/// </summary>
public partial class App : Application
{
    /// <summary>How long closing the window waits for the last playback reports.</summary>
    private static readonly TimeSpan ReportFlushTimeout = TimeSpan.FromSeconds(2);

    private IHost? _host;
    private ILogger<App>? _logger;
    private bool _windowShown;

    public App()
    {
        // Before anything can fail: until the logger exists, StartupCrashLog records what goes wrong,
        // including while App.xaml loads.
        AppDomain.CurrentDomain.FirstChanceException += OnStartupFirstChanceException;
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        InitializeComponent();
    }

    /// <summary>Gets the application's service provider.</summary>
    public static IServiceProvider Services =>
        ((App)Current)._host?.Services ?? throw new InvalidOperationException("The host is not built yet.");

    /// <summary>
    /// Creates a disposable page view model with its dependencies from <see cref="Services"/>, but outside
    /// the container: the container would keep every disposable transient until the app closes (#147).
    /// The page owns it and disposes it when the user leaves (or, for a cached page, the GC collects it).
    /// </summary>
    public static T CreateViewModel<T>()
        where T : class, IDisposable => ActivatorUtilities.CreateInstance<T>(Services);

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await StartAsync();
        }
        catch (Exception ex) when (!_windowShown)
        {
            // Without a window there is nowhere to show the error. Record it and end the app; otherwise
            // it stays open invisibly, and every new start adds another invisible copy.
            if (_logger is not null)
            {
                LogStartupFailed(_logger, ex);
            }
            else
            {
                StartupCrashLog.Write(ex, "start-up");
            }

            Exit();
        }
    }

    private async Task StartAsync()
    {
        _host = AppHost.Build();
        _host.Start();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        _logger = logger;

        // GROOVEFIN_TRACE_EXCEPTIONS=1 keeps recording every thrown exception there (the CI smoke test sets it).
        if (Environment.GetEnvironmentVariable("GROOVEFIN_TRACE_EXCEPTIONS") != "1")
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnStartupFirstChanceException;
        }

        // XAML reports failed resource lookups and bindings only here; they explain otherwise bare layout errors.
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        DebugSettings.XamlResourceReferenceFailed += (_, e) => LogXamlProblem(logger, "resource reference", e.Message);
        DebugSettings.IsBindingTracingEnabled = true;
        DebugSettings.BindingFailed += (_, e) => LogXamlProblem(logger, "binding", e.Message);

        var version = _host.Services.GetRequiredService<AppVersion>();
        LogStarted(_logger, version.DisplayText, AppPaths.IsPackaged, Environment.OSVersion.VersionString);

        // Settings must be loaded before any view reads them. LoadAsync falls back to defaults on errors.
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        await settings.LoadAsync();

        // Before the first window loads its strings; a changed language applies on the next start.
        ApplyLanguage(settings.Current.Appearance.Language);

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Closed += OnMainWindowClosed;
        window.Activate();
        _windowShown = true;

        // Lives as long as the app: tells the server what is playing (play counts, recently played).
        _host.Services.GetRequiredService<PlaybackReporting>();

        // Lives as long as the app: media keys and the Windows media flyout.
        _host.Services.GetRequiredService<SystemMediaControlsSync>();

        // Lives as long as the app: the taskbar thumbnail buttons and progress (needs the shown window).
        _host.Services.GetRequiredService<TaskbarSync>();

        // Lives as long as the app: the Discord status (when turned on).
        _host.Services.GetRequiredService<DiscordPresence>();

        // The window has restored the server connection: bring back the last queue (paused) and keep saving it.
        await _host.Services.GetRequiredService<SessionPersistence>().RestoreAsync();

        LogStartupMeasured(_logger);
    }

    /// <summary>Start-up time (process start to a shown window with the session restored) and memory, see docs/performance.md.</summary>
    private static void LogStartupMeasured(ILogger logger)
    {
        using var process = Process.GetCurrentProcess();
        var elapsed = DateTime.Now - process.StartTime;
        LogStartup(logger, (long)elapsed.TotalMilliseconds, process.WorkingSet64 / (1024 * 1024), GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024));
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_logger is not null)
        {
            LogStopping(_logger);
        }

        // Report the playing track as stopped, then give the reports a moment to reach the server.
        // Saves and reports complete on the thread pool, so blocking the UI thread here cannot deadlock.
        if (_host is not null)
        {
            // Before stopping: the position of the playing track is saved too.
            _host.Services.GetRequiredService<SessionPersistence>().SaveAsync().Wait(ReportFlushTimeout);
            _host.Services.GetRequiredService<IPlaybackEngine>().Stop();
            _host.Services.GetRequiredService<PlaybackReporting>().FlushAsync().Wait(ReportFlushTimeout);
        }

        // From here on, errors are only logged: the handlers must not ask a host that is being disposed (#145).
        var host = _host;
        _host = null;
        host?.Services.GetRequiredService<ISettingsService>().FlushAsync().GetAwaiter().GetResult();
        host?.StopAsync().GetAwaiter().GetResult();
        host?.Dispose();
    }

    /// <summary>Every exception before the logger exists, handled or not: the last ones explain a start-up crash.</summary>
    private static void OnStartupFirstChanceException(object? sender, FirstChanceExceptionEventArgs e) =>
        StartupCrashLog.Write(e.Exception, "first chance");

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            // XAML's message names the failing resource or element even when the exception is generic.
            LogUnhandled(_logger, e.Exception, "XAML", e.Message);
        }
        else
        {
            StartupCrashLog.Write(e.Exception, $"unhandled (XAML): {e.Message}");
        }

        // Keep the app running and tell the user; the details are in the log.
        if (TryGetErrorReporter() is { } errors)
        {
            errors.Report(e.Exception, Operations.Unexpected);
            e.Handled = true;
        }
    }

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception ex)
        {
            return;
        }

        if (_logger is not null)
        {
            LogUnhandled(_logger, ex, "AppDomain", ex.Message);
        }
        else
        {
            StartupCrashLog.Write(ex, "unhandled (AppDomain)");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            LogUnhandled(_logger, e.Exception, "Task", e.Exception.Message);
        }

        TryGetErrorReporter()?.Report(e.Exception.GetBaseException(), Operations.Unexpected);
        e.SetObserved();
    }

    /// <summary>Picks the <c>.resw</c> language; without a choice the Windows display language applies.</summary>
    private void ApplyLanguage(string? language)
    {
        // The unpackaged app rejects an empty override (E_INVALIDARG), and without identity no override
        // outlives the process, so "use the Windows language" simply means not setting one.
        if (language is null && !AppPaths.IsPackaged)
        {
            return;
        }

        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language ?? string.Empty;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            LogLanguageFailed(_logger!, ex, language);
        }
    }

    private IErrorReporter? TryGetErrorReporter()
    {
        try
        {
            return _host?.Services.GetService<IErrorReporter>();
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Groovefin {Version} started (packaged: {IsPackaged}, OS: {OsVersion})")]
    private static partial void LogStarted(ILogger logger, string version, bool isPackaged, string osVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The UI language {Language} could not be applied")]
    private static partial void LogLanguageFailed(ILogger logger, Exception exception, string? language);

    [LoggerMessage(Level = LogLevel.Information, Message = "Start-up took {ElapsedMs} ms; working set {WorkingSetMb} MB, managed heap {ManagedMb} MB")]
    private static partial void LogStartup(ILogger logger, long elapsedMs, long workingSetMb, long managedMb);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Start-up failed before the window was shown; exiting")]
    private static partial void LogStartupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Groovefin is shutting down")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled {Source} exception: {Message}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string source, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "XAML {Kind} failed: {Message}")]
    private static partial void LogXamlProblem(ILogger logger, string kind, string message);
}
