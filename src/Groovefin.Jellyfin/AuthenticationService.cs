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

        return ToUser(result, serverUrl, userName);
    }

    public async Task<bool> IsQuickConnectEnabledAsync(Uri serverUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);

        using var api = factory.Create(serverUrl, accessToken: null);
        return await JellyfinCall.RunValueAsync(
            () => api.QuickConnect.Enabled.GetAsync(cancellationToken: cancellationToken),
            "Checking Quick Connect").ConfigureAwait(false);
    }

    public async Task<QuickConnectRequest> StartQuickConnectAsync(Uri serverUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);

        using var api = factory.Create(serverUrl, accessToken: null);
        var result = await JellyfinCall.RunAsync(
            () => api.QuickConnect.Initiate.PostAsync(cancellationToken: cancellationToken),
            "Starting Quick Connect").ConfigureAwait(false);

        if (string.IsNullOrEmpty(result.Code) || string.IsNullOrEmpty(result.Secret))
        {
            throw new JellyfinException("Starting Quick Connect: the server returned an incomplete response.");
        }

        return new QuickConnectRequest(result.Code, result.Secret);
    }

    public async Task<bool> IsQuickConnectApprovedAsync(Uri serverUrl, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        using var api = factory.Create(serverUrl, accessToken: null);
        var result = await JellyfinCall.RunAsync(
            () => api.QuickConnect.Connect.GetAsync(request => request.QueryParameters.Secret = secret, cancellationToken),
            "Checking Quick Connect").ConfigureAwait(false);

        return result.Authenticated == true;
    }

    public async Task<AuthenticatedUser> SignInWithQuickConnectAsync(Uri serverUrl, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        using var api = factory.Create(serverUrl, accessToken: null);
        var result = await JellyfinCall.RunAsync(
            () => api.Users.AuthenticateWithQuickConnect.PostAsync(new QuickConnectDto { Secret = secret }, cancellationToken: cancellationToken),
            "Signing in with Quick Connect").ConfigureAwait(false);

        return ToUser(result, serverUrl, fallbackUserName: null);
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

    private AuthenticatedUser ToUser(AuthenticationResult result, Uri serverUrl, string? fallbackUserName)
    {
        var userId = ItemMapper.FormatId(result.User?.Id);
        var userName = result.User?.Name ?? fallbackUserName;
        if (string.IsNullOrEmpty(result.AccessToken) || userId is null || string.IsNullOrEmpty(result.ServerId) || string.IsNullOrEmpty(userName))
        {
            throw new JellyfinException("Signing in: the server returned an incomplete response.");
        }

        var user = new AuthenticatedUser(result.ServerId, userId, userName, result.AccessToken);
        LogSignedIn(user.UserName, serverUrl);
        return user;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Signed in as {UserName} on {ServerUrl}")]
    private partial void LogSignedIn(string userName, Uri serverUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ending the server session failed")]
    private partial void LogSignOutFailed(Exception exception);
}
