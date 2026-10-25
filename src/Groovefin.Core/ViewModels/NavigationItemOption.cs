using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// A row of the navigation pane's layout in Settings (#255): a main section, its switch and its move buttons.
/// Changes go to <see cref="SettingsViewModel"/>, which saves them.
/// </summary>
public sealed partial class NavigationItemOption : ObservableObject
{
    private readonly Action<NavigationItemOption, int> _move;
    private readonly Action _changed;

    internal NavigationItemOption(string key, string name, IStringResources strings, bool isVisible, Action<NavigationItemOption, int> move, Action changed)
    {
        Key = key;
        Name = name;
        ShowName = strings.GetString("Navigation_ShowName").Replace("{0}", name, StringComparison.Ordinal);
        MoveUpName = strings.GetString("Navigation_MoveUpName").Replace("{0}", name, StringComparison.Ordinal);
        MoveDownName = strings.GetString("Navigation_MoveDownName").Replace("{0}", name, StringComparison.Ordinal);
        _move = move;
        _changed = changed;
        IsVisible = isVisible;
    }

    /// <summary>Gets a key of <see cref="Navigation.PageKeys.MainSections"/>.</summary>
    public string Key { get; }

    public string Name { get; }

    /// <summary>Gets the switch's name for Narrator, e.g. "Show Genres".</summary>
    public string ShowName { get; }

    /// <summary>Gets the move buttons' names for Narrator, e.g. "Move Genres up".</summary>
    public string MoveUpName { get; }

    public string MoveDownName { get; }

    /// <summary>Gets or sets whether the section shows in the navigation pane; saved on change.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Gets a value indicating whether the switch can change: the last section shown stays.</summary>
    [ObservableProperty]
    public partial bool CanToggle { get; internal set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    public partial bool CanMoveUp { get; internal set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    public partial bool CanMoveDown { get; internal set; }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _move(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _move(this, 1);

    /// <summary>The name of the row in a list (Narrator reads it for the list item).</summary>
    public override string ToString() => Name;

    partial void OnIsVisibleChanged(bool value) => _changed();
}
