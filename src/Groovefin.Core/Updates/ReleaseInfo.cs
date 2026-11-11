namespace Groovefin.Core.Updates;

/// <summary>A published release of Groovefin (#432).</summary>
/// <param name="Version">Its version without the tag's <c>v</c>, e.g. <c>1.2.0</c>.</param>
/// <param name="Page">Its page on GitHub, with the release notes and the files.</param>
public sealed record ReleaseInfo(string Version, Uri Page);

/// <summary>Tells which release is the newest.</summary>
public interface IReleaseSource
{
    /// <summary>Gets the newest final release (no pre-release, no draft), or <see langword="null"/> when there is none yet.</summary>
    /// <exception cref="HttpRequestException">The source could not be reached, or refused.</exception>
    /// <exception cref="IOException">The answer broke off.</exception>
    /// <exception cref="System.Text.Json.JsonException">The answer was not what was expected.</exception>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>Compares release versions of the form <c>major.minor.patch[-pre-release]</c>.</summary>
public static class ReleaseVersion
{
    /// <summary>
    /// Whether the final release <paramref name="release"/> is newer than the <paramref name="installed"/> version:
    /// a higher number, or the same number while a pre-release of it is installed. Text that is no such version
    /// is never newer.
    /// </summary>
    public static bool IsNewer(string release, string installed)
    {
        if (!TryParse(release, out var newest, out var newestIsPreRelease) || newestIsPreRelease
            || !TryParse(installed, out var current, out var currentIsPreRelease))
        {
            return false;
        }

        var order = newest.CompareTo(current);
        return order > 0 || (order == 0 && currentIsPreRelease);
    }

    private static bool TryParse(string? text, out (int Major, int Minor, int Patch) version, out bool isPreRelease)
    {
        version = default;
        isPreRelease = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Build metadata (+sha) says nothing about the order.
        var core = text.Trim().TrimStart('v', 'V').Split('+')[0];
        var dash = core.IndexOf('-', StringComparison.Ordinal);
        isPreRelease = dash >= 0;
        var parts = (dash >= 0 ? core[..dash] : core).Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = (major, minor, patch);
        return true;
    }
}
