using Groovefin.Core.Shortcuts;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Shortcuts;

public sealed class ShortcutServiceTests : IDisposable
{
    private static readonly KeyGesture CtrlAltN = new("N", Control: true, Alt: true);

    private readonly InMemorySettingsService _settings = new();
    private readonly ShortcutService _sut;

    public ShortcutServiceTests() => _sut = new ShortcutService(_settings);

    public void Dispose() => _sut.Dispose();

    [Fact]
    public void Assigning_keys_saves_them_and_updates_the_map()
    {
        var changed = 0;
        _sut.Changed += (_, _) => changed++;

        _sut.Assign(ShortcutAction.Next, CtrlAltN).Should().BeTrue();

        _settings.Current.Keyboard.Shortcuts.Should().Equal(new Dictionary<string, string> { ["Next"] = "Ctrl+Alt+N" });
        _sut.Map.Find(CtrlAltN).Should().Be(ShortcutAction.Next);
        changed.Should().Be(1);
    }

    [Fact]
    public void Keys_another_shortcut_uses_are_refused_and_named()
    {
        var check = _sut.Check(ShortcutAction.Next, new KeyGesture("L", Control: true));

        check.InUseBy.Should().Be(ShortcutAction.Lyrics);
        check.CanAssign.Should().BeFalse();
        _sut.Assign(ShortcutAction.Next, new KeyGesture("L", Control: true)).Should().BeFalse();
        _settings.Current.Keyboard.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public void Keys_against_the_rules_are_refused()
    {
        _sut.Check(ShortcutAction.Next, new KeyGesture("N")).Result.Should().Be(GestureCheck.NeedsModifier);
        _sut.Check(ShortcutAction.Next, new KeyGesture("F4", Alt: true)).Result.Should().Be(GestureCheck.Reserved);
        _sut.Assign(ShortcutAction.Next, new KeyGesture("N")).Should().BeFalse();
    }

    [Fact]
    public void A_shortcuts_own_keys_are_no_conflict()
    {
        _sut.Check(ShortcutAction.Next, new KeyGesture("Right", Control: true)).CanAssign.Should().BeTrue();
    }

    [Fact]
    public void Assigning_the_default_keys_again_removes_the_change()
    {
        _sut.Assign(ShortcutAction.Next, CtrlAltN);

        _sut.Assign(ShortcutAction.Next, new KeyGesture("Right", Control: true)).Should().BeTrue();

        _settings.Current.Keyboard.Shortcuts.Should().BeEmpty();
        _sut.Map.IsChanged(ShortcutAction.Next).Should().BeFalse();
    }

    [Fact]
    public void Reset_brings_the_default_back_unless_another_shortcut_took_it()
    {
        _sut.Assign(ShortcutAction.Next, CtrlAltN);
        _sut.Assign(ShortcutAction.Previous, new KeyGesture("Right", Control: true));

        _sut.Reset(ShortcutAction.Next).Should().BeFalse("Previous has Ctrl+Right now");

        _sut.Reset(ShortcutAction.Previous).Should().BeTrue();
        _sut.Reset(ShortcutAction.Next).Should().BeTrue();
        _settings.Current.Keyboard.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public void Reset_all_restores_every_default()
    {
        _sut.Assign(ShortcutAction.Next, CtrlAltN);
        _sut.Assign(ShortcutAction.Search, new KeyGesture("F3"));

        _sut.ResetAll();

        _settings.Current.Keyboard.Shortcuts.Should().BeEmpty();
        _sut.Map.Shortcuts.Should().Equal(KeyboardShortcuts.Defaults);
    }

    [Fact]
    public void Other_settings_changes_leave_the_map_alone()
    {
        var map = _sut.Map;
        var changed = 0;
        _sut.Changed += (_, _) => changed++;

        _settings.Update(s => s with { Lyrics = s.Lyrics with { FontSize = 30 } });

        _sut.Map.Should().BeSameAs(map);
        changed.Should().Be(0);
    }
}
