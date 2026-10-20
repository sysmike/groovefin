using Groovefin.Audio;
using Groovefin.Core.Equalizer;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Equalizer;

public sealed class EqualizerSettingsTests
{
    [Fact]
    public void Every_preset_but_custom_has_a_gain_per_band_within_the_range()
    {
        foreach (var preset in EqualizerPresets.All.Where(p => p != EqualizerPreset.Custom))
        {
            EqualizerPresets.GainsOf(preset).Should().HaveCount(EqualizerBands.Count)
                .And.OnlyContain(g => g >= EqualizerBands.MinGain && g <= EqualizerBands.MaxGain);
        }

        EqualizerPresets.GainsOf(EqualizerPreset.Flat).Should().OnlyContain(g => g == 0);
        EqualizerPresets.GainsOf(EqualizerPreset.Custom).Should().BeNull();
        EqualizerPresets.All[^1].Should().Be(EqualizerPreset.Custom);
    }

    [Fact]
    public void A_preset_applies_its_gains_and_keeps_the_custom_ones()
    {
        var settings = new EqualizerSettings { Enabled = true, Preset = EqualizerPreset.BassBoost, CustomGains = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1] };

        settings.Gains.Should().Equal(EqualizerPresets.GainsOf(EqualizerPreset.BassBoost));
        (settings with { Preset = EqualizerPreset.Custom }).Gains.Should().Equal(1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
    }

    [Fact]
    public void Custom_gains_from_an_edited_file_are_completed_and_limited()
    {
        var settings = new EqualizerSettings { Preset = EqualizerPreset.Custom, CustomGains = [40, -3] };

        settings.Gains.Should().Equal(12, -3, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void Settings_with_the_same_values_are_equal()
    {
        var a = new EqualizerSettings { Preset = EqualizerPreset.Custom, CustomGains = [1, 2] };

        a.Should().Be(new EqualizerSettings { Preset = EqualizerPreset.Custom, CustomGains = [1, 2] });
        a.Should().NotBe(a with { CustomGains = [1, 3] });
    }

    [Fact]
    public void The_sync_applies_the_settings_at_start_and_after_each_change()
    {
        var settings = new InMemorySettingsService();
        var equalizer = new FakeEqualizer();
        using var sut = new EqualizerSync(settings, equalizer);

        settings.Update(s => s with { Playback = s.Playback with { Equalizer = s.Playback.Equalizer with { Enabled = true, Preset = EqualizerPreset.Rock } } });
        settings.Update(s => s with { Playback = s.Playback with { ResumeSession = false } });

        equalizer.Applied.Should().HaveCount(2);
        equalizer.Applied[0].Should().Be(EqualizerParameters.Off);
        equalizer.Applied[1].Should().Be(new EqualizerParameters(true, 0, EqualizerPresets.GainsOf(EqualizerPreset.Rock)!));
    }

    [Fact]
    public void A_preset_change_while_off_is_not_passed_on()
    {
        var settings = new InMemorySettingsService();
        var equalizer = new FakeEqualizer();
        using var sut = new EqualizerSync(settings, equalizer);

        settings.Update(s => s with { Playback = s.Playback with { Equalizer = s.Playback.Equalizer with { Preset = EqualizerPreset.Rock } } });

        // Off is off: the audio does not change, so the effect is not bothered.
        equalizer.Applied.Should().ContainSingle();
    }

    [Fact]
    public void Without_the_effect_nothing_is_applied()
    {
        var settings = new InMemorySettingsService();
        var equalizer = new FakeEqualizer { IsAvailable = false };
        using var sut = new EqualizerSync(settings, equalizer);

        settings.Update(s => s with { Playback = s.Playback with { Equalizer = s.Playback.Equalizer with { Enabled = true } } });

        equalizer.Applied.Should().BeEmpty();
    }

    [Fact]
    public void Disposed_it_stops_following_the_settings()
    {
        var settings = new InMemorySettingsService();
        var equalizer = new FakeEqualizer();
        var sut = new EqualizerSync(settings, equalizer);

        sut.Dispose();
        settings.Update(s => s with { Playback = s.Playback with { Equalizer = s.Playback.Equalizer with { Enabled = true, Preamp = -3 } } });

        equalizer.Applied.Should().ContainSingle();
    }
}
