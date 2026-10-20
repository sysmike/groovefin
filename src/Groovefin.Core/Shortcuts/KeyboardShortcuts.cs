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
}

/// <summary>A shortcut: Ctrl (or none) plus a key such as "Right", "Space" or "K".</summary>
public sealed record KeyboardShortcut(ShortcutAction Action, bool Control, string Key);

/// <summary>
/// The app-wide keyboard shortcuts. The window maps keys to <see cref="ShortcutAction"/>s with
/// <see cref="Find"/>; Settings lists them with <see cref="Describe"/>. None of them fire while the user
/// types in a text box.
/// </summary>
public static class KeyboardShortcuts
{
    public static IReadOnlyList<KeyboardShortcut> All { get; } =
    [
        new(ShortcutAction.PlayPause, false, "Space"),
        new(ShortcutAction.Next, true, "Right"),
        new(ShortcutAction.Previous, true, "Left"),
        new(ShortcutAction.VolumeUp, true, "Up"),
        new(ShortcutAction.VolumeDown, true, "Down"),
        new(ShortcutAction.Search, true, "K"),
        new(ShortcutAction.Queue, true, "Q"),
        new(ShortcutAction.Lyrics, true, "L"),
        new(ShortcutAction.Shuffle, true, "S"),
        new(ShortcutAction.Repeat, true, "R"),
        new(ShortcutAction.FullScreen, false, "F11"),
        new(ShortcutAction.MiniPlayer, true, "M"),
    ];

    /// <summary>Gets the action of <paramref name="key"/> (with or without Ctrl), or <see langword="null"/>.</summary>
    public static ShortcutAction? Find(string key, bool control) =>
        All.FirstOrDefault(s => s.Control == control && string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))?.Action;

    /// <summary>Gets the keys as shown to the user, e.g. "Ctrl+Right" ("Strg+Nach-rechts" in German).</summary>
    public static string Gesture(KeyboardShortcut shortcut, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(strings);
        var key = shortcut.Key.Length == 1 ? shortcut.Key : strings.GetString($"Key_{shortcut.Key}");
        return shortcut.Control ? strings.GetString("Key_Ctrl") + "+" + key : key;
    }

    /// <summary>Gets what the shortcut does, e.g. "Next track".</summary>
    public static string Describe(KeyboardShortcut shortcut, IStringResources strings)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(strings);
        return strings.GetString($"Shortcut_{shortcut.Action}");
    }
}
