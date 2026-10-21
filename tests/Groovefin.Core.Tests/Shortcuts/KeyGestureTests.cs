using Groovefin.Core.Shortcuts;

namespace Groovefin.Core.Tests.Shortcuts;

public sealed class KeyGestureTests
{
    [Theory]
    [InlineData("Space")]
    [InlineData("Ctrl+Right")]
    [InlineData("Alt+Shift+F5")]
    [InlineData("Ctrl+Alt+Shift+Number1")]
    public void The_settings_form_round_trips(string text)
    {
        KeyGesture.TryParse(text, out var gesture).Should().BeTrue();

        gesture.ToString().Should().Be(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Win+K")]
    [InlineData("Strg+K")]
    public void Other_text_does_not_parse(string? text) =>
        KeyGesture.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void The_key_compares_without_case_the_modifiers_exactly()
    {
        new KeyGesture("k", Control: true).Should().Be(new KeyGesture("K", Control: true));
        new KeyGesture("k", Control: true).GetHashCode().Should().Be(new KeyGesture("K", Control: true).GetHashCode());
        new KeyGesture("K", Control: true).Should().NotBe(new KeyGesture("K", Control: true, Shift: true));
        new KeyGesture("K", Alt: true).Should().NotBe(new KeyGesture("K", Control: true));
    }

    [Fact]
    public void Shift_alone_still_counts_as_bare()
    {
        new KeyGesture("F5", Shift: true).IsBare.Should().BeTrue();
        new KeyGesture("F5", Alt: true).IsBare.Should().BeFalse();
    }
}
