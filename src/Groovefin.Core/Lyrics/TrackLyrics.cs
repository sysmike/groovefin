namespace Groovefin.Core.Lyrics;

/// <summary>One line of lyrics; <see cref="Start"/> is <see langword="null"/> for unsynced lyrics.</summary>
public sealed record LyricLine(TimeSpan? Start, string Text);

/// <summary>Information from LRC tags such as <c>[ti:]</c>, <c>[ar:]</c> and <c>[offset:]</c>.</summary>
public sealed record LyricsMetadata
{
    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    /// <summary>Gets who wrote the lyrics file (<c>[by:]</c>).</summary>
    public string? By { get; init; }

    /// <summary>Gets the track length the lyrics were timed for (<c>[length:]</c>).</summary>
    public TimeSpan? Length { get; init; }

    /// <summary>Gets the <c>[offset:]</c> tag. The parser has already applied it to the line times.</summary>
    public TimeSpan Offset { get; init; }
}

/// <summary>
/// The lyrics of a track: synced (every line has a start time, in time order) or unsynced (plain
/// text lines, blank lines kept as paragraph breaks).
/// </summary>
public sealed record TrackLyrics(IReadOnlyList<LyricLine> Lines, LyricsMetadata Metadata)
{
    /// <summary>Gets a value indicating whether every line has a start time.</summary>
    public bool IsSynced => Lines.Count > 0 && Lines.All(l => l.Start is not null);

    /// <summary>
    /// Gets the index of the line being sung at <paramref name="position"/>, or -1 before the first line
    /// and for unsynced lyrics. A positive <paramref name="offset"/> shows the lines later.
    /// </summary>
    public int LineAt(TimeSpan position, TimeSpan offset = default)
    {
        if (!IsSynced)
        {
            return -1;
        }

        // The last line that started at or before the position.
        var time = position - offset;
        int low = 0, high = Lines.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (Lines[middle].Start <= time)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }
}
