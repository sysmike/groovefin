using Groovefin.Core.Services;
using Groovefin.Core.Shortcuts;

namespace Groovefin.Core.Tests.Shortcuts;

public sealed class KeyboardShortcutsTests
{
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public KeyboardShortcutsTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Key_Ctrl").Returns("Strg");
        _strings.GetString("Key_Alt").Returns("Alt");
        _strings.GetString("Key_Shift").Returns("Umschalt");
        _strings.GetString("Key_Left").Returns("Nach-links");
        _strings.GetString("Key_Right").Returns("Nach-rechts");
        _strings.GetString("Key_Space").Returns("Leertaste");
        _strings.GetString("Key_NumberPad").Returns("Num {0}");
    }

    [Theory]
    [InlineData(ShortcutAction.PlayPause, "Space")]
    [InlineData(ShortcutAction.Next, "Ctrl+Right")]
    [InlineData(ShortcutAction.Previous, "Ctrl+Left")]
    [InlineData(ShortcutAction.VolumeUp, "Ctrl+Up")]
    [InlineData(ShortcutAction.VolumeDown, "Ctrl+Down")]
    [InlineData(ShortcutAction.Search, "Ctrl+K")]
    [InlineData(ShortcutAction.Queue, "Ctrl+Q")]
    [InlineData(ShortcutAction.Lyrics, "Ctrl+L")]
    [InlineData(ShortcutAction.Shuffle, "Ctrl+S")]
    [InlineData(ShortcutAction.Repeat, "Ctrl+R")]
    [InlineData(ShortcutAction.FullScreen, "F11")]
    [InlineData(ShortcutAction.MiniPlayer, "Ctrl+M")]
    [InlineData(ShortcutAction.GoBack, "Alt+Left")]
    [InlineData(ShortcutAction.GoForward, "Alt+Right")]
    [InlineData(ShortcutAction.PrivacyMode, "Ctrl+Shift+P")]
    public void Every_documented_default_is_in_place(ShortcutAction action, string keys) =>
        KeyboardShortcuts.DefaultOf(action).ToString().Should().Be(keys);

    [Fact]
    public void Each_action_has_one_default_and_the_defaults_are_distinct_and_valid()
    {
        KeyboardShortcuts.Defaults.Select(s => s.Action).Should().BeEquivalentTo(Enum.GetValues<ShortcutAction>());
        KeyboardShortcuts.Defaults.Select(s => s.Gesture).Should().OnlyHaveUniqueItems();
        KeyboardShortcuts.Defaults.Should().OnlyContain(s => KeyboardShortcuts.Check(s.Gesture) == GestureCheck.Valid);
    }

    [Theory]
    [InlineData("Ctrl+K", GestureCheck.Valid)]
    [InlineData("Alt+Shift+Number5", GestureCheck.Valid)]
    [InlineData("Ctrl+NumberPad0", GestureCheck.Valid)]
    [InlineData("Ctrl+PageDown", GestureCheck.Valid)]
    [InlineData("F5", GestureCheck.Valid)]
    [InlineData("Shift+F24", GestureCheck.Valid)]
    [InlineData("Ctrl+Space", GestureCheck.Valid)]
    [InlineData("Ctrl+Alt+F4", GestureCheck.Valid)]
    [InlineData("K", GestureCheck.NeedsModifier)]
    [InlineData("Shift+K", GestureCheck.NeedsModifier)]
    [InlineData("Number1", GestureCheck.NeedsModifier)]
    [InlineData("Right", GestureCheck.NeedsModifier)]
    [InlineData("Home", GestureCheck.NeedsModifier)]
    [InlineData("Alt+F4", GestureCheck.Reserved)]
    [InlineData("Alt+Space", GestureCheck.Reserved)]
    [InlineData("Ctrl+Control", GestureCheck.Incomplete)]
    [InlineData("Shift", GestureCheck.Incomplete)]
    [InlineData("LeftWindows", GestureCheck.Incomplete)]
    [InlineData("Ctrl+Tab", GestureCheck.Unsupported)]
    [InlineData("Enter", GestureCheck.Unsupported)]
    [InlineData("Ctrl+F25", GestureCheck.Unsupported)]
    [InlineData("Ctrl+CapitalLock", GestureCheck.Unsupported)]
    public void Keys_are_checked_against_the_rules(string keys, GestureCheck expected)
    {
        KeyGesture.TryParse(keys, out var gesture).Should().BeTrue();

        KeyboardShortcuts.Check(gesture).Should().Be(expected);
    }

    [Theory]
    [InlineData("Ctrl+Right", "Strg+Nach-rechts")]
    [InlineData("Space", "Leertaste")]
    [InlineData("Ctrl+k", "Strg+K")]
    [InlineData("F11", "F11")]
    [InlineData("Alt+Left", "Alt+Nach-links")]
    [InlineData("Ctrl+Alt+Shift+Number7", "Strg+Alt+Umschalt+7")]
    [InlineData("Ctrl+NumberPad3", "Strg+Num 3")]
    [InlineData("Ctrl+PageUp", "Strg+Key_PageUp")]
    public void Keys_are_shown_with_the_localized_names(string keys, string shown)
    {
        KeyGesture.TryParse(keys, out var gesture).Should().BeTrue();

        KeyboardShortcuts.Format(gesture, _strings).Should().Be(shown);
    }

    [Fact]
    public void Description_is_the_actions_string() =>
        KeyboardShortcuts.Describe(ShortcutAction.PlayPause, _strings).Should().Be("Shortcut_PlayPause");
}
