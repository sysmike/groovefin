using Groovefin.Core.Models;

namespace Groovefin.Core.Queue;

/// <summary>
/// The queue as it can be saved and restored: the tracks in the user's order, the play order (differs
/// while shuffled), the current entry, shuffle and repeat. History is not kept.
/// The properties with a default have setters: for one a file does not have, the source-generated JSON reader
/// then keeps the default, where it sets an init-only property to <see langword="null"/> or 0 (#353, #355).
/// </summary>
public sealed record QueueSnapshot
{
    /// <summary>Gets the tracks in the order the user built.</summary>
    public IReadOnlyList<Track> Tracks { get; set; } = [];

    /// <summary>Gets the play order as indices into <see cref="Tracks"/>; empty means the same order.</summary>
    public IReadOnlyList<int> PlayOrder { get; set; } = [];

    /// <summary>Gets the index of the current entry in play order, or -1.</summary>
    public int CurrentIndex { get; set; } = -1;

    public bool Shuffle { get; init; }

    public RepeatMode Repeat { get; init; }

    /// <summary>Gets a value indicating whether the indices fit the tracks, e.g. after reading a damaged file.</summary>
    public bool IsValid =>
        Tracks.Count > 0
        && (PlayOrder.Count == 0 || (PlayOrder.Count == Tracks.Count && PlayOrder.Order().SequenceEqual(Enumerable.Range(0, Tracks.Count))))
        && CurrentIndex >= -1 && CurrentIndex < Tracks.Count;
}
