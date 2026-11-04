namespace Groovefin.Core.Downloads;

/// <summary>The downloaded songs of the signed-in user, for playback (#333).</summary>
public interface IOfflineLibrary
{
    /// <summary>Gets the full path of the song's downloaded file, or <see langword="null"/> when it has none.</summary>
    string? FileOf(string trackId);
}

/// <summary>Offline, none of the songs left in the queue is downloaded (#333).</summary>
public sealed class NotDownloadedException : Exception
{
    public NotDownloadedException()
        : base("Offline, and none of the songs left in the queue is downloaded.")
    {
    }

    public NotDownloadedException(string message)
        : base(message)
    {
    }

    public NotDownloadedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
