namespace Groovefin.Core.Security;

/// <summary>
/// Stores access tokens in the operating system's credential vault. Passwords are never stored.
/// One credential exists per server and user.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Gets the access token for <paramref name="userId"/> on <paramref name="serverId"/>, or <see langword="null"/>.</summary>
    string? GetAccessToken(string serverId, string userId);

    /// <summary>Saves or replaces the access token for <paramref name="userId"/> on <paramref name="serverId"/>.</summary>
    void SaveAccessToken(string serverId, string userId, string accessToken);

    /// <summary>Removes the token of one user (logout). Does nothing when none is stored.</summary>
    void RemoveAccessToken(string serverId, string userId);

    /// <summary>Removes the tokens of all users of a server (server removed). Does nothing when none are stored.</summary>
    void RemoveServer(string serverId);
}
