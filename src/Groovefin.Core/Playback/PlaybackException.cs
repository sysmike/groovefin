namespace Groovefin.Core.Playback;

/// <summary>
/// The playback engine could not play a stream. <see cref="Exception.InnerException"/> is the engine's own
/// error, if it reported one: an <see cref="UnauthorizedAccessException"/> means the server refused the
/// stream (Media Foundation reports HTTP 401 and 403 as access denied).
/// </summary>
public sealed class PlaybackException : Exception
{
    public PlaybackException()
    {
    }

    public PlaybackException(string message)
        : base(message)
    {
    }

    public PlaybackException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
