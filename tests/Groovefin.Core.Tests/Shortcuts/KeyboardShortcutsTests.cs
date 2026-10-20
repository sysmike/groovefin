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
        _strings.GetString("Key_Right").Returns("Nach-rechts");
        _strings.GetString("Key_Space").Returns("Leertaste");
    }

    [Theory]
    [InlineData("Space", false, ShortcutAction.PlayPause)]
    [InlineData("Right", true, ShortcutAction.Next)]
    [InlineData("Left", true, ShortcutAction.Previous)]
    [InlineData("Up", true, ShortcutAction.VolumeUp)]
    [InlineData("Down", true, ShortcutAction.VolumeDown)]
    [InlineData("K", true, ShortcutAction.Search)]
    [InlineData("q", true, ShortcutAction.Queue)]
    [InlineData("L", true, ShortcutAction.Lyrics)]
    [InlineData("S", true, ShortcutAction.Shuffle)]
    [InlineData("R", true, ShortcutAction.Repeat)]
    public void Every_documented_shortcut_is_found(string key, bool control, ShortcutAction expected) =>
        KeyboardShortcuts.Find(key, control).Should().Be(expected);

    [Theory]
    [InlineData("Right", false)]
    [InlineData("Space", true)]
    [InlineData("X", true)]
    [InlineData("S", false)]
    public void Other_keys_are_no_shortcut(string key, bool control) =>
        KeyboardShortcuts.Find(key, control).Should().BeNull();

    [Fact]
    public void Each_key_has_one_action_and_each_action_one_key()
    {
        KeyboardShortcuts.All.Select(s => (s.Control, s.Key.ToUpperInvariant())).Should().OnlyHaveUniqueItems();
        KeyboardShortcuts.All.Select(s => s.Action).Should().BeEquivalentTo(Enum.GetValues<ShortcutAction>());
    }

    [Fact]
    public void Gestures_use_the_localized_key_names()
    {
        KeyboardShortcuts.Gesture(KeyboardShortcuts.All.Single(s => s.Action == ShortcutAction.Next), _strings).Should().Be("Strg+Nach-rechts");
        KeyboardShortcuts.Gesture(KeyboardShortcuts.All.Single(s => s.Action == ShortcutAction.PlayPause), _strings).Should().Be("Leertaste");
        KeyboardShortcuts.Gesture(KeyboardShortcuts.All.Single(s => s.Action == ShortcutAction.Search), _strings).Should().Be("Strg+K");
        KeyboardShortcuts.Describe(KeyboardShortcuts.All[0], _strings).Should().Be("Shortcut_PlayPause");
    }
}
