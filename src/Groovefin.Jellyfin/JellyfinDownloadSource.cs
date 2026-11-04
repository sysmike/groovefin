using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Groovefin.Core.Api;
using Groovefin.Core.Downloads;
using Groovefin.Core.Settings;

namespace Groovefin.Jellyfin;

/// <summary>
/// <see cref="IDownloadSource"/> for the active connection (#327). The original is the stored file
/// (<c>/Audio/{id}/stream?static=true</c>, which needs only the right to play); a server that refuses it gets
/// <c>/Items/{id}/Download</c>. AAC comes from the universal endpoint, which keeps an AAC file within the
/// bitrate as it is and converts everything else. The token goes in the header, not the URL.
/// </summary>
/// <remarks>
/// Downloads use their own HTTP client: a song can take longer than the resilience pipeline's 45 seconds,
/// and <see cref="DownloadManager"/> retries failed songs itself.
/// </remarks>
internal sealed class JellyfinDownloadSource(JellyfinClient client, IHttpClientFactory httpClientFactory, ClientIdentity identity) : IDownloadSource
{
    public const string HttpClientName = "Groovefin.Jellyfin.Downloads";

    /// <summary>File name extensions by content type, for servers that send no file name.</summary>
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/aac"] = ".aac",
        ["audio/aacp"] = ".aac",
        ["audio/flac"] = ".flac",
        ["audio/x-flac"] = ".flac",
        ["audio/mpeg"] = ".mp3",
        ["audio/mp3"] = ".mp3",
        ["audio/mp4"] = ".m4a",
        ["audio/x-m4a"] = ".m4a",
        ["audio/m4a"] = ".m4a",
        ["audio/ogg"] = ".ogg",
        ["audio/opus"] = ".opus",
        ["audio/wav"] = ".wav",
        ["audio/wave"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["audio/webm"] = ".webm",
        ["audio/x-ms-wma"] = ".wma",
        ["audio/aiff"] = ".aiff",
        ["audio/x-aiff"] = ".aiff",
        ["audio/x-ape"] = ".ape",
        ["audio/x-wavpack"] = ".wv",
    };

    public async Task<DownloadResponse> OpenAsync(string trackId, DownloadFormat format, long offset, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackId);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (!Guid.TryParse(trackId, out var id))
        {
            throw new ArgumentException($"'{trackId}' is not a Jellyfin item ID.", nameof(trackId));
        }

        var connection = client.Connection ?? throw new JellyfinException("Not connected to a server.");
        var http = httpClientFactory.CreateClient(HttpClientName);

        if (format.Kind == DownloadFormatKind.Aac)
        {
            return await OpenAsync(http, connection, AacUrl(connection, id, format.AacBitrateKbps), 0, ".aac", cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await OpenAsync(http, connection, Url(connection, $"/Audio/{id:N}/stream", [("static", "true")]), offset, null, cancellationToken).ConfigureAwait(false);
        }
        catch (JellyfinException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            return await OpenAsync(http, connection, Url(connection, $"/Items/{id:N}/Download", []), offset, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<DownloadResponse> OpenAsync(HttpClient http, ServerConnection connection, Uri url, long offset, string? extension, CancellationToken cancellationToken) =>
        JellyfinCall.RunAsync<DownloadResponse>(
            async () =>
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                message.Headers.TryAddWithoutValidation(AuthorizationHeader.Name, AuthorizationHeader.Build(identity, connection.AccessToken));
                if (offset > 0)
                {
                    message.Headers.Range = new RangeHeaderValue(offset, null);
                }

                var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new JellyfinException($"Downloading a song failed with HTTP {(int)response.StatusCode}.", response.StatusCode);
                    }

                    var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    return new DownloadResponse(
                        content,
                        response.Content.Headers.ContentLength,
                        extension ?? ExtensionOf(response.Content.Headers),
                        response.StatusCode == HttpStatusCode.PartialContent,
                        response);
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            },
            "Downloading a song");

    /// <summary>The file name's extension the server sent, else the content type's, else <c>.audio</c>.</summary>
    internal static string ExtensionOf(HttpContentHeaders headers)
    {
        var name = headers.ContentDisposition?.FileNameStar ?? headers.ContentDisposition?.FileName;
        var fromName = Path.GetExtension(name?.Trim('"') ?? string.Empty);
        if (fromName.Length is > 1 and <= 6 && fromName.Skip(1).All(char.IsAsciiLetterOrDigit))
        {
            return fromName.ToLowerInvariant();
        }

        return headers.ContentType?.MediaType is { } type && Extensions.TryGetValue(type, out var extension) ? extension : ".audio";
    }

    private Uri AacUrl(ServerConnection connection, Guid id, int kbps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kbps);
        return Url(connection, $"/Audio/{id:N}/universal",
        [
            ("UserId", connection.UserId),
            ("DeviceId", identity.DeviceId),
            ("Container", "aac,m4a|aac,m4b|aac"),
            ("TranscodingContainer", "aac"),
            ("TranscodingProtocol", "http"),
            ("AudioCodec", "aac"),
            ("MaxStreamingBitrate", (kbps * 1000L).ToString(CultureInfo.InvariantCulture)),

            // A redirect would drop the Authorization header.
            ("EnableRedirection", "false"),
            ("EnableRemoteMedia", "false"),
        ]);
    }

    /// <summary>A URL on the server that keeps a base path such as <c>https://host/jellyfin</c>.</summary>
    private static Uri Url(ServerConnection connection, string path, IReadOnlyList<(string Name, string Value)> query) =>
        new UriBuilder(connection.ServerUrl)
        {
            Path = connection.ServerUrl.AbsolutePath.TrimEnd('/') + path,
            Query = string.Join('&', query.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value)}")),
        }.Uri;
}
