using Groovefin.Core.Settings;

namespace Groovefin.Core.Shortcuts;

/// <summary>Whether keys can be given to a shortcut, and if not, why (<see cref="ShortcutService.Check"/>).</summary>
/// <param name="Result">The key rules' answer; <see cref="GestureCheck.Valid"/> unless the keys break one.</param>
/// <param name="InUseBy">The other action that already has these keys, if any.</param>
public sealed record ShortcutCheck(GestureCheck Result, ShortcutAction? InUseBy = null)
{
    /// <summary>Gets a value indicating whether the keys can be saved.</summary>
    public bool CanAssign => Result == GestureCheck.Valid && InUseBy is null;
}

/// <summary>
/// The shortcuts in effect (<see cref="Map"/>) and changes to them, which are saved with the settings
/// (<see cref="KeyboardSettings"/>). Lives as long as the app; the window asks it what a key does.
/// </summary>
public sealed class ShortcutService : IDisposable
{
    private readonly ISettingsService _settings;

    public ShortcutService(ISettingsService settings)
    {
        _settings = settings;
        Map = ShortcutMap.From(settings.Current.Keyboard.Shortcuts);
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised on the UI thread after <see cref="Map"/> has changed.</summary>
    public event EventHandler? Changed;

    public ShortcutMap Map { get; private set; }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    /// <summary>Whether <paramref name="gesture"/> can become the keys of <paramref name="action"/>.</summary>
    public ShortcutCheck Check(ShortcutAction action, KeyGesture gesture) =>
        KeyboardShortcuts.Check(gesture) is var result and not GestureCheck.Valid
            ? new ShortcutCheck(result)
            : new ShortcutCheck(GestureCheck.Valid, Map.UserOf(gesture, action));

    /// <summary>Gives <paramref name="action"/> the keys <paramref name="gesture"/>; does nothing unless <see cref="Check"/> allows it.</summary>
    /// <returns>Whether the keys were assigned.</returns>
    public bool Assign(ShortcutAction action, KeyGesture gesture)
    {
        if (!Check(action, gesture).CanAssign)
        {
            return false;
        }

        Save(action, gesture == KeyboardShortcuts.DefaultOf(action) ? null : gesture.ToString());
        return true;
    }

    /// <summary>Gives <paramref name="action"/> its default keys back, unless another shortcut has taken them meanwhile.</summary>
    /// <returns>Whether the default keys were restored.</returns>
    public bool Reset(ShortcutAction action) => Assign(action, KeyboardShortcuts.DefaultOf(action));

    /// <summary>Restores every default.</summary>
    public void ResetAll() =>
        _settings.Update(s => s with { Keyboard = s.Keyboard with { Shortcuts = new Dictionary<string, string>() } });

    private void Save(ShortcutAction action, string? gesture) =>
        _settings.Update(s =>
        {
            var shortcuts = new Dictionary<string, string>(s.Keyboard.Shortcuts);
            if (gesture is null)
            {
                shortcuts.Remove(action.ToString());
            }
            else
            {
                shortcuts[action.ToString()] = gesture;
            }

            return s with { Keyboard = s.Keyboard with { Shortcuts = shortcuts } };
        });

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.NewSettings.Keyboard != e.OldSettings.Keyboard)
        {
            Map = ShortcutMap.From(e.NewSettings.Keyboard.Shortcuts);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
