using Groovefin.Core.Shortcuts;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Groovefin.App.Services;

/// <summary>The keys held now, for shortcuts: the window runs them, the change dialog records them (#68).</summary>
internal static class KeyboardInput
{
    /// <summary>Gets <paramref name="key"/> with the modifiers held right now.</summary>
    public static KeyGesture Gesture(VirtualKey key) =>
        new(key.ToString(), IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift));

    /// <summary>Gets a value indicating whether a Windows key is held: those combinations belong to Windows.</summary>
    public static bool IsWindowsKeyDown => IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows);

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
