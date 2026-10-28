namespace Groovefin.Core.Models;

/// <summary>
/// Everything the server knows about one track, for its info dialog (#282): the tags it read from the
/// file, the file's format and the user's data. Loaded when the dialog opens; lists load only
/// <see cref="Models.Track"/>.
/// </summary>
public sealed record TrackDetails
{
    /// <summary>Gets the fields lists load too: title, artists, album, numbers, length, gains and play data.</summary>
    public required Track Track { get; init; }

    public IReadOnlyList<string> AlbumArtists { get; init; } = [];

    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Gets the release date, or <see langword="null"/> when only the year (or nothing) is known.</summary>
    public DateOnly? ReleaseDate { get; init; }

    /// <summary>Gets the composers, lyricists and other contributors; the artists are in <see cref="Models.Track.Artists"/>.</summary>
    public IReadOnlyList<TrackPerson> People { get; init; } = [];

    /// <summary>Gets the record labels.</summary>
    public IReadOnlyList<string> Studios { get; init; } = [];

    /// <summary>Gets the tags given on the server.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets IDs in other databases by provider key (e.g. "MusicBrainzTrack"), sorted by key.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ProviderIds { get; init; } = [];

    /// <summary>Gets whether the server has lyrics for the track, or <see langword="null"/> when unknown.</summary>
    public bool? HasLyrics { get; init; }

    /// <summary>Gets the file format as the server names it, e.g. "flac".</summary>
    public string? Container { get; init; }

    /// <summary>Gets the audio codec, e.g. "flac" or "mp3".</summary>
    public string? Codec { get; init; }

    /// <summary>Gets the bitrate in bits per second.</summary>
    public int? Bitrate { get; init; }

    /// <summary>Gets the sample rate in Hz.</summary>
    public int? SampleRate { get; init; }

    public int? BitDepth { get; init; }

    public int? Channels { get; init; }

    /// <summary>Gets the file size in bytes.</summary>
    public long? Size { get; init; }

    /// <summary>Gets the file's path on the server.</summary>
    public string? Path { get; init; }
}

/// <summary>Someone who worked on a track, other than its artists.</summary>
public sealed record TrackPerson(string Name, PersonRole Role);

/// <summary>What a <see cref="TrackPerson"/> did; the info dialog groups people by role.</summary>
public enum PersonRole
{
    Composer,
    Lyricist,
    Writer,
    Arranger,
    Conductor,
    Producer,
    Engineer,
    Mixer,
    Remixer,

    /// <summary>Any other role the server knows.</summary>
    Other,
}
