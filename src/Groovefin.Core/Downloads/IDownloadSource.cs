using Groovefin.Core.Models;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Downloads;

/// <summary>What a song is downloaded as (#327): the original file, or AAC at a bitrate.</summary>
public sealed record DownloadFormat(DownloadFormatKind Kind, int AacBitrateKbps = 0)
{
    public static DownloadFormat From(DownloadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Format == DownloadFormatKind.Aac ? new(DownloadFormatKind.Aac, settings.AacBitrateKbps) : new(DownloadFormatKind.Original);
    }

    /// <summary>The bitrate of an original Windows cannot play (#362): AAC, as good as a stream would get it.</summary>
    public const int ConvertedBitrateKbps = 320;

    /// <summary>
    /// Containers Windows plays (#362), as <c>StreamUrlBuilder.DirectPlayContainers</c> streams them without
    /// transcoding. The server may name several for one file, e.g. <c>mov,mp4,m4a</c>.
    /// </summary>
    private static readonly HashSet<string> PlayableContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "aac", "m4a", "m4b", "flac", "wav", "wma",
    };

    /// <summary>Gets the name the index keeps, e.g. <c>original</c> or <c>aac256</c>: another one downloads the song again.</summary>
    public string Key => Kind == DownloadFormatKind.Aac ? $"aac{AacBitrateKbps}" : "original";

    /// <summary>
    /// What the song's file comes as (#362): an original in a container Windows cannot play as AAC at
    /// <see cref="ConvertedBitrateKbps"/>, else this format. A file of unknown container stays the original.
    /// </summary>
    public DownloadFormat For(TrackFile? file) =>
        Kind == DownloadFormatKind.Original && file?.Container is { } container && !Plays(container)
            ? new(DownloadFormatKind.Aac, ConvertedBitrateKbps)
            : this;

    /// <summary>Whether Windows plays a file in <paramref name="container"/>, or one of the names it lists.</summary>
    public static bool Plays(string container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return container.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(PlayableContainers.Contains);
    }
}

/// <summary>A song's file as it arrives from the server. Disposing it closes the connection.</summary>
public sealed class DownloadResponse(Stream content, long? length, string extension, bool resumed, IDisposable? owner = null) : IDisposable
{
    /// <summary>Gets the file's bytes, from the requested offset when <see cref="Resumed"/>, else from the start.</summary>
    public Stream Content { get; } = content;

    /// <summary>Gets the number of bytes <see cref="Content"/> holds, when the server said so.</summary>
    public long? Length { get; } = length;

    /// <summary>Gets the file name extension for the format, with its dot, e.g. <c>.flac</c>.</summary>
    public string Extension { get; } = extension;

    /// <summary>Gets a value indicating whether the server sent the file from the requested offset on.</summary>
    public bool Resumed { get; } = resumed;

    public void Dispose()
    {
        Content.Dispose();
        owner?.Dispose();
    }
}

/// <summary>Downloads songs from the active server (#327).</summary>
public interface IDownloadSource
{
    /// <summary>
    /// Opens the song's file in <paramref name="format"/>. An original resumes at <paramref name="offset"/>
    /// when the server supports it (<see cref="DownloadResponse.Resumed"/>); a transcode always starts at 0.
    /// </summary>
    /// <exception cref="Api.JellyfinException">The server refused, or could not be reached.</exception>
    Task<DownloadResponse> OpenAsync(string trackId, DownloadFormat format, long offset, CancellationToken cancellationToken);
}
