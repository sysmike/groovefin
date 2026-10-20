namespace Groovefin.Core.About;

/// <summary>A component Groovefin ships, with its license.</summary>
/// <param name="Name">The display name, e.g. "Serilog".</param>
/// <param name="License">An SPDX identifier, or the name of the license for non-SPDX terms.</param>
/// <param name="Project">The project page, where the license text is.</param>
/// <param name="Packages">The NuGet package IDs (or ID prefixes ending with '.') it covers.</param>
public sealed record ThirdPartyComponent(string Name, string License, Uri Project, IReadOnlyList<string> Packages)
{
    /// <summary>Gets a value indicating whether the NuGet package <paramref name="id"/> belongs to this component.</summary>
    public bool Covers(string id) =>
        Packages.Any(p => p.EndsWith('.')
            ? id.StartsWith(p, StringComparison.OrdinalIgnoreCase)
            : string.Equals(id, p, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The third-party components in the app package, for the About section. A test checks the list against
/// the app's NuGet lock file, so a new dependency needs an entry here.
/// </summary>
public static class ThirdPartyNotices
{
    /// <summary>Gets the components by name.</summary>
    public static IReadOnlyList<ThirdPartyComponent> All { get; } =
    [
        new(".NET and Microsoft.Extensions", "MIT", new("https://github.com/dotnet/runtime"), ["Microsoft.Extensions.", "Microsoft.Win32.", "System."]),
        new("H.NotifyIcon", "MIT", new("https://github.com/HavenDV/H.NotifyIcon"), ["H.NotifyIcon.", "H.NotifyIcon", "H.GeneratedIcons."]),
        new("Jellyfin SDK", "MIT", new("https://github.com/jellyfin/jellyfin-sdk-csharp"), ["Jellyfin.Sdk"]),
        new("Microsoft Kiota", "MIT", new("https://github.com/microsoft/kiota-dotnet"), ["Microsoft.Kiota."]),
        new("Microsoft.IO.RecyclableMemoryStream", "MIT", new("https://github.com/microsoft/Microsoft.IO.RecyclableMemoryStream"), ["Microsoft.IO.RecyclableMemoryStream"]),
        new("MVVM Toolkit", "MIT", new("https://github.com/CommunityToolkit/dotnet"), ["CommunityToolkit.Mvvm", "CommunityToolkit.Common"]),
        new("Polly", "BSD-3-Clause", new("https://github.com/App-vNext/Polly"), ["Polly."]),
        new("Serilog", "Apache-2.0", new("https://github.com/serilog/serilog"), ["Serilog", "Serilog."]),
        new("std-uritemplate", "Apache-2.0", new("https://github.com/std-uritemplate/std-uritemplate"), ["Std.UriTemplate"]),
        new("WebView2", "BSD-3-Clause", new("https://aka.ms/webview"), ["Microsoft.Web.WebView2"]),
        new("Windows App SDK", "Microsoft Software License Terms", new("https://github.com/microsoft/WindowsAppSDK"), ["Microsoft.WindowsAppSDK", "Microsoft.WindowsAppSDK.", "Microsoft.Windows.AI."]),
        new("Windows Community Toolkit", "MIT", new("https://github.com/CommunityToolkit/Windows"), ["CommunityToolkit.WinUI."]),
    ];
}
