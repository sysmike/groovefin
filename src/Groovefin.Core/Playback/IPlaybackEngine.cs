using Groovefin.Core.Models;

namespace Groovefin.Core.Playback;

/// <summary>What the engine is doing.</summary>
public enum PlaybackState
{
    /// <summary>Nothing loaded.</summary>
    Idle,

    Opening,

    Buffering,

    Playing,

    Paused,
}

/// <summary>
/// A stream to play. <see cref="Id"/> identifies the queue entry, so track changes can be matched to the
/// queue (also when the engine moves on by itself, e.g. gapless).
/// </summary>
/// <remarks><see cref="Url"/> can carry the access token (stream URLs do): never log it.</remarks>
public sealed record PlaybackSource(string Id, Uri Url, Track Track)
{
    /// <summary>Describes the source without its URL, so logging it cannot leak a token.</summary>
    public override string ToString() => $"{Id} ({Track.Name})";
}

/// <summary>Why playback of <see cref="Source"/> failed.</summary>
public sealed class PlaybackFailedEventArgs(PlaybackSource source, string message, Exception? exception) : EventArgs
{
    public PlaybackSource Source { get; } = source;

    /// <summary>Gets the platform's error message, for the log. Not localized.</summary>
    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;
}

/// <summary>
/// Plays one audio stream at a time. Implemented per platform (Windows <c>MediaPlayer</c> first, libmpv
/// possibly later). Call members from the UI thread; all events are raised on the UI thread through
/// <c>IDispatcher</c>.
/// </summary>
public interface IPlaybackEngine
{
    PlaybackState State { get; }

    /// <summary>Gets the loaded source, or <see langword="null"/> when idle.</summary>
    PlaybackSource? Current { get; }

    TimeSpan Position { get; }

    /// <summary>Gets the source queued with <see cref="SetNext"/> to follow the current one, or <see langword="null"/>.</summary>
    PlaybackSource? Next { get; }

    /// <summary>Gets the length of the current source; <see cref="TimeSpan.Zero"/> while unknown.</summary>
    TimeSpan Duration { get; }

    /// <summary>Gets or sets the volume from 0 to 1; values outside are clamped.</summary>
    double Volume { get; set; }

    bool IsMuted { get; set; }

    /// <summary>Raised after <see cref="State"/> changed.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised when <see cref="Position"/> or <see cref="Duration"/> changed, at most a few times a second.</summary>
    event EventHandler? PositionChanged;

    /// <summary>Raised after <see cref="Current"/> changed: on load and stop, and when the engine moved on to <see cref="Next"/> by itself.</summary>
    event EventHandler? CurrentChanged;

    /// <summary>Raised when the current source played to its end and no <see cref="Next"/> followed it.</summary>
    event EventHandler? Ended;

    /// <summary>Raised when the current source cannot be played.</summary>
    event EventHandler<PlaybackFailedEventArgs>? Failed;

    /// <summary>Loads <paramref name="source"/>, replacing the current one, and starts it unless <paramref name="autoPlay"/> is false.</summary>
    void Load(PlaybackSource source, bool autoPlay = true);

    /// <summary>
    /// Queues <paramref name="next"/> to play right after the current source without a gap: the engine
    /// pre-buffers it, and when the current source ends it moves on by itself (<see cref="Current"/> becomes
    /// it and <see cref="CurrentChanged"/> is raised instead of <see cref="Ended"/>). Replaces a source
    /// queued before; <see langword="null"/> clears it. Ignored while nothing is loaded; <see cref="Load"/>
    /// and <see cref="Stop"/> clear it. If the next source cannot be pre-buffered it is dropped, and the
    /// current one ends normally.
    /// </summary>
    void SetNext(PlaybackSource? next);

    void Play();

    void Pause();

    /// <summary>Moves to <paramref name="position"/>, clamped to the source's length.</summary>
    void Seek(TimeSpan position);

    /// <summary>Stops and unloads the current source.</summary>
    void Stop();
}
