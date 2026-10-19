using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
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
/// Settings page: theme, account, servers, the music library to browse, stream quality and volume
/// normalization. The full page is tracked in #53.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly AppTheme[] ThemeOrder = [AppTheme.System, AppTheme.Light, AppTheme.Dark];
    private static readonly int?[] Bitrates = [null, 320, 256, 192, 128, 96, 64];
    private static readonly TranscodingCodec[] CodecOrder = [TranscodingCodec.Aac, TranscodingCodec.Mp3];
    private static readonly NormalizationMode[] NormalizationOrder = [NormalizationMode.Off, NormalizationMode.Track, NormalizationMode.Album];

    private readonly IThemeService _themeService;
    private readonly INavigationService _navigation;
    private readonly IServerManager _servers;
    private readonly ILibraryService _library;
    private readonly IStringResources _strings;
    private readonly IErrorReporter _errors;
    private readonly ISettingsService _settings;
    private bool _loadingLibraries;

    public SettingsViewModel(
        IThemeService themeService,
        INavigationService navigation,
        IServerManager servers,
        ILibraryService library,
        IStringResources strings,
        IErrorReporter errors,
        ISettingsService settings,
        AppVersion version)
    {
        _themeService = themeService;
        _navigation = navigation;
        _servers = servers;
        _library = library;
        _strings = strings;
        _errors = errors;
        _settings = settings;
        Version = version;
        Theme = themeService.Theme;
        BitrateOptions = Bitrates
            .Select(kbps => new BitrateOption(
                kbps,
                kbps is { } value
                    ? strings.GetString("Settings_Kbps").Replace("{0}", value.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
                    : strings.GetString("Settings_OriginalQuality")))
            .ToList();
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

    public AppVersion Version { get; }

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
