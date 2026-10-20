using Groovefin.Core.Models;

namespace Groovefin.Core.Queue;

/// <summary>
/// The queue as it can be saved and restored: the tracks in the user's order, the play order (differs
/// while shuffled), the current entry, shuffle and repeat. History is not kept.
/// </summary>
public sealed record QueueSnapshot
{
    /// <summary>Gets the tracks in the order the user built.</summary>
    public IReadOnlyList<Track> Tracks { get; init; } = [];

    /// <summary>Gets the play order as indices into <see cref="Tracks"/>; empty means the same order.</summary>
    public IReadOnlyList<int> PlayOrder { get; init; } = [];

    /// <summary>Gets the index of the current entry in play order, or -1.</summary>
    public int CurrentIndex { get; init; } = -1;

    public bool Shuffle { get; init; }

    public RepeatMode Repeat { get; init; }

    /// <summary>Gets a value indicating whether the indices fit the tracks, e.g. after reading a damaged file.</summary>
    public bool IsValid =>
        Tracks.Count > 0
        && (PlayOrder.Count == 0 || (PlayOrder.Count == Tracks.Count && PlayOrder.Order().SequenceEqual(Enumerable.Range(0, Tracks.Count))))
        && CurrentIndex >= -1 && CurrentIndex < Tracks.Count;
}
