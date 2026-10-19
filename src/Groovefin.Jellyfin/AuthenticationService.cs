using Groovefin.Core.Api;
using Groovefin.Jellyfin.Mapping;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging;

namespace Groovefin.Jellyfin;

/// <summary><see cref="IAuthenticationService"/> backed by the Jellyfin SDK.</summary>
internal sealed partial class AuthenticationService(
    JellyfinApiFactory factory,
    JellyfinClient client,
    ILogger<AuthenticationService> logger) : IAuthenticationService
{
    public async Task<AuthenticatedUser> SignInAsync(Uri serverUrl, string userName, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(password);

        using var api = factory.Create(serverUrl, accessToken: null);
        var result = await JellyfinCall.RunAsync(
            () => api.Users.AuthenticateByName.PostAsync(
                new AuthenticateUserByName { Username = userName.Trim(), Pw = password },
                cancellationToken: cancellationToken),
            "Signing in").ConfigureAwait(false);

        var userId = ItemMapper.FormatId(result.User?.Id);
        if (string.IsNullOrEmpty(result.AccessToken) || userId is null || string.IsNullOrEmpty(result.ServerId))
        {
            throw new JellyfinException("Signing in: the server returned an incomplete response.");
        }

        var user = new AuthenticatedUser(result.ServerId, userId, result.User?.Name ?? userName, result.AccessToken);
        LogSignedIn(user.UserName, serverUrl);
        return user;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (client.Connection is null)
        {
            return;
        }

        try
        {
            var (api, _) = client.Active;
            await api.Sessions.Logout.PostAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The token is removed locally in any case; an expired session or offline server is fine.
            LogSignOutFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Signed in as {UserName} on {ServerUrl}")]
    private partial void LogSignedIn(string userName, Uri serverUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ending the server session failed")]
    private partial void LogSignOutFailed(Exception exception);
}
