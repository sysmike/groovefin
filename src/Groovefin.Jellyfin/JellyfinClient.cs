using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Jellyfin.Sdk;
using Microsoft.Extensions.Logging;

namespace Groovefin.Jellyfin;

/// <summary>
/// <see cref="IJellyfinClient"/> backed by the official Jellyfin SDK. Holds the active connection and
/// the SDK client for it; the focused services use <see cref="Api"/>.
/// </summary>
internal sealed partial class JellyfinClient(JellyfinApiFactory factory, ILogger<JellyfinClient> logger) : IJellyfinClient, IDisposable
{
    private readonly Lock _gate = new();
    private ActiveConnection? _active;

    public ServerConnection? Connection => _active?.Connection;

    public event EventHandler? ConnectionChanged;

    /// <summary>Gets the SDK client and connection for the active server.</summary>
    /// <exception cref="InvalidOperationException">Not connected.</exception>
    internal (JellyfinApiClient Api, ServerConnection Connection) Active
    {
        get
        {
            var active = _active ?? throw new InvalidOperationException("Not connected to a server.");
            return (active.Api, active.Connection);
        }
    }

    public async Task<ServerInfo> GetServerInfoAsync(Uri serverUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);

        using var api = factory.Create(serverUrl, accessToken: null);
        var info = await JellyfinCall.RunAsync(
            () => api.System.Info.Public.GetAsync(cancellationToken: cancellationToken),
            "Reading server information").ConfigureAwait(false);

        if (string.IsNullOrEmpty(info.Id))
        {
            throw new JellyfinException($"{serverUrl} did not return a Jellyfin server ID.");
        }

        return new ServerInfo(info.Id, info.ServerName ?? serverUrl.Host, info.Version ?? string.Empty, serverUrl);
    }

    public void Connect(ServerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var api = factory.Create(connection.ServerUrl, connection.AccessToken);
        ActiveConnection? previous;
        lock (_gate)
        {
            previous = _active;
            _active = new ActiveConnection(connection, api);
        }

        previous?.Api.Dispose();
        LogConnected(connection.ServerUrl, connection.UserId);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Disconnect()
    {
        ActiveConnection? previous;
        lock (_gate)
        {
            previous = _active;
            _active = null;
        }

        if (previous is null)
        {
            return;
        }

        previous.Api.Dispose();
        LogDisconnected(previous.Connection.ServerUrl);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _active?.Api.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to {ServerUrl} as user {UserId}")]
    private partial void LogConnected(Uri serverUrl, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disconnected from {ServerUrl}")]
    private partial void LogDisconnected(Uri serverUrl);

    private sealed record ActiveConnection(ServerConnection Connection, JellyfinApiClient Api);
}
