using System.Net;
using System.Runtime.ExceptionServices;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Services;

namespace Groovefin.Core.Downloads;

/// <summary>
/// Asks the server, and answers from the downloads when it cannot be reached (#335): the shared part of
/// <see cref="OfflineLibraryService"/>, <see cref="OfflinePlaylistService"/> and <see cref="OfflineSearchService"/>.
/// Without a network, or while "Work offline" is on (#400), the server is not asked at all. Any other error, and a
/// query the downloads cannot answer, fails as before.
/// </summary>
public sealed class OfflineAnswers(OfflineCatalog catalog, OfflineMode mode, INetworkStatus network)
{
    public async Task<T> AskAsync<T>(Func<Task<T>> server, Func<DownloadIndex, T?> fromDownloads, CancellationToken cancellationToken)
        where T : class
    {
        ExceptionDispatchInfo unreachable;
        if (network.IsOnline)
        {
            try
            {
                var answer = await server().ConfigureAwait(false);
                mode.ReachedServer();
                return answer;
            }
            catch (JellyfinException ex) when (ex.StatusCode is null)
            {
                unreachable = ExceptionDispatchInfo.Capture(ex);
            }
        }
        else
        {
            unreachable = ExceptionDispatchInfo.Capture(new JellyfinException("The PC has no network connection.", (HttpStatusCode?)null));
        }

        if (await catalog.GetAsync(cancellationToken).ConfigureAwait(false) is { } index && fromDownloads(index) is { } downloaded)
        {
            mode.ServedFromDownloads();
            return downloaded;
        }

        unreachable.Throw();
        throw unreachable.SourceException;
    }

    /// <summary>
    /// Answers from the downloads alone, for the "Downloaded only" filters (#400): online too, without asking the
    /// server. Without any downloads it is <paramref name="none"/>.
    /// </summary>
    public async Task<T> FromDownloadsAsync<T>(Func<DownloadIndex, T> fromDownloads, T none, CancellationToken cancellationToken)
    {
        var index = await catalog.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!network.IsOnline)
        {
            mode.ServedFromDownloads();
        }

        return index is null ? none : fromDownloads(index);
    }

    /// <summary>The downloaded songs among <paramref name="ids"/>, in their order, each once.</summary>
    public static IReadOnlyList<Track> Downloaded(DownloadIndex index, IEnumerable<string> ids) =>
        [.. ids.Distinct(StringComparer.Ordinal).Where(index.Tracks.ContainsKey).Select(id => index.Tracks[id].Track)];

    /// <summary>One page of <paramref name="items"/>, for a paged query.</summary>
    public static PagedResult<T> Page<T>(IReadOnlyList<T> items, int startIndex, int limit) =>
        new([.. items.Skip(startIndex).Take(limit)], items.Count, startIndex);
}
