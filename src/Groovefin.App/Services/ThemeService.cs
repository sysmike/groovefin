using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Groovefin.App.Services;

/// <summary>
/// Applies the theme stored in <see cref="ISettingsService"/> to every attached window, including its
/// caption buttons, and the accent colour choice. High contrast is handled by the theme resources and
/// always takes precedence.
/// </summary>
/// <remarks>
/// With the accent colour off, the app resources override the shades of the Windows accent colour that
/// the theme brushes are built from with neutral greys: dark ones for light theme fills, light ones for
/// dark theme fills, so text on them keeps its contrast. High contrast themes use system colours instead.
/// The compact density adds the Windows App SDK's compact control styles and shrinks the list row
/// resources from <c>App.xaml</c>.
/// </remarks>
internal sealed class ThemeService : IThemeService, IDisposable
{
    private static readonly (string Key, Color Color)[] NeutralAccent =
    [
        ("SystemAccentColor", ColorHelper.FromArgb(0xFF, 0x80, 0x80, 0x80)),
        ("SystemAccentColorLight1", ColorHelper.FromArgb(0xFF, 0x9E, 0x9E, 0x9E)),
        ("SystemAccentColorLight2", ColorHelper.FromArgb(0xFF, 0xC4, 0xC4, 0xC4)),
        ("SystemAccentColorLight3", ColorHelper.FromArgb(0xFF, 0xDC, 0xDC, 0xDC)),
        ("SystemAccentColorDark1", ColorHelper.FromArgb(0xFF, 0x5F, 0x5F, 0x5F)),
        ("SystemAccentColorDark2", ColorHelper.FromArgb(0xFF, 0x47, 0x47, 0x47)),
        ("SystemAccentColorDark3", ColorHelper.FromArgb(0xFF, 0x33, 0x33, 0x33)),
    ];

    private static readonly Uri CompactStyles = new("ms-appx:///Microsoft.UI.Xaml/DensityStyles/Compact.xaml");

    private readonly ISettingsService _settings;
    private readonly List<Window> _windows = [];
    private ResourceDictionary? _compactStyles;

    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
    }

    public AppTheme Theme => _settings.Current.Appearance.Theme;

    public event EventHandler? ThemeChanged;

    public void Attach(Window window)
    {
        if (_windows.Count == 0)
        {
            ApplyAccent();
            ApplyDensity();
        }

        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        Apply(window);
    }

    public void SetTheme(AppTheme theme) =>
        _settings.Update(s => s with { Appearance = s.Appearance with { Theme = theme } });

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var themeChanged = e.OldSettings.Appearance.Theme != e.NewSettings.Appearance.Theme;
        var accentChanged = e.OldSettings.Appearance.UseAccentColor != e.NewSettings.Appearance.UseAccentColor;
        var densityChanged = e.OldSettings.Appearance.Density != e.NewSettings.Appearance.Density;
        if (!themeChanged && !accentChanged && !densityChanged)
        {
            return;
        }

        if (accentChanged)
        {
            ApplyAccent();
        }

        if (densityChanged)
        {
            ApplyDensity();
        }

        foreach (var window in _windows)
        {
            if (accentChanged || densityChanged)
            {
                Refresh(window);
            }

            Apply(window);
        }

        if (themeChanged)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyAccent()
    {
        var resources = Application.Current.Resources;
        foreach (var (key, color) in NeutralAccent)
        {
            if (_settings.Current.Appearance.UseAccentColor)
            {
                resources.Remove(key);
            }
            else
            {
                resources[key] = color;
            }
        }
    }

    private void ApplyDensity()
    {
        var compact = _settings.Current.Appearance.Density == ListDensity.Compact;
        var resources = Application.Current.Resources;
        resources["ListThumbnailSize"] = compact ? 32d : 40d;
        resources["ListRowPadding"] = compact ? new Thickness(0, 2, 0, 2) : new Thickness(0, 4, 0, 4);
        resources["ListItemPadding"] = compact ? new Thickness(8, 2, 8, 2) : new Thickness(8, 4, 8, 4);
        resources["TrackRowPadding"] = compact ? new Thickness(0, 2, 0, 2) : new Thickness(0, 6, 0, 6);
        resources["TableRowHeight"] = compact ? 28d : 36d;

        if (compact && _compactStyles is null)
        {
            _compactStyles = new ResourceDictionary { Source = CompactStyles };
            resources.MergedDictionaries.Add(_compactStyles);
        }
        else if (!compact && _compactStyles is not null)
        {
            resources.MergedDictionaries.Remove(_compactStyles);
            _compactStyles = null;
        }
    }

    /// <summary>
    /// Theme resources are looked up again when the theme changes, so switching to the other theme
    /// (and back in <see cref="Apply"/>, before the next frame) makes open pages pick up the new accent or density.
    /// </summary>
    private static void Refresh(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        }
    }

    private void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        window.AppWindow.TitleBar.PreferredTheme = Theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }
}
