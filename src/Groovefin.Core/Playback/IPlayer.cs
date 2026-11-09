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

    /// <summary>Gets a value indicating whether a stream the network broke waits to be tried again where it stopped (#385).</summary>
    bool IsReconnecting { get; }

    /// <summary>Raised on the UI thread when <see cref="IsReconnecting"/> changed.</summary>
    event EventHandler? ReconnectingChanged;

    /// <summary>
    /// Replaces the queue with <paramref name="tracks"/> and plays the one at <paramref name="startIndex"/>.
    /// Without an index it plays the first, or with shuffle on a random one (#317).
    /// </summary>
    void Play(IReadOnlyList<Track> tracks, int? startIndex = null);

    /// <summary>Turns shuffle on and plays <paramref name="tracks"/> from a random track.</summary>
    void Shuffle(IReadOnlyList<Track> tracks);

    /// <summary>Plays <paramref name="tracks"/> album by album in a random order, each album's songs in order (#403).</summary>
    void ShuffleAlbums(IReadOnlyList<Track> tracks);

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
