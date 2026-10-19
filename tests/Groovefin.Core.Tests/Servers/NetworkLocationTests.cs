using Groovefin.Core.Api;
using Groovefin.Core.Servers;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Tests.Servers;

public sealed class NetworkLocationTests
{
    [Theory]
    [InlineData("http://localhost:8096")]
    [InlineData("http://127.0.0.1:8096")]
    [InlineData("http://10.0.0.5:8096")]
    [InlineData("http://172.16.0.1:8096")]
    [InlineData("http://172.31.255.254:8096")]
    [InlineData("http://192.168.1.10:8096")]
    [InlineData("http://169.254.10.10:8096")]
    [InlineData("http://[::1]:8096")]
    [InlineData("http://[fe80::1]:8096")]
    [InlineData("http://[fd12:3456::1]:8096")]
    [InlineData("http://[::ffff:192.168.1.10]:8096")]
    [InlineData("http://nas:8096")]
    [InlineData("http://jellyfin.local:8096")]
    [InlineData("http://media.home.arpa")]
    [InlineData("http://server.LAN")]
    public void Home_network_addresses_are_local(string url)
    {
        NetworkLocation.IsLocal(new Uri(url)).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://media.example.com")]
    [InlineData("https://jellyfin.example.com/jellyfin")]
    [InlineData("http://172.32.0.1:8096")]
    [InlineData("http://172.15.0.1:8096")]
    [InlineData("http://8.8.8.8")]
    [InlineData("http://100.101.102.103:8096")]
    [InlineData("http://[2001:db8::1]:8096")]
    [InlineData("http://[::ffff:8.8.8.8]")]
    public void Other_addresses_are_remote(string url)
    {
        NetworkLocation.IsLocal(new Uri(url)).Should().BeFalse();
    }

    [Fact]
    public void Null_is_rejected()
    {
        var act = () => NetworkLocation.IsLocal(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Streaming_defaults_play_originals_locally_and_limit_remote_servers()
    {
        var settings = new StreamingSettings();

        settings.OptionsFor(new Uri("http://192.168.1.10:8096")).Should().Be(new StreamOptions(null, TranscodingCodec.Aac));
        settings.OptionsFor(new Uri("https://media.example.com")).Should().Be(new StreamOptions(320, TranscodingCodec.Aac));
    }

    [Fact]
    public void Streaming_options_follow_the_settings()
    {
        var settings = new StreamingSettings { LocalMaxBitrateKbps = 256, RemoteMaxBitrateKbps = null, Codec = TranscodingCodec.Mp3 };

        settings.OptionsFor(new Uri("http://nas:8096")).Should().Be(new StreamOptions(256, TranscodingCodec.Mp3));
        settings.OptionsFor(new Uri("https://media.example.com")).Should().Be(new StreamOptions(null, TranscodingCodec.Mp3));
    }
}
