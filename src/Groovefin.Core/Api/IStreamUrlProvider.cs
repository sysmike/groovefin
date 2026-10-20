namespace Groovefin.Core.Api;

/// <summary>Codec the server transcodes to when a track cannot be played directly or exceeds the bitrate limit.</summary>
public enum TranscodingCodec
{
    Aac,
    Mp3,
}

/// <summary>How a track is streamed.</summary>
/// <param name="MaxBitrateKbps">Highest bitrate in kbit/s; <see langword="null"/> plays the original file whenever possible.</param>
/// <param name="Codec">Codec for transcoding.</param>
public sealed record StreamOptions(int? MaxBitrateKbps, TranscodingCodec Codec)
{
    /// <summary>
    /// Gets where a transcoded stream starts; zero for the start. Used to seek in a transcode, which can
    /// only be read from the front (#154). A file the server plays as it is ignores it.
    /// </summary>
    public TimeSpan StartPosition { get; init; }
}

/// <summary>Builds stream URLs for the active connection.</summary>
public interface IStreamUrlProvider
{
    /// <summary>
    /// Gets the URL that streams <paramref name="trackId"/>: the original file when the player supports it
    /// and it fits <see cref="StreamOptions.MaxBitrateKbps"/>, otherwise a transcode.
    /// </summary>
    /// <remarks>The URL contains the access token: never log it.</remarks>
    /// <exception cref="InvalidOperationException">There is no active connection.</exception>
    Uri GetStreamUrl(string trackId, StreamOptions options);
}
