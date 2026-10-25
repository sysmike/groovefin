using System.Text.Json.Serialization;

namespace Groovefin.Core.History;

/// <summary>Where a play in the history was seen (#251).</summary>
public enum PlaySource
{
    /// <summary>Listened to in Groovefin, for half the track or 4 minutes.</summary>
    Groovefin,

    /// <summary>A play count the server gained without Groovefin: another device.</summary>
    Server,
}

/// <summary>One play of a track.</summary>
public sealed record PlayRecord(string TrackId, DateTimeOffset PlayedAt, PlaySource Source);

/// <summary>A track's play count on the server at the last sync, and when it was last played then.</summary>
public sealed record PlayCountRecord(int PlayCount, DateTimeOffset LastPlayed);

/// <summary>A start Groovefin reported to the server, which counted it as a play.</summary>
public sealed record ReportedStart(string TrackId, DateTimeOffset StartedAt);

/// <summary>The play history of one user on one server, as saved (#251).</summary>
public sealed record PlayHistoryData
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Gets or sets when the play counts were last read from the server, or <see langword="null"/> before the first time.</summary>
    public DateTimeOffset? LastSync { get; set; }

    public List<PlayRecord> Plays { get; init; } = [];

    /// <summary>Gets the play counts at the last sync, by track ID.</summary>
    public Dictionary<string, PlayCountRecord> Counts { get; init; } = [];

    /// <summary>Gets the starts reported since the last sync that saw their track; the next sync subtracts them.</summary>
    public List<ReportedStart> ReportedStarts { get; init; } = [];
}

/// <summary>Source-generated JSON metadata for the play history files.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PlayHistoryData))]
internal sealed partial class PlayHistoryJsonContext : JsonSerializerContext;
