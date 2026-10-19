using Groovefin.Core.Models;
using Groovefin.Core.Playback;

namespace Groovefin.Core.Tests.Playback;

public sealed class NormalizationTests
{
    private static Track Song(double? trackGain, double? albumGain) =>
        new() { Id = "t", Name = "t", NormalizationGain = trackGain, AlbumNormalizationGain = albumGain };

    [Theory]
    [InlineData(NormalizationMode.Off, -6.0, -3.0, null)]
    [InlineData(NormalizationMode.Track, -6.0, -3.0, -6.0)]
    [InlineData(NormalizationMode.Album, -6.0, -3.0, -3.0)]
    [InlineData(NormalizationMode.Album, -6.0, null, -6.0)]
    [InlineData(NormalizationMode.Track, null, -3.0, null)]
    [InlineData(NormalizationMode.Album, null, null, null)]
    public void The_mode_picks_the_gain(NormalizationMode mode, double? trackGain, double? albumGain, double? expected)
    {
        Normalization.GainDb(Song(trackGain, albumGain), mode).Should().Be(expected);
    }

    [Theory]
    [InlineData(4.5)]
    [InlineData(0.0)]
    public void Positive_gains_are_limited_so_nothing_clips(double gain)
    {
        Normalization.GainDb(Song(gain, null), NormalizationMode.Track).Should().Be(Normalization.MaxGainDb);
        Normalization.VolumeFactor(Song(gain, null), NormalizationMode.Track).Should().Be(1);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    public void Broken_gains_are_ignored(double gain)
    {
        Normalization.GainDb(Song(gain, null), NormalizationMode.Track).Should().BeNull();
        Normalization.VolumeFactor(Song(gain, null), NormalizationMode.Track).Should().Be(1);
    }

    [Theory]
    [InlineData(-6.0, 0.501187)]
    [InlineData(-20.0, 0.1)]
    [InlineData(-0.5, 0.944061)]
    public void The_volume_factor_is_the_gain_in_amplitude(double gain, double expected)
    {
        Normalization.VolumeFactor(Song(gain, null), NormalizationMode.Track).Should().BeApproximately(expected, 1e-6);
    }

    [Fact]
    public void Without_a_gain_the_volume_stays()
    {
        Normalization.VolumeFactor(Song(null, null), NormalizationMode.Album).Should().Be(1);
    }

    [Fact]
    public void A_null_track_is_rejected()
    {
        var act = () => Normalization.GainDb(null!, NormalizationMode.Track);

        act.Should().Throw<ArgumentNullException>();
    }
}
