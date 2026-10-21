using Groovefin.Core.Shortcuts;

namespace Groovefin.Core.Tests.Shortcuts;

public sealed class ShortcutMapTests
{
    [Fact]
    public void Without_changes_every_action_has_its_default()
    {
        var map = ShortcutMap.From(null);

        map.Shortcuts.Should().Equal(KeyboardShortcuts.Defaults);
        map.Find(new KeyGesture("Right", Control: true)).Should().Be(ShortcutAction.Next);
        map.Find(new KeyGesture("Right", Control: true, Shift: true)).Should().BeNull();
        map.IsChanged(ShortcutAction.Next).Should().BeFalse();
    }

    [Fact]
    public void A_change_replaces_the_default_keys()
    {
        var map = ShortcutMap.From(new Dictionary<string, string> { ["Next"] = "Ctrl+Alt+N" });

        map.GestureOf(ShortcutAction.Next).Should().Be(new KeyGesture("N", Control: true, Alt: true));
        map.Find(new KeyGesture("N", Control: true, Alt: true)).Should().Be(ShortcutAction.Next);
        map.Find(new KeyGesture("Right", Control: true)).Should().BeNull("the old keys are free again");
        map.IsChanged(ShortcutAction.Next).Should().BeTrue();
    }

    [Fact]
    public void A_change_may_take_the_default_keys_of_an_action_that_moved_away()
    {
        // The user gave Lyrics Ctrl+Y first, then Next the freed Ctrl+L; Next comes first in action order.
        var map = ShortcutMap.From(new Dictionary<string, string> { ["Next"] = "Ctrl+L", ["Lyrics"] = "Ctrl+Y" });

        map.GestureOf(ShortcutAction.Next).Should().Be(new KeyGesture("L", Control: true));
        map.GestureOf(ShortcutAction.Lyrics).Should().Be(new KeyGesture("Y", Control: true));
    }

    [Fact]
    public void Broken_and_clashing_entries_fall_back_to_the_defaults()
    {
        var map = ShortcutMap.From(new Dictionary<string, string>
        {
            ["Next"] = "Ctrl+L",          // Lyrics keeps Ctrl+L
            ["Previous"] = "K",           // needs Ctrl or Alt
            ["Search"] = "Hyper+K",       // does not parse
            ["Repeat"] = "Alt+F4",        // reserved
            ["Dance"] = "Ctrl+D",         // no such action
        });

        map.Shortcuts.Should().Equal(KeyboardShortcuts.Defaults);
        map.Shortcuts.Select(s => s.Gesture).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Two_entries_with_the_same_keys_give_them_to_the_first_action_only()
    {
        var map = ShortcutMap.From(new Dictionary<string, string> { ["Next"] = "Ctrl+Alt+N", ["Previous"] = "Ctrl+Alt+N" });

        map.GestureOf(ShortcutAction.Next).Should().Be(new KeyGesture("N", Control: true, Alt: true));
        map.GestureOf(ShortcutAction.Previous).Should().Be(KeyboardShortcuts.DefaultOf(ShortcutAction.Previous));
    }

    [Fact]
    public void The_user_of_keys_leaves_out_the_action_asking()
    {
        var map = ShortcutMap.Default;
        var keys = new KeyGesture("Right", Control: true);

        map.UserOf(keys, ShortcutAction.Next).Should().BeNull();
        map.UserOf(keys, ShortcutAction.Previous).Should().Be(ShortcutAction.Next);
    }
}
