namespace Groovefin.Core.About;

/// <summary>Links to the project on GitHub.</summary>
public static class ProjectLinks
{
    public static Uri Repository { get; } = new("https://github.com/sysmike/groovefin");

    public static Uri Issues { get; } = new("https://github.com/sysmike/groovefin/issues");

    /// <summary>Gets the page of the commit the app was built from.</summary>
    public static Uri Commit(string sha) => new($"{Repository}/commit/{Uri.EscapeDataString(sha)}");
}
