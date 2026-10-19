using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>An entry of the music library choice; <see cref="Id"/> is <see langword="null"/> for "all music libraries".</summary>
public sealed record LibraryOption(string? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Settings page: theme, account, servers and the music library to browse. The full page is tracked in #53.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly AppTheme[] ThemeOrder = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    private readonly IThemeService _themeService;
    private readonly INavigationService _navigation;
    private readonly IServerManager _servers;
    private readonly ILibraryService _library;
    private readonly IStringResources _strings;
    private readonly IErrorReporter _errors;
    private bool _loadingLibraries;

    public SettingsViewModel(
        IThemeService themeService,
        INavigationService navigation,
        IServerManager servers,
        ILibraryService library,
        IStringResources strings,
        IErrorReporter errors,
        AppVersion version)
    {
        _themeService = themeService;
        _navigation = navigation;
        _servers = servers;
        _library = library;
        _strings = strings;
        _errors = errors;
        Version = version;
        Theme = themeService.Theme;
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
