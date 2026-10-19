namespace Groovefin.Core.Api;

/// <summary>The result of a successful sign-in.</summary>
/// <remarks>Contains the access token: never log it.</remarks>
public sealed record AuthenticatedUser(string ServerId, string UserId, string UserName, string AccessToken)
{
    public override string ToString() => $"{UserName} ({UserId}) on {ServerId}";
}

/// <summary>A Quick Connect request: the user approves <see cref="Code"/> in another signed-in client.</summary>
/// <remarks><see cref="Secret"/> identifies the request when signing in: never log it.</remarks>
public sealed record QuickConnectRequest(string Code, string Secret)
{
    public override string ToString() => Code;
}

/// <summary>Signs in and out of a Jellyfin server.</summary>
public interface IAuthenticationService
{
    /// <summary>Signs in with user name and password (<c>/Users/AuthenticateByName</c>). The password is not stored.</summary>
    /// <exception cref="JellyfinException">The credentials were rejected (<see cref="JellyfinException.IsUnauthorized"/>) or the request failed.</exception>
    Task<AuthenticatedUser> SignInAsync(Uri serverUrl, string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>Gets a value indicating whether Quick Connect is enabled on the server.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<bool> IsQuickConnectEnabledAsync(Uri serverUrl, CancellationToken cancellationToken = default);

    /// <summary>Starts a Quick Connect request and gets the code to show to the user.</summary>
    /// <exception cref="JellyfinException">Quick Connect is disabled (<see cref="JellyfinException.IsUnauthorized"/>) or the request failed.</exception>
    Task<QuickConnectRequest> StartQuickConnectAsync(Uri serverUrl, CancellationToken cancellationToken = default);

    /// <summary>Gets a value indicating whether the user approved the request in another client.</summary>
    /// <exception cref="JellyfinException">The request is unknown or expired (HTTP 404) or the request failed.</exception>
    Task<bool> IsQuickConnectApprovedAsync(Uri serverUrl, string secret, CancellationToken cancellationToken = default);

    /// <summary>Signs in with an approved Quick Connect request.</summary>
    /// <exception cref="JellyfinException">The request is not approved or the request failed.</exception>
    Task<AuthenticatedUser> SignInWithQuickConnectAsync(Uri serverUrl, string secret, CancellationToken cancellationToken = default);

    /// <summary>Ends the server session of the active connection. Failures are ignored: the token is discarded locally anyway.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
