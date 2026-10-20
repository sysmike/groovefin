using System.Text.Json;
using Groovefin.Core.About;

namespace Groovefin.Core.Tests.About;

public sealed class ThirdPartyNoticesTests
{
    /// <summary>Packages that are only used to build, so they are not part of the app.</summary>
    private static readonly string[] BuildOnly = ["MinVer", "Microsoft.Windows.SDK.BuildTools", "Microsoft.Windows.SDK.BuildTools.MSIX"];

    [Fact]
    public void Every_package_in_the_app_has_a_notice()
    {
        var packages = AppPackages();

        packages.Should().NotBeEmpty();
        packages.Where(id => !ThirdPartyNotices.All.Any(c => c.Covers(id))).Should().BeEmpty("the About section lists the license of every shipped package");
    }

    [Fact]
    public void Every_notice_belongs_to_a_package_in_the_app()
    {
        var packages = AppPackages();

        ThirdPartyNotices.All.Where(c => !packages.Any(c.Covers)).Select(c => c.Name).Should().BeEmpty("notices for removed packages are stale");
    }

    [Fact]
    public void Notices_are_sorted_by_name_and_link_to_https()
    {
        ThirdPartyNotices.All.Select(c => c.Name).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        ThirdPartyNotices.All.Should().OnlyContain(c => c.Project.Scheme == Uri.UriSchemeHttps && c.License.Length > 0);
    }

    [Theory]
    [InlineData("Serilog", true)]
    [InlineData("Serilog.Sinks.File", true)]
    [InlineData("SerilogX", false)]
    [InlineData("Jellyfin.Sdk", true)]
    [InlineData("Jellyfin.Sdk.Extra", false)]
    public void Ids_match_exactly_or_by_prefix(string id, bool covered)
    {
        var component = new ThirdPartyComponent("x", "MIT", new Uri("https://example.org"), ["Serilog", "Serilog.", "Jellyfin.Sdk"]);

        component.Covers(id).Should().Be(covered);
    }

    /// <summary>The packages restored for the app (all target frameworks and runtimes), without build tools and projects.</summary>
    private static List<string> AppPackages()
    {
        using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Join(FindRoot(), "src", "Groovefin.App", "packages.lock.json")));
        return
        [
            .. lockFile.RootElement.GetProperty("dependencies").EnumerateObject()
                .SelectMany(framework => framework.Value.EnumerateObject())
                .Where(package => package.Value.GetProperty("type").GetString() != "Project")
                .Select(package => package.Name)
                .Where(id => !BuildOnly.Contains(id, StringComparer.OrdinalIgnoreCase) && !id.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Groovefin.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The source tree (Groovefin.sln) was not found.");
    }
}
