namespace Groovefin.Core.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3", null)]
    [InlineData("1.2.3+abcdef0123456789", "1.2.3", "abcdef0")]
    [InlineData("0.3.0-beta.1+abc", "0.3.0-beta.1", "abc")]
    [InlineData("1.0.0+", "1.0.0", null)]
    public void Parse_splits_version_and_commit(string input, string version, string? commit)
    {
        var result = AppVersion.Parse(input);

        result.Version.Should().Be(version);
        result.Commit.Should().Be(commit);
    }

    [Fact]
    public void DisplayText_includes_commit_when_known()
    {
        new AppVersion("1.0.0", "abc1234").DisplayText.Should().Be("1.0.0 (abc1234)");
        new AppVersion("1.0.0", null).DisplayText.Should().Be("1.0.0");
    }

    [Fact]
    public void FromAssembly_reads_informational_version()
    {
        var result = AppVersion.FromAssembly(typeof(AppVersion).Assembly);

        result.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Parse_rejects_blank_input(string input)
    {
        var act = () => AppVersion.Parse(input);

        act.Should().Throw<ArgumentException>();
    }
}
