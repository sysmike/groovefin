namespace Groovefin.Core.Services;

/// <summary>The theme the user picked for the app.</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows app mode (light or dark). High contrast always wins.</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// Applies the app theme to all windows.
/// </summary>
public interface IThemeService
{
    /// <summary>Gets the theme that is currently requested.</summary>
    AppTheme Theme { get; }

    /// <summary>Raised after <see cref="Theme"/> has changed.</summary>
    event EventHandler? ThemeChanged;

    /// <summary>Applies <paramref name="theme"/> to all windows.</summary>
    void SetTheme(AppTheme theme);
}
