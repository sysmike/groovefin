using System.Globalization;
using Groovefin.Core.Services;

namespace Groovefin.Core.Shortcuts;

/// <summary>What an app-wide keyboard shortcut does.</summary>
public enum ShortcutAction
{
    PlayPause,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    Search,
    Queue,
    Lyrics,
    Shuffle,
    Repeat,
    FullScreen,
    MiniPlayer,
    GoBack,
    GoForward,
    PrivacyMode,
}

/// <summary>An action and the keys that run it.</summary>
public sealed record KeyboardShortcut(ShortcutAction Action, KeyGesture Gesture);

/// <summary>Whether keys can be a shortcut (<see cref="KeyboardShortcuts.Check"/>).</summary>
public enum GestureCheck
{
    /// <summary>The keys can be a shortcut.</summary>
    Valid,

    /// <summary>Only modifiers so far (Ctrl, Alt, Shift): the key is still to come.</summary>
    Incomplete,

    /// <summary>A key that is never a shortcut, such as Tab, Enter or Caps Lock.</summary>
    Unsupported,

    /// <summary>A letter, digit or navigation key without Ctrl or Alt: lists, menus and combo boxes use those.</summary>
    NeedsModifier,

    /// <summary>A combination Windows uses (Alt+F4, Alt+Space).</summary>
    Reserved,
}

/// <summary>
/// The app-wide keyboard shortcuts: their defaults, which keys a shortcut may use, and how keys are shown.
/// <see cref="ShortcutMap"/> holds the ones in effect, with the user's changes. None of them fire while the
/// user types in a text box.
/// </summary>
public static class KeyboardShortcuts
{
    public static IReadOnlyList<KeyboardShortcut> Defaults { get; } =
    [
        new(ShortcutAction.PlayPause, new("Space")),
        new(ShortcutAction.Next, new("Right", Control: true)),
        new(ShortcutAction.Previous, new("Left", Control: true)),
        new(ShortcutAction.VolumeUp, new("Up", Control: true)),
        new(ShortcutAction.VolumeDown, new("Down", Control: true)),
        new(ShortcutAction.Search, new("K", Control: true)),
        new(ShortcutAction.Queue, new("Q", Control: true)),
        new(ShortcutAction.Lyrics, new("L", Control: true)),
        new(ShortcutAction.Shuffle, new("S", Control: true)),
        new(ShortcutAction.Repeat, new("R", Control: true)),
        new(ShortcutAction.FullScreen, new("F11")),
        new(ShortcutAction.MiniPlayer, new("M", Control: true)),
        new(ShortcutAction.GoBack, new("Left", Alt: true)),
        new(ShortcutAction.GoForward, new("Right", Alt: true)),
        new(ShortcutAction.PrivacyMode, new("P", Control: true, Shift: true)),
    ];

    /// <summary>Keys shown by a name of their own (<c>Key_{name}</c> in the strings); they need Ctrl or Alt, except Space.</summary>
    public static IReadOnlyList<string> NamedKeys { get; } =
        ["Space", "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown", "Insert", "Delete", "Add", "Subtract", "Multiply", "Divide"];

    private static readonly HashSet<string> Modifiers = new(
        ["Control", "Shift", "Menu", "LeftControl", "RightControl", "LeftShift", "RightShift", "LeftMenu", "RightMenu", "LeftWindows", "RightWindows"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the default keys of <paramref name="action"/>.</summary>
    public static KeyGesture DefaultOf(ShortcutAction action) => Defaults.Single(s => s.Action == action).Gesture;

    /// <summary>
    /// Whether <paramref name="gesture"/> can be a shortcut. Function keys and Space work alone; letters,
    /// digits and navigation keys need Ctrl or Alt, because lists, menus and combo boxes use them.
    /// </summary>
    public static GestureCheck Check(KeyGesture gesture)
    {
        var key = gesture.Key ?? string.Empty;
        if (Modifiers.Contains(key))
        {
            return GestureCheck.Incomplete;
        }

        if (IsFunctionKey(key) || Is(key, "Space"))
        {
            // Alt+F4 closes the window, Alt+Space opens its system menu.
            return gesture.Alt && !gesture.Control && (Is(key, "F4") || Is(key, "Space")) ? GestureCheck.Reserved : GestureCheck.Valid;
        }

        if (IsLetter(key) || DigitOf(key) is not null || NamedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            return gesture.IsBare ? GestureCheck.NeedsModifier : GestureCheck.Valid;
        }

        return GestureCheck.Unsupported;
    }

    /// <summary>Gets the keys as shown to the user, e.g. "Ctrl+Right" ("Strg+Nach-rechts" in German).</summary>
    public static string Format(KeyGesture gesture, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        var parts = new List<string>(4);
        if (gesture.Control)
        {
            parts.Add(strings.GetString("Key_Ctrl"));
        }

        if (gesture.Alt)
        {
            parts.Add(strings.GetString("Key_Alt"));
        }

        if (gesture.Shift)
        {
            parts.Add(strings.GetString("Key_Shift"));
        }

        parts.Add(KeyName(gesture.Key ?? string.Empty, strings));
        return string.Join('+', parts);
    }

    /// <summary>Gets what the shortcut does, e.g. "Next track".</summary>
    public static string Describe(ShortcutAction action, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        return strings.GetString($"Shortcut_{action}");
    }

    private static string KeyName(string key, IStringResources strings)
    {
        if (key.Length == 1)
        {
            return key.ToUpperInvariant();
        }

        if (key.StartsWith("NumberPad", StringComparison.OrdinalIgnoreCase) && DigitOf(key) is { } pad)
        {
            return strings.GetString("Key_NumberPad").Replace("{0}", pad, StringComparison.Ordinal);
        }

        if (DigitOf(key) is { } digit)
        {
            return digit;
        }

        var named = NamedKeys.FirstOrDefault(k => Is(k, key));
        return named is null ? key : strings.GetString($"Key_{named}");
    }

    private static bool Is(string key, string name) => string.Equals(key, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsLetter(string key) => key.Length == 1 && char.IsAsciiLetter(key[0]);

    private static bool IsFunctionKey(string key) =>
        key.Length is 2 or 3 && (key[0] is 'F' or 'f')
        && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24;

    /// <summary>"Number5" and "NumberPad5" are the digit 5.</summary>
    private static string? DigitOf(string key)
    {
        foreach (var prefix in new[] { "NumberPad", "Number" })
        {
            if (key.Length == prefix.Length + 1 && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(key[^1]))
            {
                return key[^1..];
            }
        }

        return null;
    }
}
