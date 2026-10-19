using Groovefin.Core.Models;

namespace Groovefin.Core.Api;

/// <summary>
/// Entry point to a Jellyfin server: server discovery and the active connection. The focused services
/// (<see cref="ILibraryService"/> and those added by later features) use the active connection.
/// </summary>
public interface IJellyfinClient
{
    /// <summary>Gets the active connection, or <see langword="null"/> when not connected.</summary>
    ServerConnection? Connection { get; }

    /// <summary>Raised after <see cref="Connection"/> has changed.</summary>
    event EventHandler? ConnectionChanged;

    /// <summary>Reads the public server information. Does not need a connection or credentials.</summary>
    /// <exception cref="JellyfinException">The server cannot be reached or is not a Jellyfin server.</exception>
    Task<ServerInfo> GetServerInfoAsync(Uri serverUrl, CancellationToken cancellationToken = default);

    /// <summary>Makes <paramref name="connection"/> the active connection for all services.</summary>
    void Connect(ServerConnection connection);

    /// <summary>Clears the active connection.</summary>
    void Disconnect();
}
