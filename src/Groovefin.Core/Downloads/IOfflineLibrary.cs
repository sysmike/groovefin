namespace Groovefin.Core.Downloads;

/// <summary>The downloaded songs of the signed-in user: for playback (#333) and the marks in the lists (#337).</summary>
public interface IOfflineLibrary
{
    /// <summary>Raised on any thread after songs were downloaded or deleted, or another user's downloads loaded.</summary>
    event EventHandler? Changed;

    /// <summary>Gets the full path of the song's downloaded file, or <see langword="null"/> when it has none.</summary>
    string? FileOf(string trackId);

    /// <summary>Gets a value indicating whether the song is downloaded, from the index alone: quick, for list rows.</summary>
    bool IsDownloaded(string trackId);

    /// <summary>
    /// Gets a value indicating whether a favourite album or artist, or a playlist, is downloaded with every song.
    /// </summary>
    bool IsComplete(string itemId);
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
