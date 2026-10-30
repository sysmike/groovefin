using CommunityToolkit.Mvvm.Messaging;
using Groovefin.App.Services;
using Groovefin.Core;
using Groovefin.Core.Discord;
using Groovefin.Core.Equalizer;
using Groovefin.Core.Favourites;
using Groovefin.Core.History;
using Groovefin.Core.Images;
using Groovefin.Core.Menus;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Queue;
using Groovefin.Core.Security;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Session;
using Groovefin.Core.Settings;
using Groovefin.Core.Shortcuts;
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
        builder.Services.AddSingleton<ShortcutService>();

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
        builder.Services.AddSingleton<ILauncherService, LauncherService>();

        // Playback
        builder.Services.AddSingleton<MediaPlayerEngine>();
        builder.Services.AddSingleton<IPlaybackEngine>(sp => sp.GetRequiredService<MediaPlayerEngine>());
        builder.Services.AddSingleton<IAudioEqualizer>(sp => sp.GetRequiredService<MediaPlayerEngine>());
        builder.Services.AddSingleton<EqualizerSync>();
        builder.Services.AddSingleton(_ => new QueueManager());
        builder.Services.AddSingleton<Player>();
        builder.Services.AddSingleton<IPlayer>(sp => sp.GetRequiredService<Player>());
        // Privacy mode (#234): not saved, so it is off at every start.
        builder.Services.AddSingleton<PrivacyMode>();
        builder.Services.AddSingleton<PlaybackReporting>();
        // The plays behind "On repeat" (#251): a file per server and user.
        builder.Services.AddSingleton(new PlayHistoryStoreOptions(Path.Join(AppPaths.LocalData, "history")));
        builder.Services.AddSingleton<IPlayHistoryStore, JsonPlayHistoryStore>();
        builder.Services.AddSingleton<PlayHistory>();
        builder.Services.AddSingleton<ISystemMediaControls, SystemMediaControls>();
        builder.Services.AddSingleton<SystemMediaControlsSync>();
        builder.Services.AddSingleton(new SessionStoreOptions(Path.Join(AppPaths.LocalData, "session.json")));
        builder.Services.AddSingleton<ISessionStore, JsonSessionStore>();
        builder.Services.AddSingleton<SessionPersistence>();
        builder.Services.AddSingleton<ITaskbar>(sp => new Win32Taskbar(
            WinRT.Interop.WindowNative.GetWindowHandle(sp.GetRequiredService<MainWindow>()),
            Path.Join(AppContext.BaseDirectory, "Assets", "Taskbar"),
            sp.GetRequiredService<IStringResources>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Win32Taskbar>>()));
        builder.Services.AddSingleton<TaskbarSync>();

        // Discord status over the local pipe (no key, no network); off until the user turns it on.
        builder.Services.AddSingleton(DiscordApplication.Groovefin);
        builder.Services.AddSingleton<IDiscordConnection, DiscordIpcConnection>();
        builder.Services.AddSingleton<DiscordPresence>();
        builder.Services.AddSingleton<NowPlayingViewModel>();
        builder.Services.AddSingleton<QueueViewModel>();
        builder.Services.AddSingleton<LyricsViewModel>();
        builder.Services.AddSingleton<SearchSuggestionsViewModel>();
        builder.Services.AddSingleton<AddToPlaylist>();
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddSingleton<FavouritesService>();
        builder.Services.AddSingleton<FavouritePlaylists>();
        builder.Services.AddSingleton<InstantMix>();
        builder.Services.AddSingleton<TrackDrag>();
        builder.Services.AddSingleton<CurrentTrack>();
        builder.Services.AddSingleton<ContextMenus>();
        builder.Services.AddSingleton<QuickPlay>();

        // Servers
        builder.Services.AddSingleton<IServerManager, ServerManager>();

        // ViewModels. The disposable page view models (Home, Albums, Artists, Songs, Servers) are not
        // registered: pages create them with App.CreateViewModel, so the container does not keep them (#147).
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<AlbumDetailViewModel>();
        builder.Services.AddTransient<AlbumPreviewViewModel>();
        builder.Services.AddTransient<ArtistDetailViewModel>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<PlaylistsViewModel>();
        builder.Services.AddTransient<PlaylistDetailViewModel>();
        builder.Services.AddTransient<FavouritesViewModel>();
        builder.Services.AddTransient<OnRepeatViewModel>();
        builder.Services.AddTransient<GenresViewModel>();
        builder.Services.AddTransient<GenreDetailViewModel>();
        builder.Services.AddTransient<FoldersViewModel>();
        builder.Services.AddTransient<SignInViewModel>();

        // Views
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
