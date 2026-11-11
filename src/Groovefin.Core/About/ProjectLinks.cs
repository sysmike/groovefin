namespace Groovefin.Core.About;

/// <summary>Links to the project on GitHub.</summary>
public static class ProjectLinks
{
    public static Uri Repository { get; } = new("https://github.com/sysmike/groovefin");

    public static Uri Issues { get; } = new("https://github.com/sysmike/groovefin/issues");

    /// <summary>Gets the page that lists the releases; each release's own page is below it.</summary>
    public static Uri Releases { get; } = new("https://github.com/sysmike/groovefin/releases");

    /// <summary>Gets where GitHub tells the newest final release (#432).</summary>
    public static Uri LatestReleaseApi { get; } = new("https://api.github.com/repos/sysmike/groovefin/releases/latest");

    /// <summary>Gets the page of the commit the app was built from.</summary>
    public static Uri Commit(string sha) => new($"{Repository}/commit/{Uri.EscapeDataString(sha)}");
}
