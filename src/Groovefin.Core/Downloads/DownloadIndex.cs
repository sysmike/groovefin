using System.Text.Json.Serialization;
using Groovefin.Core.Models;

namespace Groovefin.Core.Downloads;

/// <summary>A downloaded song: its metadata and file (#327).</summary>
public sealed record DownloadedTrack
{
    public required Track Track { get; init; }

    /// <summary>Gets the file's name in the user's download folder, e.g. <c>{id}.flac</c>.</summary>
    public required string FileName { get; init; }

    public long Size { get; init; }

    /// <summary>Gets the <see cref="DownloadFormat.Key"/> it was downloaded in.</summary>
    public required string Format { get; init; }

    public DateTimeOffset DownloadedAt { get; init; }

    /// <summary>
    /// Gets the <see cref="TrackFile.Mark"/> of the server's file it was downloaded from: another one downloads the
    /// song again.
    /// </summary>
    public string? FileVersion { get; init; }
}

/// <summary>A downloaded playlist: what it is and its songs in order, for offline browsing later (#58).</summary>
public sealed record DownloadedPlaylist
{
    public required Playlist Playlist { get; init; }

    public IReadOnlyList<string> TrackIds { get; init; } = [];
}

/// <summary>A downloaded favourite album: what it is and its songs in order, for offline browsing later (#331).</summary>
public sealed record DownloadedAlbum
{
    public required Album Album { get; init; }

    public IReadOnlyList<string> TrackIds { get; init; } = [];
}

/// <summary>A downloaded favourite artist: who it is and its songs, for offline browsing later (#331).</summary>
public sealed record DownloadedArtist
{
    public required Artist Artist { get; init; }

    public IReadOnlyList<string> TrackIds { get; init; } = [];
}

/// <summary>What one user downloaded from one server, as saved next to the files (#327).</summary>
public sealed record DownloadIndex
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Gets the downloaded songs by ID.</summary>
    public Dictionary<string, DownloadedTrack> Tracks { get; init; } = [];

    /// <summary>Gets or sets the favourite songs at the last sync, by name.</summary>
    public IReadOnlyList<string> FavouriteSongs { get; set; } = [];

    /// <summary>Gets or sets the playlists at the last sync.</summary>
    public IReadOnlyList<DownloadedPlaylist> Playlists { get; set; } = [];

    /// <summary>Gets or sets the favourite albums at the last sync.</summary>
    public IReadOnlyList<DownloadedAlbum> Albums { get; set; } = [];

    /// <summary>Gets or sets the favourite artists at the last sync.</summary>
    public IReadOnlyList<DownloadedArtist> Artists { get; set; } = [];

    /// <summary>Gets the IDs of every song a collection holds.</summary>
    public IEnumerable<string> WantedTrackIds() =>
        FavouriteSongs
            .Concat(Playlists.SelectMany(p => p.TrackIds))
            .Concat(Albums.SelectMany(a => a.TrackIds))
            .Concat(Artists.SelectMany(a => a.TrackIds));

    /// <summary>Gets or sets when the wanted songs were last read from the server.</summary>
    public DateTimeOffset? LastSync { get; set; }
}

/// <summary>Source-generated JSON metadata for the download index.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DownloadIndex))]
internal sealed partial class DownloadJsonContext : JsonSerializerContext;
