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
    [InlineData("192.168.1.10:8920", "https://192.168.1.10:8920/")]
    [InlineData("media.example.com:443", "https://media.example.com/")]
    [InlineData("http://192.168.1.10:8920", "http://192.168.1.10:8920/")]
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

    [Theory]
    [InlineData("http://192.168.1.10:8096/", true)]
    [InlineData("http://localhost:8096/", true)]
    [InlineData("https://media.example.com/", false)]
    public void Plain_http_is_unencrypted_also_on_the_local_network(string address, bool unencrypted)
    {
        ServerAddress.IsUnencrypted(new Uri(address)).Should().Be(unencrypted);
    }
}
