using Groovefin.Audio;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Equalizer;

public sealed class EqualizerParametersTests
{
    [Fact]
    public void Format_and_parse_round_trip_in_any_culture()
    {
        using var culture = new CultureScope("de-DE");
        var parameters = new EqualizerParameters(true, -3.5, [6, 4.5, 0, -1, 0, 0, 0, 0, 2, 12]);

        var text = parameters.Format();

        text.Should().Be("1;-3.5;6,4.5,0,-1,0,0,0,0,2,12");
        EqualizerParameters.TryParse(text, out var parsed).Should().BeTrue();
        parsed.Should().Be(parameters);
    }

    [Fact]
    public void Gains_are_limited_and_missing_bands_are_flat()
    {
        var parameters = new EqualizerParameters(true, -20, [30, double.NaN, -15]);

        parameters.Preamp.Should().Be(EqualizerBands.MinGain);
        parameters.Gains.Should().Equal(12, 0, -12, 0, 0, 0, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1;0")]
    [InlineData("2;0;0")]
    [InlineData("1;x;0")]
    [InlineData("1;0;0,a")]
    [InlineData("1;Infinity;0")]
    public void Garbage_does_not_parse(string? text)
    {
        EqualizerParameters.TryParse(text, out var parsed).Should().BeFalse();
        parsed.Should().Be(EqualizerParameters.Off);
    }

    [Fact]
    public void Active_only_when_on_and_something_is_not_zero()
    {
        var flat = new double[EqualizerBands.Count];

        new EqualizerParameters(true, 0, flat).IsActive.Should().BeFalse();
        new EqualizerParameters(false, 3, [3]).IsActive.Should().BeFalse();
        new EqualizerParameters(true, -1, flat).IsActive.Should().BeTrue();
        new EqualizerParameters(true, 0, [0, 0, 1]).IsActive.Should().BeTrue();
    }

    [Fact]
    public void Gains_are_rounded_to_hundredths_of_a_decibel()
    {
        var parameters = new EqualizerParameters(true, -0.0001, [1.23456, -0.004]);

        parameters.Preamp.Should().Be(0);
        parameters.Gains.Should().StartWith([1.23, 0]);
        parameters.Format().Should().StartWith("1;0;1.23,0,");
        parameters.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Equal_values_are_equal()
    {
        new EqualizerParameters(true, 1, [1, 2]).Should().Be(new EqualizerParameters(true, 1, [1, 2, 0]));
        new EqualizerParameters(true, 1, [1, 2]).Should().NotBe(new EqualizerParameters(true, 1, [1, 3]));
        new EqualizerParameters(true, 1, [1, 2]).GetHashCode().Should().Be(new EqualizerParameters(true, 1, [1, 2]).GetHashCode());
    }
}
