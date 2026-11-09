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

    private static Track Song(string album, int number, double? trackGain, double? albumGain = -3.0) => new()
    {
        Id = $"{album}{number}",
        Name = $"{album}{number}",
        AlbumId = album,
        TrackNumber = number,
        NormalizationGain = trackGain,
        AlbumNormalizationGain = albumGain,
    };

    [Fact]
    public void Automatic_takes_the_album_gain_while_an_album_plays_in_order()
    {
        Track[] album = [Song("x", 1, -8), Song("x", 2, -1), Song("x", 3, -5)];

        Enumerable.Range(0, 3).Select(i => Normalization.GainDb(album, i, NormalizationMode.Automatic))
            .Should().Equal(-3.0, -3.0, -3.0);
    }

    [Fact]
    public void Automatic_takes_the_track_gain_in_a_mix_or_out_of_order()
    {
        Track[] mixed = [Song("x", 1, -8), Song("y", 1, -1), Song("x", 2, -5)];
        Track[] shuffled = [Song("x", 3, -8), Song("x", 1, -1)];

        Enumerable.Range(0, 3).Select(i => Normalization.GainDb(mixed, i, NormalizationMode.Automatic)).Should().Equal(-8.0, -1.0, -5.0);
        Normalization.GainDb(shuffled, 0, NormalizationMode.Automatic).Should().Be(-8.0, "track 1 after track 3 is not the album in order");
        Normalization.GainDb(shuffled, 1, NormalizationMode.Automatic).Should().Be(-1.0);
        Normalization.GainDb(Song("x", 1, -8), NormalizationMode.Automatic).Should().Be(-8.0, "alone it has no album around it");
    }

    [Fact]
    public void The_next_disc_still_counts_as_in_order()
    {
        Track[] album = [Song("x", 12, -8) with { DiscNumber = 1 }, Song("x", 1, -1) with { DiscNumber = 2 }];

        Normalization.GainDb(album, 1, NormalizationMode.Automatic).Should().Be(-3.0);
    }

    [Fact]
    public void A_track_without_the_album_gain_takes_it_from_its_album_in_the_queue()
    {
        Track[] album = [Song("x", 1, -8), Song("x", 2, -1, albumGain: null), Song("y", 1, -6, albumGain: null)];

        Normalization.GainDb(album, 1, NormalizationMode.Album).Should().Be(-3.0);
        Normalization.GainDb(album, 1, NormalizationMode.Automatic).Should().Be(-3.0);
        Normalization.GainDb(album, 2, NormalizationMode.Album).Should().Be(-6.0, "nothing of its album has one: its track gain");
    }

    [Theory]
    [InlineData(NormalizationTarget.Server, -8.0, -8.0)]
    [InlineData(NormalizationTarget.Louder, -8.0, -4.0)]
    [InlineData(NormalizationTarget.Loudest, -8.0, -1.0)]
    [InlineData(NormalizationTarget.Loudest, -3.0, 0.0)]
    [InlineData(NormalizationTarget.Loudest, 2.0, 0.0)]
    public void A_louder_target_lifts_the_gain_but_never_above_zero(NormalizationTarget target, double gain, double expected)
    {
        Normalization.GainDb(Song(gain, null), NormalizationMode.Track, Normalization.PreampDb(target)).Should().Be(expected);
    }
}
