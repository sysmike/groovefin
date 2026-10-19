using Groovefin.Core.Api;
using Groovefin.Core.Security;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Servers;

/// <summary><see cref="IServerManager"/> on top of settings, the Jellyfin client and the credential store.</summary>
public sealed partial class ServerManager(
    ISettingsService settings,
    IJellyfinClient client,
    IAuthenticationService authentication,
    ICredentialStore credentials,
    ILogger<ServerManager> logger) : IServerManager
{
    public IReadOnlyList<ServerProfile> Servers => settings.Current.Servers.Known;

    public ServerProfile? ActiveServer =>
        settings.Current.Servers.ActiveServerId is { } id ? Find(id) : null;

    public bool IsSignedIn => client.Connection is not null;

    public event EventHandler? Changed;

    public async Task<ServerProfile> AddAsync(string address, CancellationToken cancellationToken = default)
    {
        var uri = Normalize(address);
        var info = await client.GetServerInfoAsync(uri, cancellationToken).ConfigureAwait(false);

        var existing = Find(info.Id);
        var profile = existing is null
            ? new ServerProfile { Id = info.Id, Name = info.Name, Address = uri, Version = info.Version }
            : existing with { Name = info.Name, Address = uri, Version = info.Version };

        settings.Update(s =>
        {
            var known = s.Servers.Known.Where(p => p.Id != profile.Id).Append(profile).ToList();
            return s with
            {
                Servers = s.Servers with
                {
                    Known = known,
                    ActiveServerId = s.Servers.ActiveServerId ?? profile.Id,
                },
            };
        });

        LogAdded(profile.Name, profile.Address);
        Changed?.Invoke(this, EventArgs.Empty);
        return profile;
    }

    public async Task<ServerProfile> UpdateAddressAsync(string serverId, string address, CancellationToken cancellationToken = default)
    {
        var current = Find(serverId) ?? throw new InvalidOperationException($"Unknown server {serverId}.");
        var uri = Normalize(address);
        var info = await client.GetServerInfoAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(info.Id, current.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{uri} is a different server ({info.Name}).");
        }

        var updated = current with { Address = uri, Name = info.Name, Version = info.Version };
        Replace(updated);

        if (ActiveServer?.Id == updated.Id)
        {
            Connect(updated);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    public void Remove(string serverId)
    {
        var profile = Find(serverId);
        if (profile is null)
        {
            return;
        }

        credentials.RemoveServer(profile.Id);
        var wasActive = ActiveServer?.Id == profile.Id;

        settings.Update(s =>
        {
            var known = s.Servers.Known.Where(p => p.Id != profile.Id).ToList();
            var active = wasActive ? known.FirstOrDefault()?.Id : s.Servers.ActiveServerId;
            return s with { Servers = s.Servers with { Known = known, ActiveServerId = active } };
        });

        LogRemoved(profile.Name);
        if (wasActive)
        {
            Restore();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetActive(string serverId)
    {
        var profile = Find(serverId) ?? throw new InvalidOperationException($"Unknown server {serverId}.");
        settings.Update(s => s with { Servers = s.Servers with { ActiveServerId = profile.Id } });
        Connect(profile);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Restore()
    {
        if (ActiveServer is { } active)
        {
            Connect(active);
        }
        else
        {
            client.Disconnect();
        }
    }

    public async Task SignInAsync(string serverId, string userName, string password, CancellationToken cancellationToken = default)
    {
        var profile = Find(serverId) ?? throw new InvalidOperationException($"Unknown server {serverId}.");
        var user = await authentication.SignInAsync(profile.Address, userName, password, cancellationToken).ConfigureAwait(false);

        // Replace the previous user's token on this server, if any.
        if (profile.UserId is { } previous && previous != user.UserId)
        {
            credentials.RemoveAccessToken(profile.Id, previous);
        }

        credentials.SaveAccessToken(profile.Id, user.UserId, user.AccessToken);
        var updated = profile with { UserId = user.UserId, UserName = user.UserName };
        Replace(updated);
        settings.Update(s => s with { Servers = s.Servers with { ActiveServerId = updated.Id } });
        client.Connect(new ServerConnection(updated.Address, user.UserId, user.AccessToken));

        LogSignedIn(updated.Name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveServer is not { UserId: { } userId } profile)
        {
            return;
        }

        await authentication.SignOutAsync(cancellationToken).ConfigureAwait(false);
        credentials.RemoveAccessToken(profile.Id, userId);
        Replace(profile with { UserId = null, UserName = null });
        client.Disconnect();

        LogSignedOut(profile.Name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Connect(ServerProfile profile)
    {
        var token = profile.UserId is { } userId ? credentials.GetAccessToken(profile.Id, userId) : null;
        if (token is null)
        {
            // Not signed in (or the token was removed): the shell shows the sign-in page.
            client.Disconnect();
            return;
        }

        client.Connect(new ServerConnection(profile.Address, profile.UserId!, token));
    }

    private ServerProfile? Find(string serverId) =>
        Servers.FirstOrDefault(p => string.Equals(p.Id, serverId, StringComparison.OrdinalIgnoreCase));

    private void Replace(ServerProfile profile) =>
        settings.Update(s => s with
        {
            Servers = s.Servers with { Known = s.Servers.Known.Select(p => p.Id == profile.Id ? profile : p).ToList() },
        });

    private static Uri Normalize(string address) =>
        ServerAddress.TryNormalize(address, out var uri)
            ? uri
            : throw new ArgumentException("Enter an address such as http://192.168.1.10:8096.", nameof(address));

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Name} added at {Address}")]
    private partial void LogAdded(string name, Uri address);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Name} removed")]
    private partial void LogRemoved(string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Signed in to {Name}")]
    private partial void LogSignedIn(string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Signed out of {Name}")]
    private partial void LogSignedOut(string name);
}
