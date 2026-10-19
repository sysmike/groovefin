using Groovefin.App.Hosting;
using Groovefin.Core;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
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

    public App()
    {
        InitializeComponent();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>Gets the application's service provider.</summary>
    public static IServiceProvider Services =>
        ((App)Current)._host?.Services ?? throw new InvalidOperationException("The host is not built yet.");

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = AppHost.Build();
        _host.Start();

        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        var version = _host.Services.GetRequiredService<AppVersion>();
        LogStarted(_logger, version.DisplayText, AppPaths.IsPackaged, Environment.OSVersion.VersionString);

        // Settings must be loaded before any view reads them. LoadAsync falls back to defaults on errors.
        await _host.Services.GetRequiredService<ISettingsService>().LoadAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Closed += OnMainWindowClosed;
        window.Activate();

        // Lives as long as the app: tells the server what is playing (play counts, recently played).
        _host.Services.GetRequiredService<PlaybackReporting>();

        // Lives as long as the app: media keys and the Windows media flyout.
        _host.Services.GetRequiredService<SystemMediaControlsSync>();
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
            _host.Services.GetRequiredService<IPlaybackEngine>().Stop();
            _host.Services.GetRequiredService<PlaybackReporting>().FlushAsync().Wait(ReportFlushTimeout);
        }

        _host?.Services.GetRequiredService<ISettingsService>().FlushAsync().GetAwaiter().GetResult();
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        _host = null;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            LogUnhandled(_logger, e.Exception, "XAML");
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
        if (_logger is not null && e.ExceptionObject is Exception ex)
        {
            LogUnhandled(_logger, ex, "AppDomain");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            LogUnhandled(_logger, e.Exception, "Task");
        }

        TryGetErrorReporter()?.Report(e.Exception.GetBaseException(), Operations.Unexpected);
        e.SetObserved();
    }

    private IErrorReporter? TryGetErrorReporter() => _host?.Services.GetService<IErrorReporter>();

    [LoggerMessage(Level = LogLevel.Information, Message = "Groovefin {Version} started (packaged: {IsPackaged}, OS: {OsVersion})")]
    private static partial void LogStarted(ILogger logger, string version, bool isPackaged, string osVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Groovefin is shutting down")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled {Source} exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string source);
}
