using Groovefin.Core.Api;
using Jellyfin.Sdk;

namespace Groovefin.Jellyfin;

/// <summary>Creates SDK clients for a server URL and optional access token.</summary>
internal sealed class JellyfinApiFactory(IHttpClientFactory httpClientFactory, ClientIdentity identity)
{
    public const string HttpClientName = "Groovefin.Jellyfin";

    public JellyfinApiClient Create(Uri serverUrl, string? accessToken)
    {
        var settings = new JellyfinSdkSettings();
        settings.Initialize(identity.ClientName, identity.ClientVersion, identity.DeviceName, identity.DeviceId);
        settings.SetServerUrl(serverUrl.ToString().TrimEnd('/'));

        var adapter = new JellyfinRequestAdapter(
            new GroovefinAuthenticationProvider(identity, accessToken),
            settings,
            httpClientFactory.CreateClient(HttpClientName));
        return new JellyfinApiClient(adapter);
    }
}
