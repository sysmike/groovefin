using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Services;
using Groovefin.Core.Shortcuts;

namespace Groovefin.Core.ViewModels;

/// <summary>What the change dialog shows for the keys pressed so far.</summary>
/// <param name="Keys">The keys as the user sees them, e.g. "Ctrl+Alt+K", or "Ctrl+…" while only modifiers are down.</param>
/// <param name="Problem">Why the keys cannot be saved, or <see langword="null"/>.</param>
/// <param name="CanSave">Whether Save may assign them.</param>
public sealed record ShortcutPreview(string Keys, string? Problem, bool CanSave);

/// <summary>One shortcut in Settings: what it does and its keys, which the user can change or reset.</summary>
public sealed partial class ShortcutItemViewModel : ObservableObject
{
    private readonly IStringResources _strings;

    internal ShortcutItemViewModel(ShortcutAction action, IStringResources strings, Action<ShortcutAction> reset)
    {
        _strings = strings;
        Action = action;
        Description = KeyboardShortcuts.Describe(action, strings);
        DefaultKeys = KeyboardShortcuts.Format(KeyboardShortcuts.DefaultOf(action), strings);
        ResetCommand = new RelayCommand(() => reset(action), () => CanReset);
    }

    public ShortcutAction Action { get; }

    /// <summary>Gets what the shortcut does, e.g. "Next track".</summary>
    public string Description { get; }

    public string DefaultKeys { get; }

    /// <summary>Gets the keys in effect, e.g. "Ctrl+Right".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial string Keys { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the keys differ from the default.</summary>
    [ObservableProperty]
    public partial bool IsChanged { get; private set; }

    /// <summary>Gets a value indicating whether the default keys can come back (no other shortcut has taken them).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial bool CanReset { get; private set; }

    /// <summary>Gets the name Narrator reads for the row, e.g. "Next track, Ctrl+Right".</summary>
    public string AccessibleName => Description + ", " + Keys;

    /// <summary>Gets the name of the Change button, e.g. "Change the keys for Next track".</summary>
    public string ChangeName => _strings.GetString("Shortcuts_ChangeName").Replace("{0}", Description, StringComparison.Ordinal);

    /// <summary>Gets the name of the Reset button, e.g. "Reset Next track to Ctrl+Right".</summary>
    public string ResetName => _strings.GetString("Shortcuts_ResetName")
        .Replace("{0}", Description, StringComparison.Ordinal)
        .Replace("{1}", DefaultKeys, StringComparison.Ordinal);

    public IRelayCommand ResetCommand { get; }

    internal void Refresh(ShortcutMap map)
    {
        Keys = KeyboardShortcuts.Format(map.GestureOf(Action), _strings);
        IsChanged = map.IsChanged(Action);
        CanReset = IsChanged && map.UserOf(KeyboardShortcuts.DefaultOf(Action), Action) is null;
    }
}

/// <summary>
/// Settings → Keyboard shortcuts (#68): every shortcut with its keys, the change dialog's checks, and reset.
/// The dialog itself, which records the keys, belongs to the app.
/// </summary>
public sealed partial class ShortcutsViewModel : ObservableObject, IDisposable
{
    private readonly ShortcutService _shortcuts;
    private readonly IStringResources _strings;

    public ShortcutsViewModel(ShortcutService shortcuts, IStringResources strings)
    {
        _shortcuts = shortcuts;
        _strings = strings;
        Items = [.. KeyboardShortcuts.Defaults.Select(s => new ShortcutItemViewModel(s.Action, strings, Reset))];
        Refresh();
        _shortcuts.Changed += OnShortcutsChanged;
    }

    public IReadOnlyList<ShortcutItemViewModel> Items { get; }

    /// <summary>Gets a value indicating whether any shortcut has other keys than its default.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetAllCommand))]
    public partial bool HasChanges { get; private set; }

    public void Dispose() => _shortcuts.Changed -= OnShortcutsChanged;

    /// <summary>Gets the dialog's instructions for <paramref name="item"/>.</summary>
    public string PromptFor(ShortcutItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _strings.GetString("Shortcuts_Prompt").Replace("{0}", item.Description, StringComparison.Ordinal);
    }

    /// <summary>What the dialog shows after the user pressed <paramref name="gesture"/> for <paramref name="item"/>.</summary>
    public ShortcutPreview Preview(ShortcutItemViewModel item, KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(item);
        var check = _shortcuts.Check(item.Action, gesture);
        if (check.Result == GestureCheck.Incomplete)
        {
            return new ShortcutPreview(KeyboardShortcuts.Format(gesture with { Key = "…" }, _strings), null, false);
        }

        var keys = KeyboardShortcuts.Format(gesture, _strings);
        var problem = check.Result switch
        {
            GestureCheck.Unsupported => _strings.GetString("Shortcuts_Unsupported"),
            GestureCheck.NeedsModifier => _strings.GetString("Shortcuts_NeedsModifier"),
            GestureCheck.Reserved => _strings.GetString("Shortcuts_Reserved"),
            _ when check.InUseBy is { } other => _strings.GetString("Shortcuts_InUse")
                .Replace("{0}", KeyboardShortcuts.Describe(other, _strings), StringComparison.Ordinal),
            _ => null,
        };
        return new ShortcutPreview(keys, problem, check.CanAssign);
    }

    /// <summary>Saves <paramref name="gesture"/> as the keys of <paramref name="item"/> when <see cref="Preview"/> allows it.</summary>
    public bool Assign(ShortcutItemViewModel item, KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _shortcuts.Assign(item.Action, gesture);
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void ResetAll() => _shortcuts.ResetAll();

    private void Reset(ShortcutAction action) => _shortcuts.Reset(action);

    private void OnShortcutsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        foreach (var item in Items)
        {
            item.Refresh(_shortcuts.Map);
        }

        HasChanges = Items.Any(i => i.IsChanged);
    }
}
