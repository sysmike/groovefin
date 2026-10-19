using System.Reflection;

namespace Groovefin.Jellyfin.Tests;

/// <summary>
/// Guards the layering rules from docs/architecture.md: the Jellyfin layer depends on Core only.
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
        "Groovefin.Playback",
        "Groovefin.App",
    ];

    [Fact]
    public void Jellyfin_layer_does_not_reference_ui_or_platform_assemblies()
    {
        var jellyfin = Assembly.Load("Groovefin.Jellyfin");

        var forbidden = jellyfin.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

        forbidden.Should().BeEmpty();
    }
}
