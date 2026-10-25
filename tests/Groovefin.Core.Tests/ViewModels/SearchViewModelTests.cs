using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class SearchViewModelTests
{
    private readonly PlayingTrack _playing = new();
    private readonly ISearchService _search = Substitute.For<ISearchService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly SearchViewModel _sut;

    public SearchViewModelTests()
    {
        _strings.GetString("Search_Results").Returns("{0} results for {1}");
        _strings.GetString("Search_OneResult").Returns("{0} result for {1}");
        _strings.GetString("Search_NoResults").Returns("Nothing for {0}");
        _strings.GetString("Album_TrackCount").Returns("{0} tracks");
        _strings.GetString("Album_TrackCountOne").Returns("{0} track");
        _sut = new SearchViewModel(_search, _player, _navigation, _errors, _strings, _playing.Current);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Artist DaftPunk = new() { Id = "ar1", Name = "Daft Punk", PrimaryImageTag = "a" };
    private static readonly Album Discovery = new() { Id = "al1", Name = "Discovery", AlbumArtist = "Daft Punk", PrimaryImageTag = "b" };

    private static Track Song(string id) => new()
    {
        Id = id,
        Name = $"Song {id}",
        AlbumName = "Discovery",
        Artists = [new ArtistRef("ar1", "Daft Punk")],
        Duration = TimeSpan.FromSeconds(301),
    };

    private void Returns(SearchResults results) =>
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(results);

    [Fact]
    public async Task Shows_the_matches_grouped_by_kind()
    {
        Returns(new SearchResults(
            [DaftPunk],
            [Discovery],
            [Song("t1"), Song("t2")],
            [new Playlist { Id = "p1", Name = "Disco", TrackCount = 12, Duration = TimeSpan.FromMinutes(62) }]));

        await _sut.LoadAsync("  disco ", Ct);

        await _search.Received(1).SearchAsync("disco", SearchViewModel.Limit, Arg.Any<CancellationToken>());
        _sut.Query.Should().Be("disco");
        _sut.Artists.Single().Name.Should().Be("Daft Punk");
        _sut.Albums.Single().Name.Should().Be("Discovery");
        _sut.Tracks.Select(t => t.Name).Should().Equal("Song t1", "Song t2");
        _sut.Playlists.Single().Details.Should().Be("12 tracks · 1:02:00");
        (_sut.HasArtists, _sut.HasAlbums, _sut.HasTracks, _sut.HasPlaylists).Should().Be((true, true, true, true));
        _sut.Summary.Should().Be("5 results for disco");
        _sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void Track_rows_describe_artists_album_and_length()
    {
        var row = new SearchTrackItem(Song("t1"), 0);

        row.Details.Should().Be("Daft Punk · Discovery");
        row.Duration.Should().Be("5:01");
        row.AccessibleName.Should().Be("Song t1, Daft Punk · Discovery, 5:01");
    }

    [Fact]
    public async Task One_result_and_none_have_their_own_summary()
    {
        Returns(new SearchResults([DaftPunk], [], [], []));
        await _sut.LoadAsync("daft", Ct);
        _sut.Summary.Should().Be("1 result for daft");
        (_sut.HasAlbums, _sut.HasTracks, _sut.HasPlaylists).Should().Be((false, false, false));

        Returns(SearchResults.Empty);
        await _sut.LoadAsync("zzz", Ct);
        _sut.Summary.Should().Be("Nothing for zzz");
        _sut.HasArtists.Should().BeFalse();
    }

    [Fact]
    public async Task Playlist_details_show_what_is_known()
    {
        Returns(new SearchResults([], [], [], [new Playlist { Id = "p1", Name = "One", TrackCount = 1 }, new Playlist { Id = "p2", Name = "Bare" }]));

        await _sut.LoadAsync("x", Ct);

        _sut.Playlists.Select(p => p.Details).Should().Equal("1 track", string.Empty);
        _sut.Playlists[1].AccessibleName.Should().Be("Bare");
    }

    [Fact]
    public async Task A_failed_search_is_reported_and_clears_the_results()
    {
        Returns(new SearchResults([DaftPunk], [], [], []));
        await _sut.LoadAsync("daft", Ct);
        var failure = new JellyfinException("boom");
        _search.SearchAsync(default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs<SearchResults>(_ => throw failure);

        await _sut.LoadAsync("daft", Ct);

        _sut.LoadFailed.Should().BeTrue();
        _sut.Artists.Should().BeEmpty();
        _sut.Summary.Should().BeEmpty();
        _errors.Received(1).Report(failure, Operations.Search);
    }

    [Fact]
    public async Task Leaving_the_page_cancels_quietly()
    {
        _search.SearchAsync(default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs<SearchResults>(_ => throw new OperationCanceledException());

        await _sut.LoadAsync("daft", Ct);

        _sut.LoadFailed.Should().BeFalse();
        _sut.IsLoading.Should().BeFalse();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Rows_open_artists_and_albums_and_play_tracks_from_the_clicked_one()
    {
        Returns(new SearchResults([DaftPunk], [Discovery], [Song("t1"), Song("t2"), Song("t3")], [new Playlist { Id = "p1", Name = "Disco" }]));
        await _sut.LoadAsync("d", Ct);

        _sut.OpenPlaylistCommand.Execute(_sut.Playlists[0]);
        _sut.OpenArtistCommand.Execute(_sut.Artists[0]);
        _sut.OpenAlbumCommand.Execute(_sut.Albums[0]);
        _sut.PlayTrackCommand.Execute(_sut.Tracks[1]);

        _navigation.Received(1).NavigateTo(PageKeys.Artist, "ar1");
        _navigation.Received(1).NavigateTo(PageKeys.Album, "al1");
        _navigation.Received(1).NavigateTo(PageKeys.Playlist, "p1");
        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Select(x => x.Id).SequenceEqual(new[] { "t1", "t2", "t3" })), 1);
    }

    [Fact]
    public async Task The_playing_song_among_the_matches_is_marked()
    {
        Returns(new SearchResults([], [], [Song("t1"), Song("t2")], []));
        _playing.Play("t1");

        await _sut.LoadAsync("disco", Ct);

        _sut.Tracks.Select(t => t.IsCurrent).Should().Equal(true, false);
    }
}
