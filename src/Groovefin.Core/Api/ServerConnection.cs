namespace Groovefin.Core.Api;

/// <summary>An authenticated connection to a server: where it is, who is logged in, and the access token.</summary>
/// <remarks>The token is a secret: never log it or store it outside the credential store.</remarks>
public sealed record ServerConnection(Uri ServerUrl, string UserId, string AccessToken)
{
    public override string ToString() => $"{ServerUrl} (user {UserId})";
}
