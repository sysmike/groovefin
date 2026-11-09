namespace Groovefin.Core.Servers;

/// <summary>A Jellyfin server that answered on the local network (#405).</summary>
/// <param name="Id">The server's ID, as <see cref="ServerProfile.Id"/> has it once the server is added.</param>
/// <param name="Name">The server's name.</param>
/// <param name="Address">The address the server gives for itself, e.g. <c>http://192.168.1.10:8096</c>.</param>
public sealed record DiscoveredServer(string Id, string Name, Uri Address);

/// <summary>Finds the Jellyfin servers on the local network, so the first screen can offer them (#405).</summary>
public interface IServerDiscovery
{
    /// <summary>
    /// Asks the local network for Jellyfin servers and returns each that answered within a few seconds, once.
    /// A network that can't be asked gives an empty list, not an error.
    /// </summary>
    Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(CancellationToken cancellationToken = default);
}
