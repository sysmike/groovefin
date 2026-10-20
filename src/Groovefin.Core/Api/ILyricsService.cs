using Groovefin.Core.Lyrics;

namespace Groovefin.Core.Api;

/// <summary>Lyrics of tracks on the active connection.</summary>
public interface ILyricsService
{
    /// <summary>Gets the track's lyrics, synced when the server has time tags; <see langword="null"/> when it has none.</summary>
    /// <exception cref="InvalidOperationException">There is no active connection.</exception>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task<TrackLyrics?> GetLyricsAsync(string trackId, CancellationToken cancellationToken = default);
}
