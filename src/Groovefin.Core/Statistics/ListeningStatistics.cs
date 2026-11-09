using Groovefin.Core.Api;
using Groovefin.Core.History;
using Groovefin.Core.Models;

namespace Groovefin.Core.Statistics;

/// <summary>The statistics page's numbers (#382).</summary>
public interface IListeningStatistics
{
    /// <summary>Gets today, local.</summary>
    DateTime Today { get; }

    /// <summary>Reads the plays from other devices first, as On repeat does: call it when the page opens.</summary>
    Task SyncAsync();

    /// <summary>Gets the statistics of <paramref name="period"/>.</summary>
    /// <exception cref="JellyfinException">All time's play counts could not be read.</exception>
    Task<ListeningReport> GetReportAsync(StatisticsPeriod period, CancellationToken cancellationToken = default);
}

/// <summary>
/// The statistics page's numbers (#382): a calendar period from the play history, all time from the server's play
/// counts. Plays recorded before the history kept their songs' albums and artists get them from the server once.
/// </summary>
public sealed class ListeningStatistics(PlayHistory history, ILibraryService library, TimeProvider time) : IListeningStatistics
{
    /// <summary>Songs per request for all time, most played first.</summary>
    public const int PageSize = 500;

    /// <summary>The most played songs all time looks at; songs played less often than these do not count.</summary>
    public const int MaxSongs = 5000;

    /// <summary>Songs per request when reading the albums and artists of older plays.</summary>
    public const int LookupSize = 100;

    public DateTime Today => time.GetLocalNow().DateTime.Date;

    public Task SyncAsync() => history.SyncAsync();

    public async Task<ListeningReport> GetReportAsync(StatisticsPeriod period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        var log = await CompleteAsync(await history.GetLogAsync().ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        if (!period.IsAllTime)
        {
            return ListeningReports.ForPeriod(log, period, time.LocalTimeZone);
        }

        var played = await PlayedSongsAsync(cancellationToken).ConfigureAwait(false);
        return ListeningReports.ForAllTime(played, log, time.LocalTimeZone, Today);
    }

    /// <summary>
    /// Adds the albums and artists of the played songs the history has none for (plays from before #382), from the
    /// server, and keeps them. Without the server (offline), those plays count without them.
    /// </summary>
    private async Task<PlayLog> CompleteAsync(PlayLog log, CancellationToken cancellationToken)
    {
        var missing = log.Plays
            .Where(p => p.Source != PlaySource.Seeded && !log.Tracks.ContainsKey(p.TrackId))
            .Select(p => p.TrackId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0)
        {
            return log;
        }

        List<Track> found = [];
        try
        {
            foreach (var chunk in missing.Chunk(LookupSize))
            {
                found.AddRange(await library.GetTracksByIdAsync(chunk, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (JellyfinException)
        {
            // Offline or a server error: these plays count without their albums and artists this time.
        }

        if (found.Count == 0)
        {
            return log;
        }

        await history.RememberTracksAsync(found).ConfigureAwait(false);
        var tracks = new Dictionary<string, PlayedTrack>(log.Tracks, StringComparer.Ordinal);
        foreach (var track in found)
        {
            tracks[track.Id] = PlayedTrack.Of(track);
        }

        return log with { Tracks = tracks };
    }

    /// <summary>The played songs, most played first, up to <see cref="MaxSongs"/>.</summary>
    private async Task<List<Track>> PlayedSongsAsync(CancellationToken cancellationToken)
    {
        List<Track> played = [];
        for (var start = 0; start < MaxSongs; start += PageSize)
        {
            var page = await library.GetTracksAsync(
                new TrackQuery { StartIndex = start, Limit = PageSize, SortBy = TrackSortField.PlayCount, Descending = true },
                cancellationToken).ConfigureAwait(false);
            foreach (var track in page.Items)
            {
                if (track.PlayCount <= 0)
                {
                    return played;
                }

                played.Add(track);
            }

            if (page.Items.Count < PageSize)
            {
                break;
            }
        }

        return played;
    }
}
