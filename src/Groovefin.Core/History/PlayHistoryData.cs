using System.Text.Json.Serialization;
using Groovefin.Core.Models;

namespace Groovefin.Core.History;

/// <summary>Where a play in the history was seen (#251).</summary>
public enum PlaySource
{
    /// <summary>Listened to in Groovefin, for half the track or 4 minutes.</summary>
    Groovefin,

    /// <summary>A play count the server gained without Groovefin: another device.</summary>
    Server,

    /// <summary>
    /// A play the first sync found (#382): a song's whole play count, all timed at its last play. On repeat counts
    /// these; the statistics of a period leave them out, as they did not happen then.
    /// </summary>
    Seeded,
}

/// <summary>One play of a track.</summary>
public sealed record PlayRecord(string TrackId, DateTimeOffset PlayedAt, PlaySource Source);

/// <summary>
/// What the statistics group a played song by (#382): its album, its artists and its length. Kept with the history,
/// so a year of statistics needs no request per song, and a song gone from the server still counts.
/// </summary>
public sealed record PlayedTrack(string? AlbumId, IReadOnlyList<string> ArtistIds, TimeSpan Duration)
{
    /// <summary>Gets what the statistics need of <paramref name="track"/>.</summary>
    public static PlayedTrack Of(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new PlayedTrack(track.AlbumId, [.. track.Artists.Select(a => a.Id).Distinct(StringComparer.Ordinal)], track.Duration ?? TimeSpan.Zero);
    }
}

/// <summary>A track's play count on the server at the last sync, and when it was last played then.</summary>
public sealed record PlayCountRecord(int PlayCount, DateTimeOffset LastPlayed);

/// <summary>
/// A copy of a user's plays for the statistics (#382): when the history began, the plays, and what is known of
/// their songs by track ID (not every played song: plays from before #382 may lack theirs).
/// </summary>
public sealed record PlayLog(DateTimeOffset Started, IReadOnlyList<PlayRecord> Plays, IReadOnlyDictionary<string, PlayedTrack> Tracks);

/// <summary>A start Groovefin reported to the server, which counted it as a play.</summary>
public sealed record ReportedStart(string TrackId, DateTimeOffset StartedAt);

/// <summary>A play the server could not be told of (offline): sent once it can be reached (#339).</summary>
public sealed record PendingPlay(string TrackId, DateTimeOffset PlayedAt);

/// <summary>
/// The play history of one user on one server, as saved (#251). The properties with a default have setters: for
/// one a file does not have (one from before it was added), the source-generated reader then keeps the default,
/// where it sets an init-only property to <see langword="null"/> or 0 (#353).
/// </summary>
public sealed record PlayHistoryData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Gets or sets when the play counts were last read from the server, or <see langword="null"/> before the first time.</summary>
    public DateTimeOffset? LastSync { get; set; }

    public List<PlayRecord> Plays { get; set; } = [];

    /// <summary>Gets the play counts at the last sync, by track ID.</summary>
    public Dictionary<string, PlayCountRecord> Counts { get; set; } = [];

    /// <summary>Gets the starts reported since the last sync that saw their track; the next sync subtracts them.</summary>
    public List<ReportedStart> ReportedStarts { get; set; } = [];

    /// <summary>Gets the plays the server could not be told of yet, oldest first (#339).</summary>
    public List<PendingPlay> Pending { get; set; } = [];

    /// <summary>
    /// Gets or sets when the statistics' history began (#382): the first use, or for a file from before it, the
    /// first use after the update, whose plays include the first sync's. Clearing the history starts it again.
    /// </summary>
    public DateTimeOffset? Started { get; set; }

    /// <summary>Gets the album, artists and length of the played songs, by track ID (#382).</summary>
    public Dictionary<string, PlayedTrack> Tracks { get; set; } = [];
}

/// <summary>Source-generated JSON metadata for the play history files.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PlayHistoryData))]
internal sealed partial class PlayHistoryJsonContext : JsonSerializerContext;
