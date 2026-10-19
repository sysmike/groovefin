namespace Groovefin.Core.Api;

/// <summary>The result of a successful sign-in.</summary>
/// <remarks>Contains the access token: never log it.</remarks>
public sealed record AuthenticatedUser(string ServerId, string UserId, string UserName, string AccessToken)
{
    public override string ToString() => $"{UserName} ({UserId}) on {ServerId}";
}

/// <summary>Signs in and out of a Jellyfin server.</summary>
public interface IAuthenticationService
{
    /// <summary>Signs in with user name and password (<c>/Users/AuthenticateByName</c>). The password is not stored.</summary>
    /// <exception cref="JellyfinException">The credentials were rejected (<see cref="JellyfinException.IsUnauthorized"/>) or the request failed.</exception>
    Task<AuthenticatedUser> SignInAsync(Uri serverUrl, string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>Ends the server session of the active connection. Failures are ignored: the token is discarded locally anyway.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
