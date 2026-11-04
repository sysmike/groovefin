namespace Groovefin.Core.Models;

/// <summary>A track's file on the server: its container (e.g. <c>flac</c>) and size in bytes (#327, #331).</summary>
public sealed record TrackFile(string? Container, long Size)
{
    /// <summary>Gets the mark the downloads keep, e.g. <c>flac:31457280</c>: another one means another file.</summary>
    public string Mark => $"{Container}:{Size.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
