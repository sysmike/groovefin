using Groovefin.Core.Servers;

namespace Groovefin.Core.Tests.Servers;

public sealed class ServerAddressTests
{
    [Theory]
    [InlineData("192.168.1.10:8096", "http://192.168.1.10:8096/")]
    [InlineData("  media.local:8096  ", "http://media.local:8096/")]
    [InlineData("https://media.example.com", "https://media.example.com/")]
    [InlineData("https://media.example.com/jellyfin/", "https://media.example.com/jellyfin")]
    [InlineData("http://host:8096/web/index.html?x=1#/home", "http://host:8096/web/index.html")]
    [InlineData("HTTP://Host:8096", "http://host:8096/")]
    public void Valid_addresses_are_normalized(string input, string expected)
    {
        ServerAddress.TryNormalize(input, out var uri).Should().BeTrue();
        uri.AbsoluteUri.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://host")]
    [InlineData("http://")]
    [InlineData("my server")]
    public void Invalid_addresses_are_rejected(string? input)
    {
        ServerAddress.TryNormalize(input, out _).Should().BeFalse();
    }
}
