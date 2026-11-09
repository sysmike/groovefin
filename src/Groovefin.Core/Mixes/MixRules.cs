namespace Groovefin.Core.Mixes;

/// <summary>The order a mix takes its songs in (#401).</summary>
public enum MixOrder
{
    /// <summary>A new random choice every time the mix opens.</summary>
    Random,

    MostPlayed,

    RecentlyAdded,

    /// <summary>The songs not played for the longest time first, never played ones before them.</summary>
    LeastRecentlyPlayed,
}

/// <summary>
/// The rules of a mix (#401): which songs it takes and how many. Every rule left empty matches every song. Saved in
/// the settings: the properties with a default have setters, so a file without one keeps the default (#355).
/// </summary>
public sealed record MixRules
{
    /// <summary>The most songs a mix can have.</summary>
    public const int MaxSize = 500;

    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the genre's ID, or <see langword="null"/> for every genre.</summary>
    public string? GenreId { get; set; }

    /// <summary>Gets the genre's name, for the mix's summary without asking the server.</summary>
    public string? GenreName { get; set; }

    public int? FromYear { get; set; }

    public int? ToYear { get; set; }

    public bool FavouritesOnly { get; set; }

    /// <summary>Gets the fewest plays a song needs, or <see langword="null"/> for any.</summary>
    public int? MinPlays { get; set; }

    /// <summary>Gets the most plays a song may have (0: never played), or <see langword="null"/> for any.</summary>
    public int? MaxPlays { get; set; }

    /// <summary>Gets how many days a song must not have been played, on any device; never played songs count.</summary>
    public int? NotPlayedForDays { get; set; }

    /// <summary>Gets how many days ago a song may have been added at most.</summary>
    public int? AddedWithinDays { get; set; }

    public MixOrder Order { get; set; } = MixOrder.Random;

    /// <summary>Gets how many songs the mix takes.</summary>
    public int Size { get; set; } = 50;
}

/// <summary>A saved mix: its rules under an ID of its own (#401).</summary>
public sealed record Mix
{
    public required string Id { get; init; }

    public MixRules Rules { get; set; } = new();
}
