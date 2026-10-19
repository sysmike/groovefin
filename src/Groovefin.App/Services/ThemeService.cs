using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Groovefin.App.Services;

/// <summary>
/// Applies the theme stored in <see cref="ISettingsService"/> to every attached window, including its
/// caption buttons. High contrast is handled by the theme resources and always takes precedence.
/// </summary>
internal sealed class ThemeService : IThemeService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly List<Window> _windows = [];

    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
    }

    public AppTheme Theme => _settings.Current.Appearance.Theme;

    public event EventHandler? ThemeChanged;

    public void Attach(Window window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        Apply(window);
    }

    public void SetTheme(AppTheme theme) =>
        _settings.Update(s => s with { Appearance = s.Appearance with { Theme = theme } });

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Appearance.Theme == e.NewSettings.Appearance.Theme)
        {
            return;
        }

        foreach (var window in _windows)
        {
            Apply(window);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
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
