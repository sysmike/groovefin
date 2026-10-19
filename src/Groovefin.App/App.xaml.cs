using Groovefin.App.Hosting;
using Groovefin.Core;
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
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_logger is not null)
        {
            LogStopping(_logger);
        }

        // Saves complete on the thread pool, so blocking the UI thread here cannot deadlock.
        _host?.Services.GetRequiredService<ISettingsService>().FlushAsync().GetAwaiter().GetResult();
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        _host = null;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // TODO(#13): surface the error in an InfoBar and keep the app running where possible.
        if (_logger is not null)
        {
            LogUnhandled(_logger, e.Exception, "XAML");
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

        e.SetObserved();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Groovefin {Version} started (packaged: {IsPackaged}, OS: {OsVersion})")]
    private static partial void LogStarted(ILogger logger, string version, bool isPackaged, string osVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Groovefin is shutting down")]
    private static partial void LogStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled {Source} exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string source);
}
