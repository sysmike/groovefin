using System.ComponentModel;
using Groovefin.Audio;
using Groovefin.Core.Equalizer;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class EqualizerViewModelTests : IDisposable
{
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeEqualizer _equalizer = new();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly CultureScope _culture = new("en-US");
    private readonly EqualizerViewModel _sut;

    public EqualizerViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(c => c.Arg<string>() switch
        {
            "Equalizer_Decibels" => "{0} dB",
            "Equalizer_Hertz" => "{0} Hz",
            "Equalizer_Kilohertz" => "{0} kHz",
            var key => key,
        });
        _sut = new EqualizerViewModel(_settings, _equalizer, _strings);
    }

    private EqualizerSettings Saved => _settings.Current.Playback.Equalizer;

    public void Dispose()
    {
        _sut.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public void Shows_ten_bands_named_by_frequency_and_every_preset()
    {
        _sut.Bands.Select(b => b.Name).Should().Equal("31 Hz", "62 Hz", "125 Hz", "250 Hz", "500 Hz", "1 kHz", "2 kHz", "4 kHz", "8 kHz", "16 kHz");
        _sut.Bands.Should().OnlyContain(b => b.Gain == 0 && b.GainText == "0 dB");
        _sut.PresetOptions.Should().HaveCount(EqualizerPresets.All.Count).And.StartWith("EqualizerPreset_Flat").And.EndWith("EqualizerPreset_Custom");
        _sut.PresetIndex.Should().Be(0);
        _sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Turning_it_on_is_saved()
    {
        _sut.IsEnabled = true;

        Saved.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Choosing_a_preset_moves_the_sliders()
    {
        var changed = new List<string?>();
        _sut.Bands[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _sut.PresetIndex = EqualizerPresets.All.ToList().IndexOf(EqualizerPreset.BassBoost);

        Saved.Preset.Should().Be(EqualizerPreset.BassBoost);
        _sut.Bands.Select(b => b.Gain).Should().Equal(EqualizerPresets.GainsOf(EqualizerPreset.BassBoost));
        _sut.Bands[0].GainText.Should().Be("+6 dB");
        changed.Should().Contain([nameof(EqualizerBandViewModel.Gain), nameof(EqualizerBandViewModel.GainText)]);
    }

    [Fact]
    public void Moving_a_slider_makes_the_shown_gains_the_custom_preset()
    {
        _sut.PresetIndex = EqualizerPresets.All.ToList().IndexOf(EqualizerPreset.Rock);

        _sut.Bands[4].Gain = -2.5;

        Saved.Preset.Should().Be(EqualizerPreset.Custom);
        Saved.CustomGains.Should().Equal(4, 3, 2, 0, -2.5, -1, 1, 2, 3, 4);
        _sut.PresetIndex.Should().Be(EqualizerPresets.All.Count - 1);
        _sut.Bands[4].GainText.Should().Be("-2.5 dB");
    }

    [Fact]
    public void The_custom_gains_come_back_after_trying_a_preset()
    {
        _sut.Bands[0].Gain = 3;
        _sut.PresetIndex = 1;

        _sut.PresetIndex = EqualizerPresets.All.Count - 1;

        _sut.Bands[0].Gain.Should().Be(3);
    }

    [Fact]
    public void The_preamp_is_limited_and_saved()
    {
        _sut.Preamp = -20;

        Saved.Preamp.Should().Be(EqualizerBands.MinGain);
        _sut.PreampText.Should().Be("-12 dB");
    }

    [Fact]
    public void Reset_makes_everything_flat_and_keeps_it_on()
    {
        _sut.IsEnabled = true;
        _sut.Bands[2].Gain = 5;
        _sut.Preamp = -4;

        _sut.ResetCommand.Execute(null);

        Saved.Should().Be(new EqualizerSettings { Enabled = true });
        _sut.Bands.Should().OnlyContain(b => b.Gain == 0);
        _sut.Preamp.Should().Be(0);
    }

    [Fact]
    public void Changes_from_elsewhere_reach_the_page()
    {
        var changed = new List<string?>();
        _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _settings.Update(s => s with { Playback = s.Playback with { Equalizer = s.Playback.Equalizer with { Enabled = true, Preamp = 2 } } });

        changed.Should().Contain([nameof(EqualizerViewModel.IsEnabled), nameof(EqualizerViewModel.Preamp), nameof(EqualizerViewModel.PreampText)]);
        _sut.PreampText.Should().Be("+2 dB");
    }

    [Fact]
    public void Without_the_effect_it_is_not_available()
    {
        using var sut = new EqualizerViewModel(_settings, new FakeEqualizer { IsAvailable = false }, _strings);

        sut.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Disposed_it_stops_following_the_settings()
    {
        var changed = new List<PropertyChangedEventArgs>();
        _sut.PropertyChanged += (_, e) => changed.Add(e);

        _sut.Dispose();
        _settings.Update(s => s with { Playback = s.Playback with { Equalizer = s.Playback.Equalizer with { Enabled = true } } });

        changed.Should().BeEmpty();
    }
}
