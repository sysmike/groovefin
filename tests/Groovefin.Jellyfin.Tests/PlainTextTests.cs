using Groovefin.Jellyfin.Mapping;

namespace Groovefin.Jellyfin.Tests;

public sealed class PlainTextTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("<p></p>", null)]
    [InlineData("Plain text.", "Plain text.")]
    [InlineData("Line one<br>Line two<BR />Line three", "Line one\nLine two\nLine three")]
    [InlineData("<p>First.</p><p>Second.</p>", "First.\nSecond.")]
    [InlineData("<a href=\"https://example.org\">Link</a> &amp; more &lt;3", "Link & more <3")]
    [InlineData("One\r\n\r\n\r\n\r\nTwo", "One\n\nTwo")]
    public void Html_fragments_become_plain_text(string? html, string? expected)
    {
        PlainText.FromHtml(html).Should().Be(expected);
    }
}
