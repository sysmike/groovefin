using Groovefin.Core.Api;

namespace Groovefin.Jellyfin.Tests;

public sealed class AuthorizationHeaderTests
{
    private static readonly ClientIdentity Identity = new("Groovefin", "1.2.3", "Living Room PC", "abc123");

    [Fact]
    public void Header_identifies_client_device_and_version_without_token()
    {
        var header = AuthorizationHeader.Build(Identity, accessToken: null);

        header.Should().Be("MediaBrowser Client=\"Groovefin\", Device=\"Living%20Room%20PC\", DeviceId=\"abc123\", Version=\"1.2.3\"");
    }

    [Fact]
    public void Header_keeps_client_information_when_a_token_is_set()
    {
        var header = AuthorizationHeader.Build(Identity, "secret-token");

        header.Should().StartWith("MediaBrowser Client=\"Groovefin\"")
            .And.Contain("DeviceId=\"abc123\"")
            .And.EndWith(", Token=\"secret-token\"");
    }

    [Fact]
    public void Values_with_quotes_commas_and_umlauts_are_escaped()
    {
        var header = AuthorizationHeader.Build(Identity with { DeviceName = "Mike's \"PC\", Köln" }, null);

        header.Should().Contain("Device=\"Mike%27s%20%22PC%22%2C%20K%C3%B6ln\"");
    }
}
