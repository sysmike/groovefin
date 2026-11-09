using Groovefin.Core.Models;

namespace Groovefin.Core.Queue;

/// <summary>
/// The shuffled orders (#403). <see cref="Spread{T}"/> mixes songs so that one artist, and one album of an artist,
/// rarely plays twice in a row; <see cref="Albums"/> keeps each album's songs together and in order.
/// </summary>
/// <remarks>
/// Spreading works like a "balanced" shuffle: the songs of each group (an artist) get places evenly apart on a
/// line from 0 to 1, starting at a random offset and moved a little at random; then all songs are sorted by
/// their place. A group's own order is spread the same way by album first. Songs without an artist or album
/// are each a group of their own, so a queue without them is shuffled plainly at random.
/// </remarks>
public static class Shuffling
{
    /// <summary>How far a place may move from its even spot, as a share of the gap between spots.</summary>
    private const double Jitter = 0.2;

    /// <summary>Gets <paramref name="items"/> shuffled with artists and, within an artist, albums spread out.</summary>
    public static List<T> Spread<T>(IReadOnlyList<T> items, Func<T, Track> track, Random random)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(random);

        return SpreadBy(items, (item, index) => ArtistKey(track(item), index), random, group =>
            SpreadBy(group, (item, index) => AlbumKey(track(item), index), random, Shuffled));

        List<T> Shuffled(List<T> group)
        {
            // Fisher-Yates.
            for (var i = group.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (group[i], group[j]) = (group[j], group[i]);
            }

            return group;
        }
    }

    /// <summary>
    /// Gets <paramref name="tracks"/> album by album in a random order, each album's songs by disc and track number
    /// (in the order given when they have none); albums by the same artist are spread apart.
    /// </summary>
    public static List<Track> Albums(IReadOnlyList<Track> tracks, Random random)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(random);

        var albums = Group(tracks, AlbumKey);
        var order = Spread(albums, album => album[0], random);
        return [.. order.SelectMany(album => album.OrderBy(t => t.DiscNumber ?? 0).ThenBy(t => t.TrackNumber ?? 0))];
    }

    /// <summary>Spreads the groups that <paramref name="key"/> makes; <paramref name="arrange"/> orders each group's own items.</summary>
    private static List<T> SpreadBy<T>(IReadOnlyList<T> items, Func<T, int, string> key, Random random, Func<List<T>, List<T>> arrange)
    {
        var places = new List<(double Place, T Item)>(items.Count);
        foreach (var group in Group(items, key))
        {
            var members = arrange(group);
            var gap = 1d / members.Count;
            var offset = random.NextDouble() * gap;
            for (var i = 0; i < members.Count; i++)
            {
                places.Add((offset + (i * gap) + ((random.NextDouble() - 0.5) * Jitter * gap), members[i]));
            }
        }

        return [.. places.OrderBy(p => p.Place).Select(p => p.Item)];
    }

    /// <summary>Groups <paramref name="items"/> by <paramref name="key"/>, in the order each group first appears.</summary>
    private static List<List<T>> Group<T>(IReadOnlyList<T> items, Func<T, int, string> key)
    {
        var groups = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        var order = new List<List<T>>();
        for (var i = 0; i < items.Count; i++)
        {
            var k = key(items[i], i);
            if (!groups.TryGetValue(k, out var group))
            {
                group = [];
                groups[k] = group;
                order.Add(group);
            }

            group.Add(items[i]);
        }

        return order;
    }

    /// <summary>The song's first artist, else its album artist, else a key of its own.</summary>
    private static string ArtistKey(Track track, int index) =>
        track.Artists is [{ Id: { Length: > 0 } id }, ..] ? $"id:{id}"
        : track.AlbumArtist is { Length: > 0 } name ? $"name:{name}"
        : $"#{index}";

    private static string AlbumKey(Track track, int index) => track.AlbumId is { Length: > 0 } id ? $"id:{id}" : $"#{index}";
}
