using Groovefin.Core.Security;

namespace Groovefin.Core.Tests.Security;

public sealed class CredentialResourceTests
{
    [Fact]
    public void Resource_name_is_prefixed_and_normalized()
    {
        CredentialResource.ForServer(" ABCDEF0123 ").Should().Be("Groovefin/jellyfin/abcdef0123");
    }

    [Fact]
    public void Same_server_id_in_different_case_maps_to_the_same_resource()
    {
        CredentialResource.ForServer("ABC").Should().Be(CredentialResource.ForServer("abc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_server_id_is_rejected(string serverId)
    {
        var act = () => CredentialResource.ForServer(serverId);

        act.Should().Throw<ArgumentException>();
    }
}
