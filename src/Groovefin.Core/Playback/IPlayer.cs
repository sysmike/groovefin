using Groovefin.Core.Models;
using Groovefin.Core.Queue;

namespace Groovefin.Core.Playback;

/// <summary>
/// What the app plays: the queue and the engine together. Pages and the now playing bar use this
/// instead of driving <see cref="QueueManager"/> and <see cref="IPlaybackEngine"/> one by one.
/// </summary>
public interface IPlayer
{
    QueueManager Queue { get; }

    IPlaybackEngine Engine { get; }

    /// <summary>Replaces the queue with <paramref name="tracks"/> and plays the one at <paramref name="startIndex"/>.</summary>
    void Play(IReadOnlyList<Track> tracks, int startIndex = 0);

    /// <summary>Turns shuffle on and plays <paramref name="tracks"/> from a random track.</summary>
    void Shuffle(IReadOnlyList<Track> tracks);

    /// <summary>Plays <paramref name="tracks"/> after the current track.</summary>
    void PlayNext(IReadOnlyList<Track> tracks);

    /// <summary>Adds <paramref name="tracks"/> to the end of the queue.</summary>
    void AddToQueue(IReadOnlyList<Track> tracks);

    /// <summary>Inserts <paramref name="tracks"/> at <paramref name="position"/> among the upcoming tracks (0 plays them next).</summary>
    void InsertIntoQueue(IReadOnlyList<Track> tracks, int position);

    void TogglePlayPause();

    void Next();

    /// <summary>Restarts the track after a few seconds of playback, otherwise goes to the previous one.</summary>
    void Previous();

    /// <summary>Loads the queue's current entry paused at <paramref name="position"/>, e.g. after restoring a session.</summary>
    void Resume(TimeSpan position);

    /// <summary>Plays a queue entry, e.g. picked in the queue panel.</summary>
    void JumpTo(string queueItemId);
}
