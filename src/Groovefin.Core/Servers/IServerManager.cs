namespace Groovefin.Core.Servers;

/// <summary>
/// The servers the user has added and which one is active. Persists through settings and keeps the
/// Jellyfin connection in sync with the active server.
/// </summary>
public interface IServerManager
{
    IReadOnlyList<ServerProfile> Servers { get; }

    ServerProfile? ActiveServer { get; }

    /// <summary>Gets a value indicating whether the active server has a signed-in user with a stored token.</summary>
    bool IsSignedIn { get; }

    /// <summary>Raised after <see cref="Servers"/>, <see cref="ActiveServer"/> or <see cref="IsSignedIn"/> changed.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Validates <paramref name="address"/> against the server's public info and adds the server, or
    /// updates name and address when a server with the same ID exists. The first server becomes active.
    /// </summary>
    /// <exception cref="ArgumentException">The address is not a valid http(s) address.</exception>
    /// <exception cref="Api.JellyfinException">No Jellyfin server answered at the address.</exception>
    Task<ServerProfile> AddAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Changes the address of a server. The new address must point to the same server ID.</summary>
    /// <exception cref="InvalidOperationException">The address belongs to a different server.</exception>
    Task<ServerProfile> UpdateAddressAsync(string serverId, string address, CancellationToken cancellationToken = default);

    /// <summary>Removes a server and its stored tokens. Another server becomes active if this one was.</summary>
    void Remove(string serverId);

    /// <summary>Makes a server active and connects when a token is stored for its user.</summary>
    void SetActive(string serverId);

    /// <summary>Connects to the active server if possible. Called once at startup.</summary>
    void Restore();
}
