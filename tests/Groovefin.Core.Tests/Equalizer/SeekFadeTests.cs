using Groovefin.Audio;

namespace Groovefin.Core.Tests.Equalizer;

public sealed class SeekFadeTests
{
    private const int Rate = 48000;

    /// <summary>40 ms silent, then 20 ms rising, at 48 kHz.</summary>
    private const int MuteFrames = 1920;
    private const int FadeFrames = 960;

    private static float[] Ones(int frames, int channels = 2) => Enumerable.Repeat(1f, frames * channels).ToArray();

    [Fact]
    public void Without_a_jump_the_samples_stay_as_they_are()
    {
        var sut = new SeekFade(Rate, 2);
        var samples = Ones(100);

        sut.Process(samples);

        sut.IsActive.Should().BeFalse();
        samples.Should().AllSatisfy(s => s.Should().Be(1f));
    }

    [Fact]
    public void After_a_jump_it_is_silent_then_rises_to_full_level_in_both_channels()
    {
        var sut = new SeekFade(Rate, 2);
        var samples = Ones(MuteFrames + FadeFrames + 100);

        sut.Start();
        sut.Process(samples);

        samples.AsSpan(0, MuteFrames * 2).ToArray().Should().AllSatisfy(s => s.Should().Be(0f));
        var rise = Enumerable.Range(MuteFrames, FadeFrames).Select(frame => samples[frame * 2]).ToList();
        rise.Should().BeInAscendingOrder();
        rise[0].Should().BeGreaterThan(0f).And.BeLessThan(0.001f, "the level starts from silence, without a step");
        rise[^1].Should().Be(1f);
        Enumerable.Range(MuteFrames, FadeFrames).Should().AllSatisfy(frame => samples[(frame * 2) + 1].Should().Be(samples[frame * 2]));
        samples.AsSpan((MuteFrames + FadeFrames) * 2).ToArray().Should().AllSatisfy(s => s.Should().Be(1f));
        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void The_fade_carries_on_across_buffers()
    {
        var whole = Ones(MuteFrames + FadeFrames, channels: 1);
        var inOne = new SeekFade(Rate, 1);
        inOne.Start();
        inOne.Process(whole);

        var pieces = Ones(MuteFrames + FadeFrames, channels: 1);
        var inPieces = new SeekFade(Rate, 1);
        inPieces.Start();
        for (var start = 0; start < pieces.Length; start += 441)
        {
            inPieces.Process(pieces.AsSpan(start, Math.Min(441, pieces.Length - start)));
        }

        pieces.Should().Equal(whole);
    }

    [Fact]
    public void A_new_jump_during_the_fade_starts_from_silence_again()
    {
        var sut = new SeekFade(Rate, 1);
        sut.Start();
        sut.Process(Ones(MuteFrames + (FadeFrames / 2), channels: 1));

        sut.Start();
        var samples = Ones(10, channels: 1);
        sut.Process(samples);

        samples.Should().AllSatisfy(s => s.Should().Be(0f));
        sut.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_partial_frame_at_the_end_is_left_as_it_is()
    {
        var sut = new SeekFade(Rate, 2);
        var samples = new float[] { 1f, 1f, 1f };

        sut.Start();
        sut.Process(samples);

        samples.Should().Equal(0f, 0f, 1f);
    }

    [Fact]
    public void The_lengths_follow_the_sample_rate()
    {
        var sut = new SeekFade(44100, 1);
        var samples = Ones(3000, channels: 1);

        sut.Start();
        sut.Process(samples);

        // 40 ms at 44.1 kHz are 1764 frames, then 882 rising.
        samples[1763].Should().Be(0f);
        samples[1764].Should().BeGreaterThan(0f);
        samples[1764 + 881].Should().Be(1f);
        sut.IsActive.Should().BeFalse();
    }
}
