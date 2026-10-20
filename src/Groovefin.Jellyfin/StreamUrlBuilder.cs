using System.Globalization;
using Groovefin.Core.Api;

namespace Groovefin.Jellyfin;

/// <summary>
/// <see cref="IStreamUrlProvider"/> for Jellyfin's universal audio endpoint (<c>/Audio/{id}/universal</c>).
/// The server plays the original file when its container and codec are in <see cref="DirectPlayContainers"/>
/// and it fits the bitrate limit; otherwise it transcodes to the chosen codec over plain HTTP.
/// </summary>
/// <remarks>
/// <c>MediaPlayer</c> cannot send headers, so the access token goes into the URL as <c>ApiKey</c>. Not the
/// older <c>api_key</c>: servers with legacy authorization off (the default from Jellyfin 10.12) ignore it.
/// </remarks>
internal sealed class StreamUrlBuilder(JellyfinClient client, ClientIdentity identity) : IStreamUrlProvider
{
    /// <summary>Containers (with codec where the container allows several) that Windows plays natively.</summary>
    public const string DirectPlayContainers = "mp3,aac,m4a|aac,m4b|aac,m4a|alac,flac,wav,wma";

    public Uri GetStreamUrl(string trackId, StreamOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackId);
        ArgumentNullException.ThrowIfNull(options);
        if (!Guid.TryParse(trackId, out var id))
        {
            throw new ArgumentException($"'{trackId}' is not a Jellyfin item ID.", nameof(trackId));
        }

        var connection = client.Connection ?? throw new InvalidOperationException("Not connected to a server.");
        var codec = options.Codec switch
        {
            TranscodingCodec.Aac => "aac",
            TranscodingCodec.Mp3 => "mp3",
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Codec, null),
        };

        var query = new List<(string Name, string Value)>
        {
            ("UserId", connection.UserId),
            ("DeviceId", identity.DeviceId),
            ("Container", DirectPlayContainers),
            ("TranscodingContainer", codec),
            ("TranscodingProtocol", "http"),
            ("AudioCodec", codec),
            ("EnableRedirection", "true"),
            ("EnableRemoteMedia", "false"),
        };

        if (options.MaxBitrateKbps is { } kbps)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kbps, nameof(options));
            query.Add(("MaxStreamingBitrate", (kbps * 1000L).ToString(CultureInfo.InvariantCulture)));
        }

        if (options.StartPosition > TimeSpan.Zero)
        {
            query.Add(("StartTimeTicks", options.StartPosition.Ticks.ToString(CultureInfo.InvariantCulture)));
        }

        query.Add(("ApiKey", connection.AccessToken));

        // Keep a base path such as https://host/jellyfin.
        var builder = new UriBuilder(connection.ServerUrl)
        {
            Path = connection.ServerUrl.AbsolutePath.TrimEnd('/') + $"/Audio/{id:N}/universal",
            Query = string.Join('&', query.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value)}")),
        };
        return builder.Uri;
    }
}
