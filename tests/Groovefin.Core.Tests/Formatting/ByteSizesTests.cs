using Groovefin.Core.Formatting;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Formatting;

public sealed class ByteSizesTests
{
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public ByteSizesTests()
    {
        _strings.GetString("Size_Megabytes").Returns("{0} MB");
        _strings.GetString("Size_Gigabytes").Returns("{0} GB");
    }

    [Theory]
    [InlineData(0L, "0 MB")]
    [InlineData(400L * 1024, "0.4 MB")]
    [InlineData(5L * 1024 * 1024 + 300 * 1024, "5.3 MB")]
    [InlineData(250L * 1024 * 1024, "250 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(1536L * 1024 * 1024, "1.5 GB")]
    public void Sizes_use_megabytes_below_a_gigabyte(long bytes, string expected)
    {
        using var culture = new CultureScope("en-US");

        ByteSizes.Format(bytes, _strings).Should().Be(expected);
    }

    [Fact]
    public void Sizes_follow_the_current_culture()
    {
        using var culture = new CultureScope("de-DE");

        ByteSizes.Format(1536L * 1024 * 1024, _strings).Should().Be("1,5 GB");
    }
}
