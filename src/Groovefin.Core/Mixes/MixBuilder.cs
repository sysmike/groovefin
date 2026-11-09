using Groovefin.Core.Api;
using Groovefin.Core.Models;

namespace Groovefin.Core.Mixes;

/// <summary>
/// Picks a mix's songs (#401). The server filters by genre, years and favourites and sorts in the mix's order; the
/// play count, the last play (on any device) and the date added are checked here, page by page, until the mix is
/// full or <see cref="MaxScanned"/> songs were looked at.
/// </summary>
public sealed class MixBuilder(ILibraryService library, TimeProvider time)
{
    /// <summary>Songs asked for per request.</summary>
    public const int PageSize = 500;

    /// <summary>The most songs looked at for one mix, so a rule few songs match does not read a whole large library.</summary>
    public const int MaxScanned = 5000;

    public async Task<IReadOnlyList<Track>> BuildAsync(MixRules rules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var size = Math.Clamp(rules.Size, 1, MixRules.MaxSize);
        var (sortBy, descending) = Sort(rules.Order);
        var now = time.GetUtcNow();
        var picked = new List<Track>(size);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var start = 0; start < MaxScanned && picked.Count < size; start += PageSize)
        {
            var page = await library.GetTracksAsync(
                new TrackQuery
                {
                    StartIndex = start,
                    Limit = PageSize,
                    SortBy = sortBy,
                    Descending = descending,
                    GenreIds = rules.GenreId is { } genre ? [genre] : [],
                    MinYear = rules.FromYear,
                    MaxYear = rules.ToYear,
                    FavouritesOnly = rules.FavouritesOnly,
                },
                cancellationToken).ConfigureAwait(false);

            // A random order is drawn anew for every page: a song can come again.
            picked.AddRange(page.Items.Where(t => seen.Add(t.Id) && Matches(rules, t, now)).Take(size - picked.Count));
            if (!page.HasMore || page.Items.Count == 0)
            {
                break;
            }
        }

        return picked;
    }

    /// <summary>Whether <paramref name="track"/> passes the rules the server does not check.</summary>
    internal static bool Matches(MixRules rules, Track track, DateTimeOffset now)
    {
        if (rules.MinPlays is { } min && track.PlayCount < min)
        {
            return false;
        }

        if (rules.MaxPlays is { } max && track.PlayCount > max)
        {
            return false;
        }

        if (rules.NotPlayedForDays is { } days && track.LastPlayed is { } played && played > now.AddDays(-days))
        {
            return false;
        }

        return rules.AddedWithinDays is not { } added || (track.DateAdded is { } date && date >= now.AddDays(-added));
    }

    private static (TrackSortField SortBy, bool Descending) Sort(MixOrder order) => order switch
    {
        MixOrder.MostPlayed => (TrackSortField.PlayCount, true),
        MixOrder.RecentlyAdded => (TrackSortField.DateAdded, true),
        MixOrder.LeastRecentlyPlayed => (TrackSortField.DatePlayed, false),
        _ => (TrackSortField.Random, false),
    };
}
