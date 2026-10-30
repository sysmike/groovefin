namespace Groovefin.Core.Servers;

/// <summary>
/// A server the user has added. Identified by the server's own ID, so changing its address keeps
/// the profile (and the stored login).
/// </summary>
public sealed record ServerProfile
{
    /// <summary>Gets the server ID reported by Jellyfin.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Gets the normalized address, e.g. <c>https://media.example.com/jellyfin</c>.</summary>
    public required Uri Address { get; init; }

    public string? Version { get; init; }

    /// <summary>Gets the signed-in user's ID, or <see langword="null"/> when nobody is signed in.</summary>
    public string? UserId { get; init; }

    public string? UserName { get; init; }

    /// <summary>
    /// Gets the music library browsing is limited to on this server, or <see langword="null"/> for all
    /// music libraries (the default).
    /// </summary>
    public string? LibraryId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user chose to sign in although <see cref="Address"/> is plain HTTP
    /// (<see cref="ServerAddress.IsUnencrypted"/>, #293). The choice holds for this address only: a new
    /// address asks again.
    /// </summary>
    public bool UnencryptedAccepted { get; init; }
}
