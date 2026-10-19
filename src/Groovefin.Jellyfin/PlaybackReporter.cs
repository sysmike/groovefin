using Groovefin.Core.Api;
using Jellyfin.Sdk.Generated.Models;

namespace Groovefin.Jellyfin;

/// <summary><see cref="IPlaybackReporter"/> on Jellyfin's session endpoints for the active connection.</summary>
internal sealed class PlaybackReporter(JellyfinClient client) : IPlaybackReporter
{
    public Task ReportStartAsync(PlaybackReport report, CancellationToken cancellationToken = default)
    {
        var (api, _) = client.Active;
        var info = new PlaybackStartInfo
        {
            ItemId = ParseId(report),
            PlaySessionId = report.PlaySessionId,
            PositionTicks = report.Position.Ticks,
            IsPaused = report.IsPaused,
            IsMuted = report.IsMuted,
            VolumeLevel = report.Volume,
            CanSeek = true,
        };
        return JellyfinCall.RunAsync(() => api.Sessions.Playing.PostAsync(info, cancellationToken: cancellationToken), "Reporting playback start");
    }

    public Task ReportProgressAsync(PlaybackReport report, CancellationToken cancellationToken = default)
    {
        var (api, _) = client.Active;
        var info = new PlaybackProgressInfo
        {
            ItemId = ParseId(report),
            PlaySessionId = report.PlaySessionId,
            PositionTicks = report.Position.Ticks,
            IsPaused = report.IsPaused,
            IsMuted = report.IsMuted,
            VolumeLevel = report.Volume,
            CanSeek = true,
        };
        return JellyfinCall.RunAsync(() => api.Sessions.Playing.Progress.PostAsync(info, cancellationToken: cancellationToken), "Reporting playback progress");
    }

    public Task ReportStoppedAsync(PlaybackReport report, CancellationToken cancellationToken = default)
    {
        var (api, _) = client.Active;
        var info = new PlaybackStopInfo
        {
            ItemId = ParseId(report),
            PlaySessionId = report.PlaySessionId,
            PositionTicks = report.Position.Ticks,
        };
        return JellyfinCall.RunAsync(() => api.Sessions.Playing.Stopped.PostAsync(info, cancellationToken: cancellationToken), "Reporting playback stop");
    }

    private static Guid ParseId(PlaybackReport report) =>
        Guid.TryParse(report.ItemId, out var id) ? id : throw new ArgumentException($"'{report.ItemId}' is not a Jellyfin item ID.", nameof(report));
}
