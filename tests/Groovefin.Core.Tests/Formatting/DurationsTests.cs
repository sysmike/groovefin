using Groovefin.Core.Formatting;
using Groovefin.Core.Services;

namespace Groovefin.Core.Tests.Formatting;

public sealed class DurationsTests
{
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public DurationsTests()
    {
        _strings.GetString("Duration_HoursMinutes").Returns("{0} h {1} min");
        _strings.GetString("Duration_Minutes").Returns("{0} min");
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(600, "10:00")]
    [InlineData(3723, "1:02:03")]
    public void Track_lengths_use_minutes_and_seconds(int seconds, string expected)
    {
        Durations.Track(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void Unknown_track_length_is_empty()
    {
        Durations.Track(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(42 * 60 + 10, "42 min")]
    [InlineData(61 * 60 + 40, "1 h 2 min")]
    [InlineData(120 * 60, "2 h 0 min")]
    public void Totals_use_hours_and_minutes(int seconds, string expected)
    {
        Durations.Total(TimeSpan.FromSeconds(seconds), _strings).Should().Be(expected);
    }
}
