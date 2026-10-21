namespace Groovefin.Core.Shortcuts;

/// <summary>
/// The shortcuts in effect: the defaults with the user's changes (<see cref="Settings.KeyboardSettings"/>).
/// Each action has one gesture and no two actions share one. Immutable: a change makes a new map.
/// </summary>
public sealed class ShortcutMap
{
    private readonly Dictionary<ShortcutAction, KeyGesture> _gestures;

    private ShortcutMap(Dictionary<ShortcutAction, KeyGesture> gestures) => _gestures = gestures;

    public static ShortcutMap Default { get; } = From(null);

    /// <summary>Gets the shortcuts in the order of <see cref="KeyboardShortcuts.Defaults"/>.</summary>
    public IReadOnlyList<KeyboardShortcut> Shortcuts =>
        [.. KeyboardShortcuts.Defaults.Select(d => new KeyboardShortcut(d.Action, _gestures[d.Action]))];

    /// <summary>
    /// Applies <paramref name="changes"/> (action name to <see cref="KeyGesture.ToString"/> form) to the
    /// defaults. A change applies once its keys are free, so one that takes the default keys of an action
    /// that was changed as well still applies. A change that does not parse, is not allowed, or keeps
    /// clashing (a hand-edited file) is left out: that action keeps its default.
    /// </summary>
    public static ShortcutMap From(IReadOnlyDictionary<string, string>? changes)
    {
        var map = new ShortcutMap(KeyboardShortcuts.Defaults.ToDictionary(s => s.Action, s => s.Gesture));
        if (changes is null)
        {
            return map;
        }

        var pending = KeyboardShortcuts.Defaults
            .Select(s => (s.Action, Gesture: ValidChange(changes, s.Action)))
            .Where(c => c.Gesture is not null)
            .Select(c => (c.Action, Gesture: c.Gesture!.Value))
            .ToList();

        // Each pass applies the changes whose keys are free; one that frees a default can unblock another.
        int applied;
        do
        {
            applied = pending.RemoveAll(change =>
            {
                if (map.UserOf(change.Gesture, change.Action) is not null)
                {
                    return false;
                }

                map._gestures[change.Action] = change.Gesture;
                return true;
            });
        }
        while (applied > 0 && pending.Count > 0);

        return map;
    }

    public KeyGesture GestureOf(ShortcutAction action) => _gestures[action];

    /// <summary>The keys <paramref name="changes"/> gives <paramref name="action"/>, if they parse and are allowed.</summary>
    private static KeyGesture? ValidChange(IReadOnlyDictionary<string, string> changes, ShortcutAction action) =>
        changes.TryGetValue(action.ToString(), out var text)
        && KeyGesture.TryParse(text, out var gesture)
        && KeyboardShortcuts.Check(gesture) == GestureCheck.Valid
            ? gesture
            : null;

    /// <summary>Gets a value indicating whether <paramref name="action"/> has other keys than its default.</summary>
    public bool IsChanged(ShortcutAction action) => _gestures[action] != KeyboardShortcuts.DefaultOf(action);

    /// <summary>Gets the action <paramref name="gesture"/> runs, or <see langword="null"/>.</summary>
    public ShortcutAction? Find(KeyGesture gesture)
    {
        foreach (var (action, keys) in _gestures)
        {
            if (keys == gesture)
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>Gets the action other than <paramref name="except"/> that already uses <paramref name="gesture"/>.</summary>
    public ShortcutAction? UserOf(KeyGesture gesture, ShortcutAction except) =>
        Find(gesture) is { } user && user != except ? user : null;
}
