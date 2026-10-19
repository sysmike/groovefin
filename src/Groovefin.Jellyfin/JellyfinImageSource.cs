using Groovefin.Core.Api;
using Groovefin.Core.Images;

namespace Groovefin.Jellyfin;

/// <summary>
/// <see cref="IImageSource"/> for the active connection. Images are resized by the server and fetched
/// through the resilient HTTP client.
/// </summary>
internal sealed class JellyfinImageSource(JellyfinClient client, IHttpClientFactory httpClientFactory, ClientIdentity identity) : IImageSource
{
    public string? ServerKey => client.Connection?.ServerUrl.AbsoluteUri.TrimEnd('/').ToLowerInvariant();

    public async Task<Stream> OpenAsync(ImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = client.Connection ?? throw new JellyfinException("Not connected to a server.");
        var http = httpClientFactory.CreateClient(JellyfinApiFactory.HttpClientName);

        return await JellyfinCall.RunAsync<Stream>(
            async () =>
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, ImageUrl.Build(connection.ServerUrl, request));
                message.Headers.TryAddWithoutValidation(AuthorizationHeader.Name, AuthorizationHeader.Build(identity, connection.AccessToken));

                using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new JellyfinException($"Loading image failed with HTTP {(int)response.StatusCode}.", response.StatusCode);
                }

                // Images are small; buffering keeps the response inside the resilience pipeline's timeouts.
                return new MemoryStream(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false), writable: false);
            },
            "Loading image").ConfigureAwait(false);
    }
}
