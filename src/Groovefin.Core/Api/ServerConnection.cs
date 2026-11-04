using System.Security.Cryptography;
using System.Text;

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

    /// <summary>
    /// Gets a file name for what Groovefin keeps of this user on this server: the play history (#251) and the
    /// downloads (#327). The user ID and a hash of the server's address, so it reveals neither.
    /// </summary>
    public string UserDataKey
    {
        get
        {
            var server = SHA256.HashData(Encoding.UTF8.GetBytes(ServerUrl.AbsoluteUri.ToUpperInvariant()));
            return $"{UserId}-{Convert.ToHexStringLower(server, 0, 8)}";
        }
    }

    public override string ToString() => $"{ServerUrl} (user {UserId})";
}
