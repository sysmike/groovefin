using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class HomeViewModelTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly ICoverColours _colours = Substitute.For<ICoverColours>();

    public HomeViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => $"[{call.Arg<string>()}]");
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => Page(call.Arg<AlbumQuery>().SortBy.ToString(), 3));
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.Ids.Count > 0), Arg.Any<CancellationToken>())
            .Returns(call => ById(call.Arg<AlbumQuery>().Ids));
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Track>([Played("t1", "a"), Played("t2", "b")], 2, 0));
        _library.GetAlbumAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new Album
            {
                Id = call.Arg<string>(),
                Name = $"{call.Arg<string>()} in full",
                PrimaryImageTag = "tag",
                Year = 2001,
                Genres = [new Genre("g1", "House")],
            });
        _library.GetAlbumTracksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([new Track { Id = "song", Name = "Song", Duration = TimeSpan.FromMinutes(3) }]);
        _colours.GetAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>()).Returns(new CoverColour(20, 40, 80));
    }

    private static PagedResult<Album> Page(string prefix, int count)
    {
        var items = Enumerable.Range(0, count)
            .Select(i => new Album { Id = $"{prefix}-{i}", Name = $"{prefix} {i}", PrimaryImageTag = "tag" })
            .ToList();
        return new PagedResult<Album>(items, count, 0);
    }

    /// <summary>The server sends albums asked for by ID in its own order: here, reversed.</summary>
    private static PagedResult<Album> ById(IReadOnlyList<string> ids)
    {
        var items = ids.Reverse().Select(id => new Album { Id = id, Name = id }).ToList();
        return new PagedResult<Album>(items, items.Count, 0);
    }

    private static Track Played(string id, string albumId, int minutesAgo = 0) => new()
    {
        Id = id,
        Name = id,
        AlbumId = albumId,
        LastPlayed = DateTimeOffset.UnixEpoch.AddDays(1).AddMinutes(-minutesAgo),
    };

    private HomeViewModel CreateSut() =>
        new(_library, _navigation, _errors, _strings, _player, new FavouritesService(_library, new WeakReferenceMessenger(), _errors), _colours);

    [Fact]
    public void Disposing_twice_does_not_throw()
    {
        // The page disposes it when the user leaves; the container disposes it again at shutdown (#145).
        var sut = CreateSut();
        sut.Dispose();

        var act = sut.Dispose;

        act.Should().NotThrow();
    }

    [Fact]
    public void Shelves_are_recently_played_then_recently_added()
    {
        using var sut = CreateSut();

        // Most played, random and favourite albums have pages of their own (#367).
        sut.Shelves.Select(s => s.Kind).Should().Equal(HomeShelfKind.RecentlyPlayed, HomeShelfKind.RecentlyAdded);
        sut.Shelves.Select(s => s.Title).Should().Equal("[HomeShelf_RecentlyPlayed]", "[HomeShelf_RecentlyAdded]");
        sut.Shelves.Should().OnlyContain(s => !s.IsLoaded && !s.IsEmpty);
    }

    [Fact]
    public void Recently_added_and_recent_songs_have_their_queries()
    {
        HomeViewModel.RecentlyAddedQuery.Should().BeEquivalentTo(new
        {
            StartIndex = 0,
            Limit = HomeViewModel.ShelfSize,
            SortBy = AlbumSortField.DateAdded,
            Descending = true,
            PlayedOnly = false,
        });
        HomeViewModel.RecentTracksQuery.Should().BeEquivalentTo(new
        {
            Limit = HomeViewModel.RecentTrackCount,
            SortBy = TrackSortField.DatePlayed,
            Descending = true,
        });
    }

    [Fact]
    public void Recent_album_ids_follow_the_songs_and_skip_unplayed_ones()
    {
        // Songs the user never played come last in the server's order; they don't count.
        Track[] songs =
        [
            Played("t1", "a"),
            Played("t2", "b", 1),
            Played("t3", "a", 2),
            new() { Id = "t4", Name = "t4", AlbumId = "c" },
            Played("t5", null!, 3),
        ];

        HomeViewModel.RecentAlbumIds(songs).Should().Equal("a", "b");
    }

    [Fact]
    public void Recent_album_ids_stop_at_a_shelf()
    {
        var songs = Enumerable.Range(0, HomeViewModel.ShelfSize + 5).Select(i => Played($"t{i}", $"album{i}", i));

        HomeViewModel.RecentAlbumIds(songs).Should().HaveCount(HomeViewModel.ShelfSize);
    }

    [Fact]
    public async Task Recently_played_shows_the_albums_of_the_last_played_songs_in_that_order()
    {
        using var sut = CreateSut();

        await sut.RefreshAsync();

        // Not the albums sorted by their own play date: Jellyfin keeps that on songs (#367).
        sut.Shelves[0].Albums.Select(a => a.Album.Id).Should().Equal("a", "b");
        await _library.Received(1).GetAlbumsAsync(
            Arg.Is<AlbumQuery>(q => q.Ids.SequenceEqual(new[] { "a", "b" }) && q.Limit == 2), Arg.Any<CancellationToken>());
        await _library.DidNotReceive().GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.SortBy == AlbumSortField.DatePlayed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recently_played_is_empty_without_played_songs()
    {
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Track>([new Track { Id = "t1", Name = "t1", AlbumId = "a" }], 1, 0));
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Shelves[0].IsEmpty.Should().BeTrue();
        await _library.DidNotReceive().GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.Ids.Count > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_fills_every_shelf()
    {
        using var sut = CreateSut();
        sut.DisplayScale = 2;

        await sut.RefreshAsync();

        sut.IsLoading.Should().BeFalse();
        sut.Shelves.Should().OnlyContain(s => s.IsLoaded && !s.IsLoading && !s.ShowsProgress && !s.LoadFailed && s.Albums.Count > 0);
        sut.Shelves[1].Albums[0].Album.Id.Should().Be("DateAdded-0");
        sut.Shelves[1].Albums[0].Cover!.Width.Should().Be((int)Math.Ceiling(HomeViewModel.TileWidth * 2));
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Failed_shelves_show_an_error_and_are_reported_once()
    {
        var first = new JellyfinException("boom");
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<PagedResult<Track>>(first));
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q == HomeViewModel.RecentlyAddedQuery), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Album>>(new JellyfinException("boom again")));
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Shelves.Should().OnlyContain(s => s.LoadFailed && !s.IsLoading && !s.IsEmpty);
        sut.HasHighlight.Should().BeTrue();
        _errors.Received(1).Report(Arg.Any<Exception>(), Operations.LoadHome);
        _errors.Received(1).Report(first, Operations.LoadHome);
    }

    [Fact]
    public async Task Refresh_after_a_failure_clears_the_error()
    {
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Track>>(new JellyfinException("boom")));
        using var sut = CreateSut();
        await sut.RefreshAsync();

        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Track>([Played("t1", "a")], 1, 0));
        await sut.RefreshAsync();

        sut.Shelves.Should().OnlyContain(s => !s.LoadFailed && s.Albums.Count > 0);
    }

    [Fact]
    public async Task A_refresh_cancels_the_running_load_and_drops_its_results()
    {
        var slow = new TaskCompletionSource<PagedResult<Album>>();
        CancellationToken firstToken = default;
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                firstToken = call.Arg<CancellationToken>();
                return slow.Task;
            });
        using var sut = CreateSut();
        var first = sut.RefreshAsync();
        sut.IsLoading.Should().BeTrue();
        sut.Shelves.Should().OnlyContain(s => s.ShowsProgress);

        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => Page("fresh", 1));
        await sut.RefreshAsync();
        slow.SetResult(Page("stale", 5));
        await first;

        firstToken.IsCancellationRequested.Should().BeTrue();
        sut.IsLoading.Should().BeFalse();
        sut.Shelves[1].Albums.Select(a => a.Album.Id).Should().Equal("fresh-0");
        sut.Highlight!.Album.Id.Should().Be("fresh-0");
        sut.Shelves.Should().OnlyContain(s => !s.IsLoading);
    }

    [Fact]
    public async Task No_connection_is_reported_not_thrown()
    {
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Album>>(new InvalidOperationException("Not connected.")));
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Track>>(new InvalidOperationException("Not connected.")));
        using var sut = CreateSut();

        var act = () => sut.RefreshAsync();

        await act.Should().NotThrowAsync();
        sut.Shelves.Should().OnlyContain(s => s.LoadFailed && !s.IsEmpty);
        sut.HasHighlight.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<InvalidOperationException>(), Operations.LoadHome);
    }

    [Fact]
    public async Task Opening_an_album_navigates_to_its_detail_page()
    {
        using var sut = CreateSut();
        await sut.RefreshAsync();

        sut.OpenAlbumCommand.Execute(sut.Shelves[1].Albums[1]);

        _navigation.Received(1).NavigateTo(PageKeys.Album, "DateAdded-1");
    }

    [Fact]
    public async Task The_highlight_is_a_random_album_with_its_details_songs_and_colour()
    {
        using var sut = CreateSut();
        sut.DisplayScale = 1.5;

        await sut.RefreshAsync();

        var highlight = sut.Highlight!;
        await _library.Received(1).GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q == HomeViewModel.HighlightQuery), Arg.Any<CancellationToken>());
        HomeViewModel.HighlightQuery.SortBy.Should().Be(AlbumSortField.Random);
        highlight.Album.Id.Should().Be("Random-0");

        // The list has no genres: the album itself is loaded.
        highlight.Name.Should().Be("Random-0 in full");
        highlight.Genres.Select(g => g.Name).Should().Equal("House");
        highlight.HasGenres.Should().BeTrue();
        highlight.Details.Should().StartWith("2001 · ");
        highlight.Tracks.Select(t => t.Id).Should().Equal("song");
        highlight.Cover!.Width.Should().Be((int)Math.Ceiling(HomeViewModel.HighlightCoverWidth * 1.5));
        highlight.Colour.Should().Be(new CoverColour(20, 40, 80).ForWhiteText());
        highlight.PlayName.Should().Be("[Cover_Play]");
        highlight.ShuffleName.Should().Be("[HomeHighlight_Shuffle]");
    }

    [Fact]
    public async Task The_highlight_colour_is_darkened_for_white_text()
    {
        _colours.GetAsync(Arg.Any<ImageRequest>(), Arg.Any<CancellationToken>()).Returns(new CoverColour(250, 230, 120));
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Highlight!.Colour!.Value.Luminance.Should().BeLessThanOrEqualTo(CoverColour.MaxLuminance);
        sut.Highlight.Colour.Should().Be(new CoverColour(250, 230, 120).ForWhiteText());
    }

    [Fact]
    public async Task An_album_without_a_cover_has_no_colour()
    {
        _library.GetAlbumAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new Album { Id = "Random-0", Name = "Plain" });
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Highlight!.Cover.Should().BeNull();
        sut.Highlight.Colour.Should().BeNull();
        await _colours.DidNotReceiveWithAnyArgs().GetAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_empty_library_has_no_highlight()
    {
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q == HomeViewModel.HighlightQuery), Arg.Any<CancellationToken>())
            .Returns(Page("none", 0));
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.HasHighlight.Should().BeFalse();
        sut.PlayHighlightCommand.CanExecute(null).Should().BeFalse();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Play_and_shuffle_use_the_highlight_songs()
    {
        using var sut = CreateSut();
        await sut.RefreshAsync();

        sut.PlayHighlightCommand.Execute(null);
        sut.ShuffleHighlightCommand.Execute(null);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Single().Id == "song"));
        _player.Received(1).Shuffle(Arg.Is<IReadOnlyList<Track>>(t => t.Single().Id == "song"));
    }

    [Fact]
    public async Task An_album_without_songs_cannot_be_played()
    {
        _library.GetAlbumTracksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.PlayHighlightCommand.CanExecute(null).Should().BeFalse();
        sut.ShuffleHighlightCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Another_album_skips_the_one_shown()
    {
        using var sut = CreateSut();
        await sut.RefreshAsync();
        var shelves = sut.Shelves[1].Albums.ToList();

        await sut.ShowAnotherAlbumCommand.ExecuteAsync(null);

        // The random query gives Random-0 first again; the second album is taken instead.
        sut.Highlight!.Album.Id.Should().Be("Random-1");
        sut.Shelves[1].Albums.Should().Equal(shelves);
    }

    [Fact]
    public async Task A_failing_other_album_keeps_the_card_and_is_reported()
    {
        using var sut = CreateSut();
        await sut.RefreshAsync();
        var failure = new JellyfinException("boom");
        _library.GetAlbumTracksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<Track>>(failure));

        await sut.ShowAnotherAlbumCommand.ExecuteAsync(null);

        sut.Highlight!.Album.Id.Should().Be("Random-0");
        _errors.Received(1).Report(failure, Operations.LoadHome);
    }

    [Fact]
    public async Task The_highlight_heart_toggles_and_rolls_back_when_the_server_refuses()
    {
        using var sut = CreateSut();
        await sut.RefreshAsync();

        await sut.ToggleHighlightFavouriteCommand.ExecuteAsync(null);
        sut.Highlight!.IsFavourite.Should().BeTrue();
        await _library.Received(1).SetFavouriteAsync("Random-0", true, Arg.Any<CancellationToken>());

        _library.SetFavouriteAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new JellyfinException("boom")));
        await sut.ToggleHighlightFavouriteCommand.ExecuteAsync(null);
        sut.Highlight.IsFavourite.Should().BeTrue();
    }

    [Fact]
    public async Task The_highlight_opens_its_album_and_genres()
    {
        using var sut = CreateSut();
        await sut.RefreshAsync();

        sut.OpenHighlightCommand.Execute(null);
        sut.OpenGenreCommand.Execute(sut.Highlight!.Genres[0]);

        _navigation.Received(1).NavigateTo(PageKeys.Album, "Random-0");
        _navigation.Received(1).NavigateTo(PageKeys.Genre, "g1");
    }
}
