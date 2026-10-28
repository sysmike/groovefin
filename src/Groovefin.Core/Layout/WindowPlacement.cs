using Groovefin.Core.Settings;

namespace Groovefin.Core.Layout;

/// <summary>A rectangle in physical pixels, as the window APIs give and take it.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    /// <summary>Gets the area this rectangle shares with <paramref name="other"/>.</summary>
    public long OverlapWith(PixelRect other) =>
        (long)Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X)) * Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
}

/// <summary>A screen: its work area (without the taskbar) in physical pixels, and its DPI (96 at 100 %).</summary>
public sealed record ScreenArea(PixelRect WorkArea, int Dpi, bool IsPrimary);

/// <summary>How the window shows.</summary>
public enum WindowShowState
{
    Normal,
    Maximized,
    Minimized,
}

/// <summary>
/// The main window's place, size and maximized state, kept for the next start (#272). It follows the full
/// window only: while the mini player or full screen shows, or the window is minimized, it keeps the full
/// window's last placement, so that is what closing saves.
/// </summary>
public sealed class WindowPlacement
{
    /// <summary>The share of the window that must still be on a screen for its saved place to be used.</summary>
    public const double MinVisibleShare = 0.5;

    private PixelRect? _normalBounds;
    private bool _isMaximized;
    private int _dpi = 96;

    /// <summary>Notes the window's state: call on each move, resize and presenter change.</summary>
    /// <param name="isFullWindow">Whether the full window shows (not the mini player, not full screen).</param>
    /// <param name="state">Whether it is maximized or minimized.</param>
    /// <param name="bounds">Its bounds now.</param>
    /// <param name="dpi">The DPI of its screen now.</param>
    public void Observe(bool isFullWindow, WindowShowState state, PixelRect bounds, int dpi)
    {
        if (!isFullWindow || state == WindowShowState.Minimized)
        {
            return;
        }

        _isMaximized = state == WindowShowState.Maximized;

        // Maximized, the bounds are the screen's: the size to come back to is the one before.
        if (!_isMaximized && bounds.Width > 0 && bounds.Height > 0)
        {
            _normalBounds = bounds;
            _dpi = dpi > 0 ? dpi : 96;
        }
    }

    /// <summary>Gets the placement to save, or <see langword="null"/> before the full window was seen.</summary>
    public WindowSettings? ToSettings() => _normalBounds is { } b
        ? new WindowSettings { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, IsMaximized = _isMaximized, Dpi = _dpi }
        : null;

    /// <summary>
    /// Gets the bounds to open the window with from the saved placement: in its old place if at least
    /// <see cref="MinVisibleShare"/> of it is still on a screen, otherwise centred on the primary screen. The size
    /// follows a changed DPI, is at least <paramref name="minWidth"/> × <paramref name="minHeight"/> (effective
    /// pixels) and at most the screen's work area, and the window is moved fully onto that screen.
    /// </summary>
    public static PixelRect Restore(WindowSettings saved, IReadOnlyList<ScreenArea> screens, int minWidth, int minHeight)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(screens);
        var rect = new PixelRect(saved.X, saved.Y, saved.Width, saved.Height);
        if (screens.Count == 0)
        {
            return rect;
        }

        var best = screens.MaxBy(s => s.WorkArea.OverlapWith(rect))!;
        var keepsPlace = rect.Area > 0 && best.WorkArea.OverlapWith(rect) >= rect.Area * MinVisibleShare;
        var screen = keepsPlace ? best : screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
        var area = screen.WorkArea;

        var scale = saved.Dpi > 0 ? (double)screen.Dpi / saved.Dpi : 1;
        var width = Fit((int)Math.Round(rect.Width * scale), Scale(minWidth, screen.Dpi), area.Width);
        var height = Fit((int)Math.Round(rect.Height * scale), Scale(minHeight, screen.Dpi), area.Height);

        var x = keepsPlace ? rect.X : area.X + ((area.Width - width) / 2);
        var y = keepsPlace ? rect.Y : area.Y + ((area.Height - height) / 2);
        x = Math.Clamp(x, area.X, area.Right - width);
        y = Math.Clamp(y, area.Y, area.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    private static int Scale(int effective, int dpi) => (int)Math.Ceiling((double)effective * (dpi > 0 ? dpi : 96) / 96);

    private static int Fit(int size, int min, int max) => Math.Min(Math.Max(size, min), max);
}
