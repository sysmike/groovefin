using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// Settings page. M0 only exposes the theme; the full page is tracked in #53.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly AppTheme[] ThemeOrder = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    private readonly IThemeService _themeService;
    private readonly INavigationService _navigation;
    private readonly IServerManager _servers;

    public SettingsViewModel(IThemeService themeService, INavigationService navigation, IServerManager servers, AppVersion version)
    {
        _themeService = themeService;
        _navigation = navigation;
        _servers = servers;
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
