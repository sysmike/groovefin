namespace Groovefin.Core.Discord;

/// <summary>
/// What Discord shows in the user's status while Groovefin plays: "Listening to Groovefin", the title
/// (<see cref="Details"/>), the artists (<see cref="State"/>), the album when hovering the icon
/// (<see cref="LargeText"/>) and, while playing, the progress (<see cref="Start"/> to <see cref="End"/>).
/// </summary>
public sealed record DiscordActivity(string Details, string? State, string? LargeText, DateTimeOffset? Start, DateTimeOffset? End);

/// <summary>
/// The Groovefin application in the Discord Developer Portal. Its ID is public (it names the app in
/// "Listening to …"), not a secret; Rich Presence needs no key or token.
/// </summary>
public sealed record DiscordApplication(string Id)
{
    public static DiscordApplication Groovefin { get; } = new("1563195751360757931");

    /// <summary>Gets a value indicating whether this build has an application, and so can show a presence.</summary>
    public bool IsSet => Id.Length > 0;
}
