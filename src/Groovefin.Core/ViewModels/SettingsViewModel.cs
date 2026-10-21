using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.About;
using Groovefin.Core.Api;
using Groovefin.Core.Discord;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>An entry of a bitrate choice; <see cref="Kbps"/> is <see langword="null"/> for "original quality".</summary>
public sealed record BitrateOption(int? Kbps, string Label)
{
    public override string ToString() => Label;
}

/// <summary>An entry of the music library choice; <see cref="Id"/> is <see langword="null"/> for "all music libraries".</summary>
public sealed record LibraryOption(string? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Settings page: appearance (theme, accent colour, density, language, start page), account and servers, the music library to browse, stream quality,
/// playback, storage (image cache and logs) and About. The keyboard shortcuts have their own <see cref="ShortcutsViewModel"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly AppTheme[] ThemeOrder = [AppTheme.System, AppTheme.Light, AppTheme.Dark];
    private static readonly int?[] Bitrates = [null, 320, 256, 192, 128, 96, 64];
    private static readonly TranscodingCodec[] CodecOrder = [TranscodingCodec.Aac, TranscodingCodec.Mp3];
    private static readonly NormalizationMode[] NormalizationOrder = [NormalizationMode.Off, NormalizationMode.Track, NormalizationMode.Album];
    private static readonly InstantMixMode[] InstantMixOrder = [InstantMixMode.Replace, InstantMixMode.Append];
    private static readonly ListDensity[] DensityOrder = [ListDensity.Standard, ListDensity.Compact];
    private static readonly int[] CacheLimits = [100, 250, 500, 1000, 2000, 5000];
    private static readonly int[] CrossfadeSeconds = [0, 1, 2, 3, 4, 5, 6, 8, 10, 12];

    /// <summary>The UI languages: Windows' choice first, then each translation (<c>.resw</c> folder).</summary>
    private static readonly string?[] Languages = [null, "en-US", "de-DE"];

    private readonly IThemeService _themeService;
    private readonly INavigationService _navigation;
    private readonly IServerManager _servers;
    private readonly ILibraryService _library;
    private readonly IStringResources _strings;
    private readonly IErrorReporter _errors;
    private readonly ISettingsService _settings;
    private readonly IImageCache _cache;
    private readonly ILauncherService _launcher;
    private bool _loadingLibraries;

    public SettingsViewModel(
        IThemeService themeService,
        INavigationService navigation,
        IServerManager servers,
        ILibraryService library,
        IStringResources strings,
        IErrorReporter errors,
        ISettingsService settings,
        IImageCache cache,
        ILauncherService launcher,
        AppVersion version)
    {
        _themeService = themeService;
        _navigation = navigation;
        _servers = servers;
        _library = library;
        _strings = strings;
        _errors = errors;
        _settings = settings;
        _cache = cache;
        _launcher = launcher;
        Version = version;
        Theme = themeService.Theme;
        BitrateOptions = Bitrates
            .Select(kbps => new BitrateOption(
                kbps,
                kbps is { } value
                    ? strings.GetString("Settings_Kbps").Replace("{0}", value.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                    : strings.GetString("Settings_OriginalQuality")))
            .ToList();
        LanguageOptions =
        [
            strings.GetString("Settings_LanguageSystem"),
            strings.GetString("Settings_LanguageEnglish"),
            strings.GetString("Settings_LanguageGerman"),
        ];
        StartPageOptions = [.. PageKeys.MainSections.Select(key => strings.GetString($"StartPage_{key}"))];
        CacheLimitOptions = [.. CacheLimits.Select(mb => ByteSizes.Format(mb * 1024L * 1024L, strings))];
        CrossfadeOptions =
        [
            .. CrossfadeSeconds.Select(s => s == 0
                ? strings.GetString("Settings_CrossfadeOff")
                : strings.GetString("Settings_Seconds").Replace("{0}", s.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)),
        ];
    }

    /// <summary>Gets the bitrate choices: original quality first, then from high to low.</summary>
    public IReadOnlyList<BitrateOption> BitrateOptions { get; }

    /// <summary>Gets or sets the bitrate limit on the local network as an index into <see cref="BitrateOptions"/>.</summary>
    public int LocalBitrateIndex
    {
        get => Array.IndexOf(Bitrates, _settings.Current.Streaming.LocalMaxBitrateKbps);
        set
        {
            if (value >= 0 && value < Bitrates.Length && value != LocalBitrateIndex)
            {
                _settings.Update(s => s with { Streaming = s.Streaming with { LocalMaxBitrateKbps = Bitrates[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets the bitrate limit for remote servers as an index into <see cref="BitrateOptions"/>.</summary>
    public int RemoteBitrateIndex
    {
        get => Array.IndexOf(Bitrates, _settings.Current.Streaming.RemoteMaxBitrateKbps);
        set
        {
            if (value >= 0 && value < Bitrates.Length && value != RemoteBitrateIndex)
            {
                _settings.Update(s => s with { Streaming = s.Streaming with { RemoteMaxBitrateKbps = Bitrates[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets the transcoding codec as an index into AAC, MP3.</summary>
    public int CodecIndex
    {
        get => Array.IndexOf(CodecOrder, _settings.Current.Streaming.Codec);
        set
        {
            if (value >= 0 && value < CodecOrder.Length && value != CodecIndex)
            {
                _settings.Update(s => s with { Streaming = s.Streaming with { Codec = CodecOrder[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets the volume normalization as an index into off, track, album.</summary>
    public int NormalizationIndex
    {
        get => Array.IndexOf(NormalizationOrder, _settings.Current.Playback.Normalization);
        set
        {
            if (value >= 0 && value < NormalizationOrder.Length && value != NormalizationIndex)
            {
                _settings.Update(s => s with { Playback = s.Playback with { Normalization = NormalizationOrder[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets what an instant mix does to the queue, as an index into replace, append.</summary>
    public int InstantMixIndex
    {
        get => Array.IndexOf(InstantMixOrder, _settings.Current.Playback.InstantMix);
        set
        {
            if (value >= 0 && value < InstantMixOrder.Length && value != InstantMixIndex)
            {
                _settings.Update(s => s with { Playback = s.Playback with { InstantMix = InstantMixOrder[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the queue and position are restored (paused) on the next start.</summary>
    public bool ResumeSession
    {
        get => _settings.Current.Playback.ResumeSession;
        set
        {
            if (value != ResumeSession)
            {
                _settings.Update(s => s with { Playback = s.Playback with { ResumeSession = value } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets the crossfade choices: off, then seconds.</summary>
    public IReadOnlyList<string> CrossfadeOptions { get; }

    /// <summary>Gets or sets the crossfade as an index into <see cref="CrossfadeOptions"/>; a value from an edited file shows as the next longer choice.</summary>
    public int CrossfadeIndex
    {
        get
        {
            var seconds = _settings.Current.Playback.CrossfadeSeconds;
            var index = Array.FindIndex(CrossfadeSeconds, s => s >= seconds);
            return index < 0 ? CrossfadeSeconds.Length - 1 : index;
        }

        set
        {
            if (value >= 0 && value < CrossfadeSeconds.Length && value != CrossfadeIndex)
            {
                _settings.Update(s => s with { Playback = s.Playback with { CrossfadeSeconds = CrossfadeSeconds[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets a value indicating whether this build can show a Discord status (it has an application ID).</summary>
    public bool IsDiscordAvailable => DiscordApplication.Groovefin.IsSet;

    /// <summary>Gets or sets a value indicating whether Discord shows the playing track in the user's status.</summary>
    public bool DiscordPresence
    {
        get => _settings.Current.Playback.DiscordPresence;
        set
        {
            if (value != DiscordPresence)
            {
                _settings.Update(s => s with { Playback = s.Playback with { DiscordPresence = value } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the taskbar button shows the playback progress.</summary>
    public bool ShowTaskbarProgress
    {
        get => _settings.Current.Appearance.ShowTaskbarProgress;
        set
        {
            if (value != ShowTaskbarProgress)
            {
                _settings.Update(s => s with { Appearance = s.Appearance with { ShowTaskbarProgress = value } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the notification area icon is shown.</summary>
    public bool ShowTrayIcon
    {
        get => _settings.Current.Appearance.ShowTrayIcon;
        set
        {
            if (value != ShowTrayIcon)
            {
                _settings.Update(s => s with { Appearance = s.Appearance with { ShowTrayIcon = value } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether minimizing hides the window to the notification area (with the icon on).</summary>
    public bool MinimizeToTray
    {
        get => _settings.Current.Appearance.MinimizeToTray;
        set
        {
            if (value != MinimizeToTray)
            {
                _settings.Update(s => s with { Appearance = s.Appearance with { MinimizeToTray = value } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether highlights use the Windows accent colour (otherwise neutral grey).</summary>
    public bool UseAccentColor
    {
        get => _settings.Current.Appearance.UseAccentColor;
        set
        {
            if (value != UseAccentColor)
            {
                _settings.Update(s => s with { Appearance = s.Appearance with { UseAccentColor = value } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets the list density as an index into standard, compact.</summary>
    public int DensityIndex
    {
        get => Array.IndexOf(DensityOrder, _settings.Current.Appearance.Density);
        set
        {
            if (value >= 0 && value < DensityOrder.Length && value != DensityIndex)
            {
                _settings.Update(s => s with { Appearance = s.Appearance with { Density = DensityOrder[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets the UI languages to choose from: "Use Windows language", then each language in its own name.</summary>
    public IReadOnlyList<string> LanguageOptions { get; }

    /// <summary>Gets or sets the UI language as an index into <see cref="LanguageOptions"/>; applies on the next start.</summary>
    public int LanguageIndex
    {
        get => Math.Max(0, Array.IndexOf(Languages, _settings.Current.Appearance.Language));
        set
        {
            if (value >= 0 && value < Languages.Length && value != LanguageIndex)
            {
                _settings.Update(s => s with { Appearance = s.Appearance with { Language = Languages[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets the names of the main sections, the choices for the start page.</summary>
    public IReadOnlyList<string> StartPageOptions { get; }

    /// <summary>Gets or sets the start page as an index into <see cref="PageKeys.MainSections"/>.</summary>
    public int StartPageIndex
    {
        get => Math.Max(0, PageKeys.MainSections.ToList().IndexOf(_settings.Current.Appearance.StartPage));
        set
        {
            if (value >= 0 && value < PageKeys.MainSections.Count && value != StartPageIndex)
            {
                _settings.Update(s => s with { Appearance = s.Appearance with { StartPage = PageKeys.MainSections[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets the image cache size limits to choose from, e.g. "500 MB".</summary>
    public IReadOnlyList<string> CacheLimitOptions { get; }

    /// <summary>Gets or sets the image cache size limit as an index into <see cref="CacheLimitOptions"/>.</summary>
    public int CacheLimitIndex
    {
        get => Array.IndexOf(CacheLimits, _settings.Current.Cache.ImageCacheSizeMegabytes);
        set
        {
            if (value >= 0 && value < CacheLimits.Length && value != CacheLimitIndex)
            {
                _settings.Update(s => s with { Cache = s.Cache with { ImageCacheSizeMegabytes = CacheLimits[value] } });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets how much the image cache uses, e.g. "120 MB used"; empty until measured.</summary>
    [ObservableProperty]
    public partial string CacheSizeText { get; private set; } = string.Empty;

    public AppVersion Version { get; }

    /// <summary>Gets the version for display, e.g. "Version 1.0.0".</summary>
    public string VersionText => _strings.GetString("Settings_Version").Replace("{0}", Version.Version, StringComparison.Ordinal);

    /// <summary>Gets the page of the commit the app was built from, or <see langword="null"/> when unknown.</summary>
    public Uri? CommitUri => Version.Commit is { } sha ? ProjectLinks.Commit(sha) : null;

    public bool HasCommit => Version.Commit is not null;

    public Uri RepositoryUri => ProjectLinks.Repository;

    public Uri IssuesUri => ProjectLinks.Issues;

    /// <summary>Gets the third-party components in the app and their licenses.</summary>
    public IReadOnlyList<ThirdPartyComponent> Licenses => ThirdPartyNotices.All;

    /// <summary>Gets the signed-in user name, or <see langword="null"/> when nobody is signed in.</summary>
    public string? UserName => _servers.IsSignedIn ? _servers.ActiveServer?.UserName : null;

    /// <summary>Gets the active server's name.</summary>
    public string? ServerName => _servers.ActiveServer?.Name;

    public bool IsSignedIn => _servers.IsSignedIn;

    /// <summary>Gets the selectable themes in display order.</summary>
    public IReadOnlyList<AppTheme> Themes => ThemeOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeIndex))]
    public partial AppTheme Theme { get; set; }

    /// <summary>Gets or sets <see cref="Theme"/> as an index into <see cref="Themes"/> (for radio button groups).</summary>
    public int ThemeIndex
    {
        get => Array.IndexOf(ThemeOrder, Theme);
        set
        {
            if (value >= 0 && value < ThemeOrder.Length)
            {
                Theme = ThemeOrder[value];
            }
        }
    }

    /// <summary>Gets the music library choices: "all music libraries" first, then each library.</summary>
    public ObservableCollection<LibraryOption> Libraries { get; } = [];

    /// <summary>Gets a value indicating whether there is something to choose (more than one music library).</summary>
    [ObservableProperty]
    public partial bool HasLibraryChoice { get; private set; }

    /// <summary>Gets or sets the selected library; changing it limits browsing on the active server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLibraryIndex))]
    public partial LibraryOption? SelectedLibrary { get; set; }

    /// <summary>Gets or sets <see cref="SelectedLibrary"/> as an index into <see cref="Libraries"/> (for the combo box).</summary>
    public int SelectedLibraryIndex
    {
        get => SelectedLibrary is null ? -1 : Libraries.IndexOf(SelectedLibrary);
        set
        {
            if (value >= 0 && value < Libraries.Count)
            {
                SelectedLibrary = Libraries[value];
            }
        }
    }

    /// <summary>Loads the active server's music libraries and selects the stored choice.</summary>
    public async Task LoadLibrariesAsync(CancellationToken cancellationToken = default)
    {
        if (!_servers.IsSignedIn || _servers.ActiveServer is not { } server)
        {
            return;
        }

        IReadOnlyList<MusicLibrary> libraries;
        try
        {
            libraries = await _library.GetMusicLibrariesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            _errors.Report(ex, Operations.LoadLibraries);
            return;
        }

        _loadingLibraries = true;
        try
        {
            Libraries.Clear();
            Libraries.Add(new LibraryOption(null, _strings.GetString("Settings_AllLibraries")));
            foreach (var library in libraries)
            {
                Libraries.Add(new LibraryOption(library.Id, library.Name));
            }

            // A library that was removed on the server falls back to "all".
            SelectedLibrary = Libraries.FirstOrDefault(o => o.Id == server.LibraryId) ?? Libraries[0];
            HasLibraryChoice = libraries.Count > 1;
        }
        finally
        {
            _loadingLibraries = false;
        }
    }

    partial void OnSelectedLibraryChanged(LibraryOption? value)
    {
        if (!_loadingLibraries && value is not null && _servers.ActiveServer is { } server)
        {
            _servers.SetLibrary(server.Id, value.Id);
        }
    }

    /// <summary>Measures the image cache for <see cref="CacheSizeText"/>.</summary>
    public async Task RefreshCacheSizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await _cache.GetSizeAsync(cancellationToken);
            CacheSizeText = _strings.GetString("Settings_CacheUsed").Replace("{0}", ByteSizes.Format(bytes, _strings), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Files can disappear while they are counted (eviction); the size is shown on the next visit.
            CacheSizeText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cache.ClearAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Report(ex, Operations.ClearCache);
        }

        await RefreshCacheSizeAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        try
        {
            await _launcher.OpenLogFolderAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Report(ex, Operations.OpenLogFolder);
        }
    }

    [RelayCommand]
    private void ManageServers() => _navigation.NavigateTo(PageKeys.Servers);

    [RelayCommand(CanExecute = nameof(IsSignedIn))]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await _servers.SignOutAsync(cancellationToken);
        _navigation.NavigateTo(PageKeys.SignIn);
    }

    partial void OnThemeChanged(AppTheme value)
    {
        if (value != _themeService.Theme)
        {
            _themeService.SetTheme(value);
        }
    }
}
