using System.Runtime.InteropServices;
using Groovefin.Core.Security;
using Microsoft.Extensions.Logging;
using Windows.Security.Credentials;

namespace Groovefin.App.Services;

/// <summary>
/// <see cref="ICredentialStore"/> backed by <see cref="PasswordVault"/> (Windows Credential Manager).
/// Resource: <c>Groovefin/jellyfin/&lt;serverId&gt;</c>, user name: the Jellyfin user ID, password: the token.
/// </summary>
internal sealed partial class PasswordVaultCredentialStore(ILogger<PasswordVaultCredentialStore> logger) : ICredentialStore
{
    // HRESULT_FROM_WIN32(ERROR_NOT_FOUND): the vault has no matching credential.
    private const int ElementNotFound = unchecked((int)0x80070490);

    private readonly PasswordVault _vault = new();

    public string? GetAccessToken(string serverId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            var credential = _vault.Retrieve(CredentialResource.ForServer(serverId), userId);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (COMException ex) when (ex.HResult == ElementNotFound)
        {
            return null;
        }
    }

    public void SaveAccessToken(string serverId, string userId, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        // Adding a credential with an existing resource + user name replaces it.
        _vault.Add(new PasswordCredential(CredentialResource.ForServer(serverId), userId, accessToken));
        LogSaved(serverId, userId);
    }

    public void RemoveAccessToken(string serverId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            _vault.Remove(_vault.Retrieve(CredentialResource.ForServer(serverId), userId));
            LogRemoved(serverId, userId);
        }
        catch (COMException ex) when (ex.HResult == ElementNotFound)
        {
            // Nothing stored.
        }
    }

    public void RemoveServer(string serverId)
    {
        IReadOnlyList<PasswordCredential> credentials;
        try
        {
            credentials = _vault.FindAllByResource(CredentialResource.ForServer(serverId));
        }
        catch (COMException ex) when (ex.HResult == ElementNotFound)
        {
            return;
        }

        foreach (var credential in credentials)
        {
            _vault.Remove(credential);
        }

        LogServerRemoved(serverId, credentials.Count);
    }

    // Never log tokens; server and user IDs are not secret.
    [LoggerMessage(Level = LogLevel.Information, Message = "Saved access token for user {UserId} on server {ServerId}")]
    private partial void LogSaved(string serverId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed access token for user {UserId} on server {ServerId}")]
    private partial void LogRemoved(string serverId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} access token(s) for server {ServerId}")]
    private partial void LogServerRemoved(string serverId, int count);
}
