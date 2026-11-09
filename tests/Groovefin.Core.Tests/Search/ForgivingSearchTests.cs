using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Search;

namespace Groovefin.Core.Tests.Search;

public sealed class ForgivingSearchTests
{
    private static readonly Artist Adf = new() { Id = "ar1", Name = "Asian Dub Foundation" };
    private static readonly Artist DubSyndicate = new() { Id = "ar2", Name = "Dub Syndicate" };

    private readonly ISearchService _inner = Substitute.For<ISearchService>();
    private readonly ForgivingSearch _sut;

    public ForgivingSearchTests()
    {
        _inner.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(SearchResults.Empty);
        _sut = new ForgivingSearch(_inner);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Finds(string term, SearchResults results) =>
        _inner.SearchAsync(term, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(results);

    private static SearchResults Artists(params Artist[] artists) => new(artists, [], [], []);

    private static Track Recording(string id, string album, int seconds = 200) => new()
    {
        Id = id,
        Name = "Fortress Europe",
        AlbumName = album,
        Artists = [new ArtistRef("ar1", "Asian Dub Foundation")],
        Duration = TimeSpan.FromSeconds(seconds),
    };

    [Fact]
    public async Task What_the_server_finds_is_kept_as_it_is()
    {
        Finds("dub", Artists(Adf, DubSyndicate));

        var results = await _sut.SearchAsync("dub", 5, Ct);

        results.Artists.Should().Equal(Adf, DubSyndicate);
        await _inner.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Words_in_another_order_are_searched_one_by_one_and_must_all_match()
    {
        Finds("dub", Artists(DubSyndicate, Adf));
        Finds("foundation", Artists(Adf));

        var results = await _sut.SearchAsync("dub foundation asian", 5, Ct);

        results.Artists.Should().Equal(Adf);
    }

    [Fact]
    public async Task A_typo_is_forgiven()
    {
        Finds("dub", Artists(DubSyndicate, Adf));

        var results = await _sut.SearchAsync("asain dub", 5, Ct);

        results.Artists.Should().Equal(Adf);
        await _inner.Received(1).SearchAsync("asa", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task One_short_word_that_finds_nothing_is_not_searched_again()
    {
        var results = await _sut.SearchAsync("xyz", 5, Ct);

        results.Count.Should().Be(0);
        await _inner.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_same_recording_comes_once_and_others_on_other_albums_stay()
    {
        Finds("fortress europe", new SearchResults([], [], [Recording("t1", "Rafi's Revenge"), Recording("t2", "Rafi's Revenge"), Recording("t3", "Live in Paris", 260)], []));

        var results = await _sut.SearchAsync("fortress europe", 5, Ct);

        results.Tracks.Select(t => t.Id).Should().Equal("t1", "t3");
    }

    [Fact]
    public async Task Genres_are_ranked_like_the_rest()
    {
        Finds("indie", new SearchResults([], [], [], []) { Genres = [new Genre("g1", "Rock"), new Genre("g2", "Indie Rock"), new Genre("g3", "Indie Pop")] });

        var results = await _sut.SearchAsync("rokc indie", 5, Ct);

        results.Genres.Select(g => g.Name).Should().Equal("Indie Rock");
    }

    [Theory]
    [InlineData("asian", "asain", 1)]
    [InlineData("foundation", "foundatoin", 1)]
    [InlineData("dub", "dub", 0)]
    [InlineData("kitten", "sitting", 3)]
    public void The_distance_counts_swapped_letters_as_one(string a, string b, int distance) =>
        ForgivingSearch.Distance(a, b).Should().Be(distance);

    [Fact]
    public void Words_are_folded_to_lower_case_without_accents()
    {
        ForgivingSearch.Words("Café Tacvba / Beyoncé").Should().Equal("cafe", "tacvba", "beyonce");
        ForgivingSearch.Score(["beyon"], ["Beyoncé"]).Should().Be(1, "a word's start matches");
        ForgivingSearch.Score(["dub"], ["Asian Dub Foundation"]).Should().Be(1);
        ForgivingSearch.Score(["dab"], ["Asian Dub Foundation"]).Should().Be(-1, "three letters must match exactly");
    }
}
