using Groovefin.Core.Images;

namespace Groovefin.Core.Tests.Images;

public sealed class ImageRequestTests
{
    [Theory]
    [InlineData(1, 64)]
    [InlineData(64, 64)]
    [InlineData(65, 128)]
    [InlineData(300, 384)]
    [InlineData(2048, 2048)]
    [InlineData(5000, 2048)]
    public void Width_is_rounded_up_to_the_next_bucket(int width, int expected)
    {
        new ImageRequest("id", ImageKind.Primary, "tag", width).BucketWidth.Should().Be(expected);
    }

    [Fact]
    public void Primary_multiplies_display_width_by_the_scale()
    {
        var request = ImageRequest.Primary("id", "tag", displayWidth: 160, scale: 1.5);

        request!.Width.Should().Be(240);
        request.BucketWidth.Should().Be(256);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Primary_without_tag_returns_null(string? tag)
    {
        ImageRequest.Primary("id", tag, 160, 1).Should().BeNull();
    }
}
