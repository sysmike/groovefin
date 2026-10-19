using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Navigation;
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

    public SettingsViewModel(IThemeService themeService, INavigationService navigation, AppVersion version)
    {
        _themeService = themeService;
        _navigation = navigation;
        Version = version;
        Theme = themeService.Theme;
    }

    public AppVersion Version { get; }

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

    partial void OnThemeChanged(AppTheme value)
    {
        if (value != _themeService.Theme)
        {
            _themeService.SetTheme(value);
        }
    }
}
