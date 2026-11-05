using Groovefin.Core.Queue;

namespace Groovefin.Core.Session;

/// <summary>
/// What is saved to resume playback on the next start: the queue and the position, per server.
/// The properties with a default have setters: for one a file does not have, the source-generated JSON reader
/// then keeps the default, where it sets an init-only property to <see langword="null"/> or 0 (#353, #355).
/// </summary>
public sealed record SavedSession
{
    /// <summary>The format version this build writes; files with another version are ignored.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Gets the server the queue's tracks belong to (<c>ServerProfile.Id</c>).</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Gets the user the queue belongs to, so another account on the same server starts empty.</summary>
    public string UserId { get; set; } = string.Empty;

    public QueueSnapshot Queue { get; set; } = new();

    /// <summary>Gets the position in the current track.</summary>
    public TimeSpan Position { get; init; }
}

/// <summary>Location of the session file (next to <c>settings.json</c>).</summary>
/// <param name="FilePath">Absolute path of <c>session.json</c>.</param>
public sealed record SessionStoreOptions(string FilePath);

/// <summary>Reads and writes the <see cref="SavedSession"/>. Failures are logged, never thrown.</summary>
public interface ISessionStore
{
    /// <summary>Gets the saved session, or <see langword="null"/> when there is none or it cannot be read.</summary>
    Task<SavedSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SavedSession session, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
