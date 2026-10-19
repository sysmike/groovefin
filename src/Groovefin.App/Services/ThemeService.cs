using Groovefin.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Groovefin.App.Services;

/// <summary>
/// Applies the requested theme to every attached window, including its caption buttons.
/// High contrast is handled by the theme resources and always takes precedence.
/// </summary>
internal sealed class ThemeService : IThemeService
{
    private readonly List<Window> _windows = [];

    // TODO(#11): persist the selected theme through ISettingsService.
    public AppTheme Theme { get; private set; } = AppTheme.System;

    public event EventHandler? ThemeChanged;

    public void Attach(Window window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        Apply(window);
    }

    public void SetTheme(AppTheme theme)
    {
        if (theme == Theme)
        {
            return;
        }

        Theme = theme;
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
