using Groovefin.Core.Models;
using Groovefin.Core.Queue;

namespace Groovefin.Core.Tests.Queue;

public sealed class ShufflingTests
{
    private static Track Song(string artist, string album, int number) => new()
    {
        Id = $"{artist}-{album}-{number}",
        Name = $"{artist}-{album}-{number}",
        AlbumId = $"{artist}-{album}",
        Artists = [new ArtistRef(artist, artist)],
        TrackNumber = number,
    };

    /// <summary>Five artists with ten songs each, on two albums of five.</summary>
    private static List<Track> FiveArtists() =>
        [.. from artist in "abcde" from album in "xy" from number in Enumerable.Range(1, 5) select Song(artist.ToString(), album.ToString(), number)];

    private static int Repeats(IReadOnlyList<Track> order, Func<Track, string?> key) =>
        order.Zip(order.Skip(1)).Count(pair => key(pair.First) == key(pair.Second));

    private static List<Track> Spread(IReadOnlyList<Track> tracks, int seed) => Shuffling.Spread(tracks, t => t, new Random(seed));

    [Fact]
    public void Spreading_keeps_every_song_once()
    {
        var tracks = FiveArtists();

        Spread(tracks, 1).Should().BeEquivalentTo(tracks).And.NotEqual(tracks);
    }

    [Fact]
    public void The_same_artist_rarely_plays_twice_in_a_row()
    {
        var tracks = FiveArtists();

        // Plain random puts the same artist next to itself about nine times in this queue.
        var repeats = Enumerable.Range(0, 20).Sum(seed => Repeats(Spread(tracks, seed), t => t.Artists[0].Id));

        repeats.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public void An_artists_albums_are_spread_too()
    {
        List<Track> tracks = [.. from album in "xyz" from number in Enumerable.Range(1, 4) select Song("a", album.ToString(), number)];

        var repeats = Enumerable.Range(0, 20).Sum(seed => Repeats(Spread(tracks, seed), t => t.AlbumId));

        repeats.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public void Songs_without_an_artist_or_album_are_shuffled_at_random()
    {
        List<Track> tracks = [.. Enumerable.Range(0, 10).Select(i => new Track { Id = $"t{i}", Name = $"t{i}" })];

        var orders = Enumerable.Range(0, 5).Select(seed => string.Join(',', Spread(tracks, seed).Select(t => t.Id))).ToHashSet();

        orders.Should().HaveCountGreaterThan(1);
        Spread(tracks, 0).Should().BeEquivalentTo(tracks);
    }

    [Fact]
    public void The_album_artist_counts_when_the_song_has_no_artists()
    {
        List<Track> tracks =
        [
            .. from artist in "abcde" from number in Enumerable.Range(1, 6)
               select new Track { Id = $"{artist}{number}", Name = $"{artist}{number}", AlbumArtist = artist.ToString() },
        ];

        var repeats = Enumerable.Range(0, 20).Sum(seed => Repeats(Spread(tracks, seed), t => t.AlbumArtist));

        repeats.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public void Shuffled_albums_keep_their_songs_together_and_in_order()
    {
        var tracks = FiveArtists();
        tracks.Reverse();

        var order = Shuffling.Albums(tracks, new Random(3));

        order.Should().BeEquivalentTo(tracks);
        order.Chunk(5).Should().AllSatisfy(album =>
        {
            album.Select(t => t.AlbumId).Distinct().Should().ContainSingle();
            album.Select(t => t.TrackNumber).Should().Equal(1, 2, 3, 4, 5);
        });
    }

    [Fact]
    public void Shuffled_albums_come_in_a_random_order()
    {
        var tracks = FiveArtists();

        var orders = Enumerable.Range(0, 5)
            .Select(seed => string.Join(',', Shuffling.Albums(tracks, new Random(seed)).Select(t => t.AlbumId).Distinct()))
            .ToHashSet();

        orders.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Shuffled_albums_keep_songs_without_an_album_as_their_own()
    {
        List<Track> tracks = [new() { Id = "loose", Name = "loose" }, .. FiveArtists().Take(5)];

        var order = Shuffling.Albums(tracks, new Random(0));

        order.Should().BeEquivalentTo(tracks);
        order.Where(t => t.AlbumId is not null).Select(t => t.TrackNumber).Should().Equal(1, 2, 3, 4, 5);
    }
}
