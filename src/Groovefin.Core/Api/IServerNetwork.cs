namespace Groovefin.Core.Api;

/// <summary>The signed-in server's view of where this PC connects from (#453).</summary>
public interface IServerNetwork
{
    /// <summary>
    /// Asks the server whether this PC is in its local network, as its network settings define it (the LAN
    /// networks, and the known proxies a reverse proxy needs). <see langword="null"/> when the server doesn't say.
    /// </summary>
    /// <exception cref="JellyfinException">The server could not be asked.</exception>
    /// <exception cref="InvalidOperationException">Not connected to a server.</exception>
    Task<bool?> IsInServerNetworkAsync(CancellationToken cancellationToken = default);
}
