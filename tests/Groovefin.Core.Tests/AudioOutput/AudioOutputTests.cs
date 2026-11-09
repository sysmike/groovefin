using Groovefin.Core.AudioOutput;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.AudioOutput;

public sealed class AudioOutputTests
{
    private static readonly AudioOutputDevice Speakers = new("dev-speakers", "Speakers");
    private static readonly AudioOutputDevice Headphones = new("dev-headphones", "Headphones");

    private readonly IAudioOutput _output = Substitute.For<IAudioOutput>();
    private readonly InMemorySettingsService _settings = new();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public AudioOutputTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Settings_OutputMissing").Returns("{0} (not connected)");
        _output.GetDevicesAsync().Returns([Speakers, Headphones]);
    }

    private void Choose(string? id, string? name) =>
        _settings.Update(s => s with { Playback = s.Playback with { OutputDeviceId = id, OutputDeviceName = name } });

    [Fact]
    public void The_sync_passes_the_saved_device_at_start_and_each_change()
    {
        Choose("dev-headphones", "Headphones");

        using var sync = new AudioOutputSync(_settings, _output);
        Choose(null, null);
        Choose(null, null);

        Received.InOrder(() =>
        {
            _output.Use("dev-headphones");
            _output.Use(null);
        });
        _output.ReceivedWithAnyArgs(2).Use(default);
    }

    [Fact]
    public void The_default_needs_no_call_at_start()
    {
        using var sync = new AudioOutputSync(_settings, _output);

        _output.DidNotReceiveWithAnyArgs().Use(default);
    }

    [Fact]
    public async Task The_list_has_the_default_first_and_selects_the_saved_device()
    {
        Choose("dev-speakers", "Speakers");
        using var sut = new OutputDeviceViewModel(_output, _settings, _strings);

        await sut.LoadAsync();

        sut.Options.Should().Equal("Settings_OutputDefault", "Speakers", "Headphones");
        sut.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public async Task Choosing_saves_the_device_and_its_name()
    {
        using var sut = new OutputDeviceViewModel(_output, _settings, _strings);
        await sut.LoadAsync();

        sut.SelectedIndex = 2;
        _settings.Current.Playback.OutputDeviceId.Should().Be("dev-headphones");
        _settings.Current.Playback.OutputDeviceName.Should().Be("Headphones");

        sut.SelectedIndex = 0;
        _settings.Current.Playback.OutputDeviceId.Should().BeNull();
    }

    [Fact]
    public async Task A_saved_device_that_is_not_connected_stays_listed_and_selected()
    {
        Choose("dev-usb", "USB DAC");
        using var sut = new OutputDeviceViewModel(_output, _settings, _strings);

        await sut.LoadAsync();

        sut.Options.Should().Equal("Settings_OutputDefault", "Speakers", "Headphones", "USB DAC (not connected)");
        sut.SelectedIndex.Should().Be(3);
        _settings.Current.Playback.OutputDeviceId.Should().Be("dev-usb", "loading does not change the choice");
    }

    [Fact]
    public async Task Without_the_device_list_only_the_default_is_offered()
    {
        _output.GetDevicesAsync().Returns(Task.FromException<IReadOnlyList<AudioOutputDevice>>(new InvalidOperationException("no audio")));
        using var sut = new OutputDeviceViewModel(_output, _settings, _strings);

        await sut.LoadAsync();

        sut.Options.Should().Equal("Settings_OutputDefault");
        sut.SelectedIndex.Should().Be(0);
    }
}
