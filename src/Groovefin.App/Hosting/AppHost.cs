using Groovefin.App.Services;
using Groovefin.Core;
using Groovefin.Core.Notifications;
using Groovefin.Core.Security;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Groovefin.Jellyfin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Groovefin.App.Hosting;

/// <summary>
/// Composition root: builds the generic host with logging and all service registrations.
/// </summary>
internal static class AppHost
{
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddSerilog(config => config
            .MinimumLevel.Information()
#if DEBUG
            .MinimumLevel.Debug()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Join(AppPaths.Logs, "groovefin-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        var version = AppVersion.FromAssembly(typeof(AppHost).Assembly);
        builder.Services.AddSingleton(version);

        // Jellyfin
        builder.Services.AddSingleton(DeviceIdentity.Create(version));
        builder.Services.AddGroovefinJellyfin();

        // Settings: settings.json next to the logs (LocalFolder when packaged, %LOCALAPPDATA%\Groovefin otherwise).
        builder.Services.AddSingleton(new SettingsStoreOptions(Path.Join(AppPaths.LocalData, "settings.json")));
        builder.Services.AddSingleton<ISettingsService, JsonSettingsService>();

        // Platform services
        builder.Services.AddSingleton<IDispatcher, DispatcherQueueDispatcher>();
        builder.Services.AddSingleton<IStringResources, ResourceStringProvider>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<IErrorReporter, ErrorReporter>();
        builder.Services.AddSingleton<FrameNavigationService>();
        builder.Services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<FrameNavigationService>());
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
        builder.Services.AddSingleton<ICredentialStore, PasswordVaultCredentialStore>();
        builder.Services.AddSingleton<ContentDialogService>();
        builder.Services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<ContentDialogService>());

        // Servers
        builder.Services.AddSingleton<IServerManager, ServerManager>();

        // ViewModels
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<ServersViewModel>();

        // Views
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
