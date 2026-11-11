using Groovefin.Core.Updates;

namespace Groovefin.Core.Tests.Updates;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.0.0", "0.9.0", true)]
    [InlineData("1.0.0", "0.9.0-beta3", true)]
    [InlineData("1.0.0", "1.0.0-beta9", true, "the final release follows its pre-releases")]
    [InlineData("1.0.0", "1.0.0-rc.1", true)]
    [InlineData("v1.2.0", "1.1.9", true)]
    [InlineData("1.10.0", "1.9.0", true, "numbers, not text")]
    [InlineData("1.0.1", "1.0.1-alpha.0.5", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0+abc1234", false)]
    [InlineData("1.0.0", "1.0.1-alpha.0.5", false, "a build after the release")]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("1.1.0-beta1", "1.0.0", false, "pre-releases are not announced")]
    [InlineData("nightly", "1.0.0", false)]
    [InlineData("1.0", "0.9.0", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData("1.0.0", "unknown", false)]
    public void A_final_release_is_newer_than_what_came_before_it(string release, string installed, bool newer, string because = "")
    {
        ReleaseVersion.IsNewer(release, installed).Should().Be(newer, because);
    }
}
