namespace Groovefin.Core.Api;

/// <summary>One report about a playing track.</summary>
/// <param name="ItemId">The track's Jellyfin ID.</param>
/// <param name="PlaySessionId">Identifies this play of the track, from start to stop.</param>
/// <param name="Position">Playback position.</param>
/// <param name="IsPaused">Whether playback is paused.</param>
/// <param name="Volume">Volume from 0 to 100.</param>
/// <param name="IsMuted">Whether the sound is muted.</param>
public sealed record PlaybackReport(string ItemId, string PlaySessionId, TimeSpan Position, bool IsPaused, int Volume, bool IsMuted);

/// <summary>
/// Tells the server what is playing, so play counts, "recently played" and the dashboard's sessions
/// stay current (<c>/Sessions/Playing</c>, <c>…/Progress</c>, <c>…/Stopped</c>).
/// </summary>
public interface IPlaybackReporter
{
    /// <summary>Reports that a track started playing.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task ReportStartAsync(PlaybackReport report, CancellationToken cancellationToken = default);

    /// <summary>Reports the position and state of the playing track.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task ReportProgressAsync(PlaybackReport report, CancellationToken cancellationToken = default);

    /// <summary>Reports that a track stopped playing.</summary>
    /// <exception cref="JellyfinException">The request failed.</exception>
    Task ReportStoppedAsync(PlaybackReport report, CancellationToken cancellationToken = default);
}
