using CommunityToolkit.Mvvm.Messaging;
using Groovefin.App.Services;
using Groovefin.Core;
using Groovefin.Core.Favourites;
using Groovefin.Core.Images;
using Groovefin.Core.Menus;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Security;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Groovefin.Jellyfin;
using Groovefin.Playback.Windows;
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

        // Image cache: Cache\Images in local app data, size limit from settings (applies immediately).
        builder.Services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new ImageCacheOptions(
                Path.Join(AppPaths.LocalData, "Cache", "Images"),
                () => settings.Current.Cache.ImageCacheSizeMegabytes * 1024L * 1024L);
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IImageCache, DiskImageCache>();

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

        // Playback
        builder.Services.AddSingleton<MediaPlayerEngine>();
        builder.Services.AddSingleton<IPlaybackEngine>(sp => sp.GetRequiredService<MediaPlayerEngine>());
        builder.Services.AddSingleton(_ => new QueueManager());
        builder.Services.AddSingleton<Player>();
        builder.Services.AddSingleton<IPlayer>(sp => sp.GetRequiredService<Player>());
        builder.Services.AddSingleton<PlaybackReporting>();
        builder.Services.AddSingleton<ISystemMediaControls, SystemMediaControls>();
        builder.Services.AddSingleton<SystemMediaControlsSync>();
        builder.Services.AddSingleton<NowPlayingViewModel>();
        builder.Services.AddSingleton<QueueViewModel>();
        builder.Services.AddSingleton<SearchSuggestionsViewModel>();
        builder.Services.AddSingleton<AddToPlaylist>();
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddSingleton<FavouritesService>();
        builder.Services.AddSingleton<InstantMix>();
        builder.Services.AddSingleton<ContextMenus>();

        // Servers
        builder.Services.AddSingleton<IServerManager, ServerManager>();

        // ViewModels
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<AlbumsViewModel>();
        builder.Services.AddTransient<AlbumDetailViewModel>();
        builder.Services.AddTransient<ArtistsViewModel>();
        builder.Services.AddTransient<ArtistDetailViewModel>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<PlaylistsViewModel>();
        builder.Services.AddTransient<PlaylistDetailViewModel>();
        builder.Services.AddTransient<FavouritesViewModel>();
        builder.Services.AddTransient<GenresViewModel>();
        builder.Services.AddTransient<GenreDetailViewModel>();
        builder.Services.AddTransient<ServersViewModel>();
        builder.Services.AddTransient<SignInViewModel>();

        // Views
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
