using System.Reflection;

namespace Groovefin.Core.Tests;

/// <summary>
/// Guards the layering rules from docs/architecture.md: Core stays free of UI, WinRT and SDK dependencies.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.WinUI",
        "Microsoft.UI",
        "Microsoft.Windows.SDK.NET",
        "Microsoft.WindowsAppRuntime",
        "WinRT.Runtime",
        "Jellyfin.Sdk",
        "Groovefin.Jellyfin",
        "Groovefin.Playback",
        "Groovefin.App",
    ];

    [Fact]
    public void Core_does_not_reference_ui_platform_or_sdk_assemblies()
    {
        var core = Assembly.Load("Groovefin.Core");

        var forbidden = core.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

        forbidden.Should().BeEmpty();
    }
}
