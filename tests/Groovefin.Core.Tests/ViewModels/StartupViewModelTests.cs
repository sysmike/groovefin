using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

/// <summary>Settings → Start with Windows (#395).</summary>
public sealed class StartupViewModelTests
{
    private readonly IStartupRegistration _startup = Substitute.For<IStartupRegistration>();
    private readonly InMemorySettingsService _settings = new();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public StartupViewModelTests() => _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());

    private StartupViewModel CreateSut() => new(_startup, _settings, _strings);

    [Fact]
    public async Task The_switch_shows_windows_state_once_read()
    {
        _startup.GetStateAsync().Returns(StartupState.On);
        using var sut = CreateSut();
        sut.CanChange.Should().BeFalse("nothing is read yet");

        await sut.LoadAsync();

        sut.StartWithWindows.Should().BeTrue();
        sut.CanChange.Should().BeTrue();
        sut.HasStateText.Should().BeFalse();
    }

    [Fact]
    public async Task Turning_it_on_asks_windows_and_shows_what_it_says()
    {
        _startup.GetStateAsync().Returns(StartupState.Off);
        _startup.SetAsync(true).Returns(StartupState.On);
        using var sut = CreateSut();
        await sut.LoadAsync();

        sut.StartWithWindows = true;

        await _startup.Received(1).SetAsync(true);
        sut.StartWithWindows.Should().BeTrue();
        sut.CanChange.Should().BeTrue();
    }

    [Theory]
    [InlineData(StartupState.OffByUser, "Settings_StartupOffByUser")]
    [InlineData(StartupState.ByPolicy, "Settings_StartupByPolicy")]
    [InlineData(StartupState.Unavailable, "Settings_StartupUnavailable")]
    public async Task What_windows_keeps_from_changing_says_why(StartupState state, string text)
    {
        _startup.GetStateAsync().Returns(state);
        using var sut = CreateSut();
        await sut.LoadAsync();

        sut.StartWithWindows = true;

        sut.CanChange.Should().BeFalse();
        sut.StateText.Should().Be(text);
        await _startup.DidNotReceive().SetAsync(Arg.Any<bool>());
    }

    [Fact]
    public void Start_minimized_is_saved()
    {
        using var sut = CreateSut();

        sut.StartMinimized = true;

        _settings.Current.Appearance.StartMinimized.Should().BeTrue();
    }
}
