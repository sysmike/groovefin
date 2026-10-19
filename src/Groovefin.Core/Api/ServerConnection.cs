namespace Groovefin.Core.Api;

/// <summary>An authenticated connection to a server: where it is, who is logged in, and the access token.</summary>
/// <remarks>The token is a secret: never log it or store it outside the credential store.</remarks>
public sealed record ServerConnection(Uri ServerUrl, string UserId, string AccessToken)
{
    /// <summary>
    /// Gets the music library that browsing is limited to, or <see langword="null"/> for all music
    /// libraries.
    /// </summary>
    public string? LibraryId { get; init; }

    public override string ToString() => $"{ServerUrl} (user {UserId})";
}
