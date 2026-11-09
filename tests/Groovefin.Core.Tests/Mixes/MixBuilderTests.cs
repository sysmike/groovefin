using Groovefin.Core.Api;
using Groovefin.Core.Mixes;
using Groovefin.Core.Models;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Mixes;

public sealed class MixBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly List<TrackQuery> _queries = [];
    private readonly MixBuilder _sut;

    public MixBuilderTests() => _sut = new MixBuilder(_library, new FakeTimeProvider(Now));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track Song(string id, int plays = 0, int? playedDaysAgo = null, int addedDaysAgo = 400) => new()
    {
        Id = id,
        Name = id,
        PlayCount = plays,
        LastPlayed = playedDaysAgo is { } days ? Now.AddDays(-days) : null,
        DateAdded = Now.AddDays(-addedDaysAgo),
    };

    /// <summary>The library pages through <paramref name="songs"/> in order.</summary>
    private void Library(params Track[] songs) =>
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var query = call.Arg<TrackQuery>();
            _queries.Add(query);
            return new PagedResult<Track>([.. songs.Skip(query.StartIndex).Take(query.Limit)], songs.Length, query.StartIndex);
        });

    [Fact]
    public async Task The_server_gets_the_genre_years_favourites_and_order()
    {
        Library();

        await _sut.BuildAsync(new MixRules { GenreId = "g1", FromYear = 1990, ToYear = 1999, FavouritesOnly = true, Order = MixOrder.MostPlayed }, Ct);

        _queries.Should().ContainSingle().Which.Should().BeEquivalentTo(new TrackQuery
        {
            StartIndex = 0,
            Limit = MixBuilder.PageSize,
            SortBy = TrackSortField.PlayCount,
            Descending = true,
            GenreIds = ["g1"],
            MinYear = 1990,
            MaxYear = 1999,
            FavouritesOnly = true,
        });
    }

    [Theory]
    [InlineData(MixOrder.Random, TrackSortField.Random, false)]
    [InlineData(MixOrder.RecentlyAdded, TrackSortField.DateAdded, true)]
    [InlineData(MixOrder.LeastRecentlyPlayed, TrackSortField.DatePlayed, false)]
    public async Task Each_order_sorts_on_the_server(MixOrder order, TrackSortField sortBy, bool descending)
    {
        Library();

        await _sut.BuildAsync(new MixRules { Order = order }, Ct);

        _queries[0].SortBy.Should().Be(sortBy);
        _queries[0].Descending.Should().Be(descending);
    }

    [Fact]
    public async Task Plays_last_play_and_date_added_are_checked_here()
    {
        Library(
            Song("often-recent", plays: 9, playedDaysAgo: 3),
            Song("often-old", plays: 9, playedDaysAgo: 300),
            Song("never", plays: 0),
            Song("new-old", plays: 9, playedDaysAgo: 300, addedDaysAgo: 5));

        var notPlayed = await _sut.BuildAsync(new MixRules { MinPlays = 5, NotPlayedForDays = 182 }, Ct);
        var never = await _sut.BuildAsync(new MixRules { MaxPlays = 0 }, Ct);
        var added = await _sut.BuildAsync(new MixRules { AddedWithinDays = 30 }, Ct);

        notPlayed.Select(t => t.Id).Should().Equal("often-old", "new-old");
        never.Select(t => t.Id).Should().Equal("never");
        added.Select(t => t.Id).Should().Equal("new-old");
    }

    [Fact]
    public async Task Never_played_songs_count_as_not_played_for_a_while()
    {
        Library(Song("never"));

        var tracks = await _sut.BuildAsync(new MixRules { NotPlayedForDays = 30 }, Ct);

        tracks.Should().ContainSingle();
    }

    [Fact]
    public async Task It_reads_page_after_page_until_the_mix_is_full()
    {
        var songs = Enumerable.Range(0, MixBuilder.PageSize * 3).Select(i => Song($"s{i}", plays: i % 2)).ToArray();
        Library(songs);

        var tracks = await _sut.BuildAsync(new MixRules { MinPlays = 1, Size = 300 }, Ct);

        tracks.Should().HaveCount(300).And.OnlyContain(t => t.PlayCount == 1);
        _queries.Select(q => q.StartIndex).Should().Equal(0, MixBuilder.PageSize);
    }

    [Fact]
    public async Task It_stops_after_the_scan_limit_and_at_the_librarys_end()
    {
        Library([.. Enumerable.Range(0, MixBuilder.MaxScanned + MixBuilder.PageSize).Select(i => Song($"s{i}"))]);

        var rare = await _sut.BuildAsync(new MixRules { MinPlays = 1 }, Ct);

        rare.Should().BeEmpty();
        _queries.Should().HaveCount(MixBuilder.MaxScanned / MixBuilder.PageSize);

        _queries.Clear();
        Library(Song("a"), Song("b"));
        (await _sut.BuildAsync(new MixRules(), Ct)).Should().HaveCount(2);
        _queries.Should().ContainSingle();
    }

    [Fact]
    public async Task A_song_that_comes_again_is_taken_once()
    {
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Track>([Song("a"), Song("a"), Song("b")], 3, 0));

        var tracks = await _sut.BuildAsync(new MixRules(), Ct);

        tracks.Select(t => t.Id).Should().Equal("a", "b");
    }

    [Fact]
    public async Task The_size_is_kept_within_bounds()
    {
        Library([.. Enumerable.Range(0, 600).Select(i => Song($"s{i}"))]);

        (await _sut.BuildAsync(new MixRules { Size = 0 }, Ct)).Should().HaveCount(1);
        (await _sut.BuildAsync(new MixRules { Size = 10_000 }, Ct)).Should().HaveCount(MixRules.MaxSize);
    }
}
