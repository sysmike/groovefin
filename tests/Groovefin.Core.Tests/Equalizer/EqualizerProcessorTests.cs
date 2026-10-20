using Groovefin.Audio;

namespace Groovefin.Core.Tests.Equalizer;

public sealed class EqualizerProcessorTests
{
    private const int Rate = 48000;

    private static EqualizerParameters Band(int band, double gain, double preamp = 0)
    {
        var gains = new double[EqualizerBands.Count];
        gains[band] = gain;
        return new EqualizerParameters(true, preamp, gains);
    }

    private static float[] Sine(double frequency, double seconds, int channels = 1, int rate = Rate)
    {
        var frames = (int)(seconds * rate);
        var samples = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            samples[i * channels] = (float)(0.25 * Math.Sin(2 * Math.PI * frequency * i / rate));
        }

        return samples;
    }

    /// <summary>The level change in dB, measured after the filter has settled (the first second).</summary>
    private static double GainAt(double frequency, EqualizerParameters parameters)
    {
        var input = Sine(frequency, 2);
        var output = (float[])input.Clone();
        var sut = new EqualizerProcessor(Rate, 1);
        sut.Configure(parameters);
        sut.Process(output);
        return 20 * Math.Log10(Rms(output.AsSpan(Rate)) / Rms(input.AsSpan(Rate)));
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var s in samples)
        {
            sum += s * (double)s;
        }

        return Math.Sqrt(sum / samples.Length);
    }

    private static float[] Noise(int count)
    {
        var random = new Random(65);
        return [.. Enumerable.Range(0, count).Select(_ => (float)((random.NextDouble() * 2) - 1))];
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void Each_band_raises_its_centre_frequency_by_its_gain(int band)
    {
        GainAt(EqualizerBands.Frequencies[band], Band(band, 6)).Should().BeApproximately(6, 0.1);
    }

    [Fact]
    public void A_cut_lowers_the_centre_frequency()
    {
        GainAt(1000, Band(5, -12)).Should().BeApproximately(-12, 0.1);
    }

    [Fact]
    public void A_band_leaves_frequencies_far_away_alone()
    {
        GainAt(62, Band(5, 12)).Should().BeApproximately(0, 0.2);
        GainAt(16000, Band(5, 12)).Should().BeApproximately(0, 0.2);
    }

    [Fact]
    public void The_preamp_scales_everything()
    {
        GainAt(440, new EqualizerParameters(true, -6, new double[EqualizerBands.Count])).Should().BeApproximately(-6, 0.01);
    }

    [Fact]
    public void Flat_leaves_the_samples_bit_identical()
    {
        var input = Noise(4096);
        var output = (float[])input.Clone();
        var sut = new EqualizerProcessor(Rate, 2);
        sut.Configure(new EqualizerParameters(true, 0, new double[EqualizerBands.Count]));

        sut.Process(output);

        sut.IsActive.Should().BeFalse();
        output.Should().Equal(input);
    }

    [Fact]
    public void Turned_off_it_leaves_the_samples_bit_identical_whatever_the_bands()
    {
        var input = Noise(4096);
        var output = (float[])input.Clone();
        var sut = new EqualizerProcessor(Rate, 2);
        sut.Configure(new EqualizerParameters(false, 6, [12, 12, 12, 12, 12, 12, 12, 12, 12, 12]));

        sut.Process(output);

        output.Should().Equal(input);
    }

    [Fact]
    public void Channels_are_filtered_separately()
    {
        // Left: a sine, right: silence.
        var samples = Sine(1000, 0.5, channels: 2);
        var sut = new EqualizerProcessor(Rate, 2);
        sut.Configure(Band(5, 12));

        sut.Process(samples);

        samples.Where((_, i) => i % 2 == 1).Should().AllBeEquivalentTo(0f);
        samples.Where((_, i) => i % 2 == 0).Should().Contain(s => Math.Abs(s) > 0.01f);
    }

    [Fact]
    public void Processing_in_buffers_gives_the_same_result_as_at_once()
    {
        var whole = Noise(4800);
        var parts = (float[])whole.Clone();
        var once = new EqualizerProcessor(Rate, 2);
        var buffered = new EqualizerProcessor(Rate, 2);
        once.Configure(Band(2, 9, preamp: -3));
        buffered.Configure(Band(2, 9, preamp: -3));

        once.Process(whole);
        for (var start = 0; start < parts.Length; start += 480)
        {
            buffered.Process(parts.AsSpan(start, 480));
        }

        parts.Should().Equal(whole);
    }

    [Fact]
    public void Reset_forgets_the_ringing_of_earlier_samples()
    {
        var sut = new EqualizerProcessor(Rate, 1);
        sut.Configure(Band(0, 12));
        float[] impulse = [1, 0, 0, 0];
        sut.Process(impulse);

        sut.Reset();
        var silence = new float[256];
        sut.Process(silence);

        silence.Should().AllBeEquivalentTo(0f);
    }

    [Fact]
    public void A_band_too_close_to_the_nyquist_frequency_is_skipped()
    {
        // 16 kHz at 22.05 kHz would be above the Nyquist frequency.
        var sut = new EqualizerProcessor(22050, 1);
        sut.Configure(Band(9, 12));

        sut.IsActive.Should().BeFalse();
        PeakingFilter.Create(16000, 12, EqualizerBands.Q, 22050).IsIdentity.Should().BeTrue();
    }

    [Fact]
    public void A_partial_frame_at_the_end_is_left_alone()
    {
        var samples = new float[] { 0.5f, 0.5f, 0.5f };
        var sut = new EqualizerProcessor(Rate, 2);
        sut.Configure(new EqualizerParameters(true, -6, new double[EqualizerBands.Count]));

        sut.Process(samples);

        samples[2].Should().Be(0.5f);
        samples[0].Should().BeApproximately(0.25f, 0.01f);
    }

    [Fact]
    public void Bands_that_stay_keep_their_state_when_another_band_changes()
    {
        var input = Noise(9600);
        var reference = (float[])input.Clone();
        var changed = (float[])input.Clone();
        var steady = new EqualizerProcessor(Rate, 1);
        var sut = new EqualizerProcessor(Rate, 1);
        steady.Configure(Band(3, 6));
        sut.Configure(Band(3, 6));

        steady.Process(reference);
        sut.Process(changed.AsSpan(0, 4800));
        // A band is turned on and off again in the middle: band 3 goes on as if nothing happened.
        sut.Configure(new EqualizerParameters(true, 0, [0, 0, 0, 6, 0, 0, 0, 0, 0, 3]));
        sut.Configure(Band(3, 6));
        sut.Process(changed.AsSpan(4800));

        changed.Should().Equal(reference);
    }
}
