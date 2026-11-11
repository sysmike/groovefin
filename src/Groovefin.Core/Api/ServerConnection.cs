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
    /// Gets the server's own ID (<c>ServerProfile.Id</c>), which stays when its address changes; or
    /// <see langword="null"/> for a connection made without a profile.
    /// </summary>
    public string? ServerId { get; init; }

    /// <summary>
    /// Gets a file name for what Groovefin keeps of this user on this server: the play history (#251) and the
    /// downloads (#327). The user ID and a hash of the server's ID, so it reveals neither and stays the same when
    /// the server gets another address (#429). Without a <see cref="ServerId"/> the hash is of the address.
    /// </summary>
    public string UserDataKey => $"{UserId}-{(ServerId is { Length: > 0 } id ? ServerKeyOf(id) : ServerKeyOf(ServerUrl))}";

    /// <summary>
    /// Gets the <see cref="UserDataKey"/> of versions before #429, which named the server by its address; or
    /// <see langword="null"/> when it is the same. What is kept under it moves to <see cref="UserDataKey"/>.
    /// </summary>
    public string? LegacyUserDataKey => ServerId is { Length: > 0 } ? $"{UserId}-{ServerKeyOf(ServerUrl)}" : null;

    /// <summary>Gets the part of a <see cref="LegacyUserDataKey"/> that names the server: a hash of its address.</summary>
    public static string ServerKeyOf(Uri serverUrl)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serverUrl.AbsoluteUri.ToUpperInvariant()));
        return Convert.ToHexStringLower(hash, 0, 8);
    }

    /// <summary>Gets the part of <see cref="UserDataKey"/> that names the server: a hash of its ID.</summary>
    public static string ServerKeyOf(string serverId)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("id:" + serverId.ToUpperInvariant()));
        return Convert.ToHexStringLower(hash, 0, 8);
    }

    public override string ToString() => $"{ServerUrl} (user {UserId})";
}
