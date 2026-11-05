using Groovefin.Core.Images;

namespace Groovefin.Core.Tests.Images;

public sealed class CoverColourTests
{
    /// <summary>Pixels as the decoder gives them: blue, green, red, alpha.</summary>
    private static byte[] Pixels(params (byte R, byte G, byte B, int Count)[] runs) =>
    [
        .. runs.SelectMany(run => Enumerable.Repeat(new[] { run.B, run.G, run.R, (byte)255 }, run.Count).SelectMany(p => p)),
    ];

    [Fact]
    public void The_most_common_strong_colour_wins()
    {
        var pixels = Pixels((220, 30, 40, 60), (30, 60, 220, 30), (240, 240, 240, 200));

        // The white background is no colour; red covers more of the rest than blue.
        CoverColour.Pick(pixels).Should().Be(new CoverColour(220, 30, 40));
    }

    [Fact]
    public void Shades_of_one_hue_are_averaged()
    {
        var pixels = Pixels((200, 20, 20, 10), (180, 40, 40, 10));

        var colour = CoverColour.Pick(pixels)!.Value;

        colour.R.Should().BeInRange(180, 200);
        colour.G.Should().BeInRange(20, 40);
    }

    [Fact]
    public void Dark_and_washed_out_pixels_do_not_count_as_colour()
    {
        // Bright red is only 10 pixels of 100; a dark blue and a pale pink must not outweigh it.
        var pixels = Pixels((10, 10, 40, 40), (250, 235, 235, 50), (230, 20, 20, 10));

        CoverColour.Pick(pixels).Should().Be(new CoverColour(230, 20, 20));
    }

    [Fact]
    public void A_speck_of_colour_on_a_grey_cover_is_not_its_colour()
    {
        var pixels = Pixels((40, 40, 40, 98), (230, 20, 20, 2));

        CoverColour.Pick(pixels).Should().Be(new CoverColour(44, 40, 40));
    }

    [Fact]
    public void A_grey_cover_gets_its_average_colour()
    {
        var pixels = Pixels((0, 0, 0, 50), (200, 200, 200, 50));

        CoverColour.Pick(pixels).Should().Be(new CoverColour(100, 100, 100));
    }

    [Fact]
    public void Transparent_pixels_are_left_out()
    {
        byte[] pixels = [0, 0, 255, 0, 255, 0, 0, 255];

        CoverColour.Pick(pixels).Should().Be(new CoverColour(0, 0, 255));
    }

    [Fact]
    public void No_opaque_pixels_give_no_colour()
    {
        CoverColour.Pick([]).Should().BeNull();
        CoverColour.Pick([10, 20, 30, 0]).Should().BeNull();
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(250, 230, 120)]
    [InlineData(80, 200, 230)]
    [InlineData(230, 30, 40)]
    public void A_light_colour_is_darkened_for_white_text(byte r, byte g, byte b)
    {
        var colour = new CoverColour(r, g, b).ForWhiteText();

        colour.Luminance.Should().BeLessThanOrEqualTo(CoverColour.MaxLuminance);
        Contrast(colour).Should().BeGreaterThanOrEqualTo(7);

        // As light as it may be: one step brighter is too light.
        var brighter = new CoverColour((byte)Math.Min(255, colour.R + 3), (byte)Math.Min(255, colour.G + 3), (byte)Math.Min(255, colour.B + 3));
        brighter.Luminance.Should().BeGreaterThan(CoverColour.MaxLuminance);
    }

    [Fact]
    public void Darkening_keeps_the_hue()
    {
        var colour = new CoverColour(240, 120, 60).ForWhiteText();

        // The channels keep their ratio: red twice green, green twice blue.
        (colour.R / (double)colour.G).Should().BeApproximately(2, 0.1);
        (colour.G / (double)colour.B).Should().BeApproximately(2, 0.15);
    }

    [Fact]
    public void A_dark_colour_stays_as_it_is()
    {
        var dark = new CoverColour(20, 40, 80);

        dark.ForWhiteText().Should().Be(dark);
    }

    private static double Contrast(CoverColour background) => 1.05 / (background.Luminance + 0.05);
}
