using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class SearchSuggestionsViewModelTests : IDisposable
{
    private readonly ISearchService _search = Substitute.For<ISearchService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly FakeTimeProvider _time = new();
    private readonly SearchSuggestionsViewModel _sut;

    public SearchSuggestionsViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>().Replace("Search_Kind", string.Empty, StringComparison.Ordinal));
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Results);
        _sut = new SearchSuggestionsViewModel(_search, _player, _navigation, _strings, _time, NullLogger<SearchSuggestionsViewModel>.Instance);
    }

    public void Dispose() => _sut.Dispose();

    private static readonly Track DigitalLove = new() { Id = "t1", Name = "Digital Love", Artists = [new ArtistRef("ar1", "Daft Punk")] };

    private static readonly SearchResults Results = new(
        [new Artist { Id = "ar1", Name = "Daft Punk" }],
        [new Album { Id = "al1", Name = "Discovery", AlbumArtist = "Daft Punk" }, new Album { Id = "al2", Name = "Untitled" }],
        [DigitalLove],
        [new Playlist { Id = "p1", Name = "Ignored for now" }]);

    /// <summary>Starts an update and lets the debounce pass.</summary>
    private async Task TypeAsync(string text)
    {
        var update = _sut.UpdateAsync(text);
        _time.Advance(SearchSuggestionsViewModel.Debounce);
        await update;
    }

    [Fact]
    public async Task Typing_shows_artists_albums_and_tracks_after_the_debounce()
    {
        var update = _sut.UpdateAsync(" disco ");
        await _search.DidNotReceiveWithAnyArgs().SearchAsync(default!, default, TestContext.Current.CancellationToken);

        _time.Advance(SearchSuggestionsViewModel.Debounce);
        await update;

        await _search.Received(1).SearchAsync("disco", SearchSuggestionsViewModel.PerKind, Arg.Any<CancellationToken>());
        _sut.Suggestions.Select(s => (s.Kind, s.Title, s.Subtitle)).Should().Equal(
            (SuggestionKind.Artist, "Daft Punk", "Artist"),
            (SuggestionKind.Album, "Discovery", "Daft Punk"),
            (SuggestionKind.Album, "Untitled", string.Empty),
            (SuggestionKind.Track, "Digital Love", "Daft Punk"));
        _sut.Suggestions.Select(s => s.AccessibleName).Should().Equal(
            "Daft Punk, Artist", "Discovery, Album, Daft Punk", "Untitled, Album", "Digital Love, Track, Daft Punk");
        _sut.Suggestions[0].ToString().Should().Be("Daft Punk");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" d ")]
    [InlineData(null)]
    public async Task Short_texts_clear_the_suggestions_without_asking(string? text)
    {
        await TypeAsync("disco");

        await _sut.UpdateAsync(text!);

        _sut.Suggestions.Should().BeEmpty();
        await _search.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_newer_text_cancels_the_pending_one()
    {
        var first = _sut.UpdateAsync("dis");
        var second = _sut.UpdateAsync("disco");
        _time.Advance(SearchSuggestionsViewModel.Debounce);
        await Task.WhenAll(first, second);

        await _search.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _search.Received(1).SearchAsync("disco", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_answer_that_arrives_after_a_newer_text_is_dropped()
    {
        var slow = new TaskCompletionSource<SearchResults>();
        _search.SearchAsync("dis", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(slow.Task);
        var first = _sut.UpdateAsync("dis");
        _time.Advance(SearchSuggestionsViewModel.Debounce);

        _sut.Cancel();
        slow.SetResult(Results);
        await first;

        _sut.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task Failures_only_clear_the_list()
    {
        await TypeAsync("disco");
        _search.SearchAsync(default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs<SearchResults>(_ => throw new JellyfinException("boom"));

        await TypeAsync("discovery");

        _sut.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task Choosing_opens_artists_and_albums_and_plays_tracks()
    {
        await TypeAsync("disco");
        var artist = _sut.Suggestions[0];
        var album = _sut.Suggestions[1];
        var track = _sut.Suggestions[3];

        _sut.Choose(artist);
        _sut.Choose(album);
        _sut.Choose(track);

        _navigation.Received(1).NavigateTo(PageKeys.Artist, "ar1");
        _navigation.Received(1).NavigateTo(PageKeys.Album, "al1");
        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Single() == DigitalLove));
        _sut.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public void A_track_suggestion_without_its_track_does_nothing()
    {
        _sut.Choose(new SearchSuggestion(SuggestionKind.Track, "t1", "Lost", string.Empty));

        _player.DidNotReceiveWithAnyArgs().Play(default!);
    }
}
