using Groovefin.Core.Models;

namespace Groovefin.Core.Queue;

/// <summary>
/// One entry in the play queue. <see cref="Id"/> tells entries apart when the same track is queued
/// more than once.
/// </summary>
public sealed record QueueItem(string Id, Track Track);

/// <summary>What happens at the end of a track or of the queue.</summary>
public enum RepeatMode
{
    /// <summary>Stop after the last track.</summary>
    Off,

    /// <summary>Start again with the first track after the last one.</summary>
    All,

    /// <summary>Play the current track again when it ends. Next and Previous still move.</summary>
    One,
}
