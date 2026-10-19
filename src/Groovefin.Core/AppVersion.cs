using System.Reflection;

namespace Groovefin.Core;

/// <summary>
/// Version information of the running app.
/// </summary>
/// <param name="Version">Semantic version, e.g. <c>0.3.0-beta.1</c>.</param>
/// <param name="Commit">Short commit SHA, or <see langword="null"/> when unknown.</param>
public sealed record AppVersion(string Version, string? Commit)
{
    /// <summary>Gets the version and commit for display, e.g. <c>0.3.0 (abc1234)</c>.</summary>
    public string DisplayText => Commit is null ? Version : $"{Version} ({Commit})";

    /// <summary>
    /// Reads the version from an assembly's informational version (<c>1.2.3+sha</c>).
    /// </summary>
    public static AppVersion FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Parse(informational ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    /// <summary>Parses <c>version[+metadata]</c>; the commit is the first 7 characters of the metadata.</summary>
    public static AppVersion Parse(string informationalVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(informationalVersion);

        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return new AppVersion(informationalVersion, null);
        }

        var metadata = informationalVersion[(plus + 1)..];
        var commit = metadata.Length == 0 ? null : metadata[..Math.Min(7, metadata.Length)];
        return new AppVersion(informationalVersion[..plus], commit);
    }
}
