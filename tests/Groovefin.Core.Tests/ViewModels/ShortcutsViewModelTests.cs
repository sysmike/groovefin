using Groovefin.Core.Services;
using Groovefin.Core.Shortcuts;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class ShortcutsViewModelTests : IDisposable
{
    private readonly InMemorySettingsService _settings = new();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly ShortcutService _shortcuts;
    private readonly ShortcutsViewModel _sut;

    public ShortcutsViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Key_Ctrl").Returns("Ctrl");
        _strings.GetString("Key_Alt").Returns("Alt");
        _strings.GetString("Key_Right").Returns("Right");
        _strings.GetString("Shortcut_Next").Returns("Next track");
        _strings.GetString("Shortcut_Lyrics").Returns("Lyrics");
        _strings.GetString("Shortcuts_InUse").Returns("Used for {0}");
        _strings.GetString("Shortcuts_ChangeName").Returns("Change the keys for {0}");
        _strings.GetString("Shortcuts_ResetName").Returns("Reset {0} to {1}");
        _shortcuts = new ShortcutService(_settings);
        _sut = new ShortcutsViewModel(_shortcuts, _strings);
    }

    private ShortcutItemViewModel Next => _sut.Items.Single(i => i.Action == ShortcutAction.Next);

    public void Dispose()
    {
        _sut.Dispose();
        _shortcuts.Dispose();
    }

    [Fact]
    public void Every_shortcut_is_listed_with_its_keys_and_names()
    {
        _sut.Items.Select(i => i.Action).Should().Equal(KeyboardShortcuts.Defaults.Select(s => s.Action));
        Next.Description.Should().Be("Next track");
        Next.Keys.Should().Be("Ctrl+Right");
        Next.AccessibleName.Should().Be("Next track, Ctrl+Right");
        Next.ChangeName.Should().Be("Change the keys for Next track");
        Next.ResetName.Should().Be("Reset Next track to Ctrl+Right");
        Next.IsChanged.Should().BeFalse();
        _sut.HasChanges.Should().BeFalse();
        _sut.ResetAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void The_preview_explains_why_keys_cannot_be_saved()
    {
        _sut.Preview(Next, new KeyGesture("L", Control: true)).Should().Be(new ShortcutPreview("Ctrl+L", "Used for Lyrics", false));
        _sut.Preview(Next, new KeyGesture("N")).Should().Be(new ShortcutPreview("N", "Shortcuts_NeedsModifier", false));
        _sut.Preview(Next, new KeyGesture("F4", Alt: true)).Should().Be(new ShortcutPreview("Alt+F4", "Shortcuts_Reserved", false));
        _sut.Preview(Next, new KeyGesture("Tab", Control: true)).Problem.Should().Be("Shortcuts_Unsupported");
    }

    [Fact]
    public void While_only_modifiers_are_down_the_preview_waits_for_the_key() =>
        _sut.Preview(Next, new KeyGesture("Menu", Control: true, Alt: true)).Should().Be(new ShortcutPreview("Ctrl+Alt+…", null, false));

    [Fact]
    public void Saving_new_keys_updates_the_row_and_enables_reset()
    {
        var keys = new KeyGesture("N", Control: true, Alt: true);
        _sut.Preview(Next, keys).Should().Be(new ShortcutPreview("Ctrl+Alt+N", null, true));

        _sut.Assign(Next, keys).Should().BeTrue();

        Next.Keys.Should().Be("Ctrl+Alt+N");
        Next.IsChanged.Should().BeTrue();
        Next.ResetCommand.CanExecute(null).Should().BeTrue();
        _sut.HasChanges.Should().BeTrue();
        _sut.ResetAllCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Reset_is_unavailable_while_another_shortcut_holds_the_default()
    {
        _sut.Assign(Next, new KeyGesture("N", Control: true, Alt: true));
        var previous = _sut.Items.Single(i => i.Action == ShortcutAction.Previous);
        _sut.Assign(previous, new KeyGesture("Right", Control: true));

        Next.CanReset.Should().BeFalse();

        previous.ResetCommand.Execute(null);
        Next.CanReset.Should().BeTrue();
        Next.ResetCommand.Execute(null);
        Next.Keys.Should().Be("Ctrl+Right");
    }

    [Fact]
    public void Reset_all_restores_the_list()
    {
        _sut.Assign(Next, new KeyGesture("N", Control: true, Alt: true));

        _sut.ResetAllCommand.Execute(null);

        Next.Keys.Should().Be("Ctrl+Right");
        _sut.HasChanges.Should().BeFalse();
    }
}
