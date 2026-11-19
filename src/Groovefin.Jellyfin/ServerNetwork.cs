using Groovefin.Core.Api;

namespace Groovefin.Jellyfin;

/// <summary>
/// <see cref="IServerNetwork"/> on Jellyfin's <c>/System/Endpoint</c> (#453): the server tells from the address it
/// sees whether the client is on the same PC (<c>IsLocal</c>) or in one of its LAN networks (<c>IsInNetwork</c>).
/// </summary>
internal sealed class ServerNetwork(JellyfinClient client) : IServerNetwork
{
    public async Task<bool?> IsInServerNetworkAsync(CancellationToken cancellationToken = default)
    {
        var (api, _) = client.Active;
        var info = await JellyfinCall.RunAsync(
            () => api.System.Endpoint.GetAsync(cancellationToken: cancellationToken),
            "Checking the network").ConfigureAwait(false);
        return info.IsLocal == true ? true : info.IsInNetwork;
    }
}
