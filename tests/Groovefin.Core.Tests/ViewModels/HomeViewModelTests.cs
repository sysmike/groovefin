using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.History;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class HomeViewModelTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly ICoverColours _colours = Substitute.For<ICoverColours>();
    private readonly InMemorySettingsService _settings = new();
    private readonly IPlayLog _history = Substitute.For<IPlayLog>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

    public HomeViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => $"[{call.Arg<string>()}]");
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => Page(call.Arg<AlbumQuery>().SortBy.ToString(), 3));
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
        History();
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private void History(params (string TrackId, DateTimeOffset At, PlaySource Source, PlayedTrack? Track)[] plays) =>
        _history.GetLogAsync().Returns(new PlayLog(
            Now.AddYears(-2),
            [.. plays.Select(p => new PlayRecord(p.TrackId, p.At, p.Source))],
            plays.Where(p => p.Track is not null).DistinctBy(p => p.TrackId).ToDictionary(p => p.TrackId, p => p.Track!, StringComparer.Ordinal)));

    private void Rows(params HomeShelfKind[] shown) =>
        _settings.Update(s => s with
        {
            Appearance = s.Appearance with
            {
                HomeRows = [.. Enum.GetValues<HomeShelfKind>().Select(k => new HomeRowSettings { Key = k.ToString(), IsVisible = shown.Contains(k) })],
            },
        });

    private static PagedResult<Album> Page(string prefix, int count)
    {
        var items = Enumerable.Range(0, count)
            .Select(i => new Album { Id = $"{prefix}-{i}", Name = $"{prefix} {i}", PrimaryImageTag = "tag" })
            .ToList();
        return new PagedResult<Album>(items, count, 0);
    }

    private void FailFor(HomeShelfKind kind, Exception exception) =>
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q == HomeViewModel.QueryFor(kind, Now)), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Album>>(exception));

    private HomeViewModel CreateSut() =>
        new(_library, _navigation, _errors, _strings, _player, new FavouritesService(_library, new WeakReferenceMessenger(), _errors), _colours, _settings, _history, _time)
        {
            Pick = _ => 0,
        };

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

        // By default the two rows the page always had; the others are shown in Settings (#394).
        sut.Shelves.Select(s => s.Kind).Should().Equal(HomeShelfKind.RecentlyPlayed, HomeShelfKind.RecentlyAdded);
        sut.Shelves.Select(s => s.Title).Should().Equal("[HomeShelf_RecentlyPlayed]", "[HomeShelf_RecentlyAdded]");
        sut.Shelves.Should().OnlyContain(s => !s.IsLoaded && !s.IsEmpty);
    }

    [Theory]
    [InlineData(HomeShelfKind.RecentlyPlayed, AlbumSortField.DatePlayed, true)]
    [InlineData(HomeShelfKind.RecentlyAdded, AlbumSortField.DateAdded, false)]
    public void Each_shelf_has_its_query(HomeShelfKind kind, AlbumSortField sortBy, bool playedOnly)
    {
        // Played albums by their songs' plays: the library service works that out (#369).
        HomeViewModel.QueryFor(kind, Now).Should().BeEquivalentTo(new
        {
            StartIndex = 0,
            Limit = HomeViewModel.ShelfSize,
            SortBy = sortBy,
            Descending = true,
            PlayedOnly = playedOnly,
            FavouritesOnly = false,
        });
    }

    [Fact]
    public async Task Empty_shelf_reports_empty()
    {
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.PlayedOnly), Arg.Any<CancellationToken>()).Returns(Page("none", 0));
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Shelves[0].IsEmpty.Should().BeTrue();
        sut.Shelves.Count(s => s.IsEmpty).Should().Be(1);
    }

    [Fact]
    public async Task Refresh_fills_every_shelf()
    {
        using var sut = CreateSut();
        sut.DisplayScale = 2;

        await sut.RefreshAsync();

        sut.IsLoading.Should().BeFalse();
        sut.Shelves.Should().OnlyContain(s => s.IsLoaded && !s.IsLoading && !s.ShowsProgress && !s.LoadFailed && s.Albums.Count == 3);
        sut.Shelves[0].Albums[0].Album.Id.Should().Be("DatePlayed-0");
        sut.Shelves[1].Albums[0].Album.Id.Should().Be("DateAdded-0");
        sut.Shelves[1].Albums[0].Cover!.Width.Should().Be((int)Math.Ceiling(HomeViewModel.TileWidth * 2));
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task A_shelf_shows_as_many_albums_as_fit()
    {
        // A row of as many tiles as fit in the window, no sideways scrolling (#376).
        using var sut = CreateSut();
        await sut.RefreshAsync();
        var shelf = sut.Shelves[1];

        shelf.Fit(2);
        shelf.Albums.Select(a => a.Album.Id).Should().Equal("DateAdded-0", "DateAdded-1");

        shelf.Fit(5);
        shelf.Albums.Should().HaveCount(3, "only three were loaded");

        shelf.Fit(0);
        shelf.Albums.Should().ContainSingle();
        shelf.HasAlbums.Should().BeTrue();
    }

    [Fact]
    public async Task A_refresh_keeps_the_number_that_fits()
    {
        using var sut = CreateSut();
        sut.Shelves[0].Fit(2);

        await sut.RefreshAsync();

        sut.Shelves[0].Albums.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(100, 1)]
    [InlineData(152, 1)]
    [InlineData(320, 1)]
    [InlineData(321, 2)]
    [InlineData(1000, 6)]
    public void Tiles_that_fit_count_the_space_between_them(double width, int tiles)
    {
        HomeShelf.TilesThatFit(width).Should().Be(tiles);
    }

    [Theory]
    [InlineData(HomeShelfKind.RecentlyPlayed, AlbumSortField.DatePlayed)]
    [InlineData(HomeShelfKind.RecentlyAdded, AlbumSortField.DateAdded)]
    public void Show_all_opens_the_albums_page_in_the_shelfs_order(HomeShelfKind kind, AlbumSortField sortBy)
    {
        using var sut = CreateSut();

        sut.ShowAllCommand.Execute(sut.Shelves.Single(s => s.Kind == kind));

        _navigation.Received(1).NavigateTo(PageKeys.Albums, new AlbumsOrder(sortBy, Descending: true));
    }

    [Fact]
    public async Task Show_all_is_named_after_its_shelf()
    {
        _strings.GetString("HomeShelfShowAll").Returns("Show all: {0}");
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.PlayedOnly), Arg.Any<CancellationToken>()).Returns(Page("none", 0));
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Shelves.Select(s => s.ShowAllName).Should().Equal("Show all: [HomeShelf_RecentlyPlayed]", "Show all: [HomeShelf_RecentlyAdded]");

        // Nothing played yet: the empty row has no link.
        sut.Shelves.Select(s => s.HasAlbums).Should().Equal(false, true);
    }

    [Fact]
    public async Task Failed_shelves_show_an_error_and_are_reported_once()
    {
        var first = new JellyfinException("boom");
        FailFor(HomeShelfKind.RecentlyPlayed, first);
        FailFor(HomeShelfKind.RecentlyAdded, new JellyfinException("boom again"));
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
        FailFor(HomeShelfKind.RecentlyPlayed, new JellyfinException("boom"));
        using var sut = CreateSut();
        await sut.RefreshAsync();

        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => Page(call.Arg<AlbumQuery>().SortBy.ToString(), 2));
        await sut.RefreshAsync();

        sut.Shelves.Should().OnlyContain(s => !s.LoadFailed && s.Albums.Count == 2);
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
        sut.Shelves.Should().OnlyContain(s => s.Albums.Count == 1 && s.Albums[0].Album.Id == "fresh-0" && !s.IsLoading);
        sut.Highlight!.Album.Id.Should().Be("fresh-0");
    }

    [Fact]
    public async Task No_connection_is_reported_not_thrown()
    {
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Album>>(new InvalidOperationException("Not connected.")));
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

    [Fact]
    public void The_rows_are_those_shown_in_settings_in_their_order()
    {
        _settings.Update(s => s with
        {
            Appearance = s.Appearance with
            {
                HomeRows =
                [
                    new HomeRowSettings { Key = nameof(HomeShelfKind.MostPlayed) },
                    new HomeRowSettings { Key = nameof(HomeShelfKind.RecentlyAdded), IsVisible = false },
                ],
            },
        });
        using var sut = CreateSut();

        // Rows not saved follow, as by default: Recently played shown, the rest hidden.
        sut.Shelves.Select(s => s.Kind).Should().Equal(HomeShelfKind.MostPlayed, HomeShelfKind.RecentlyPlayed);
    }

    [Fact]
    public void The_server_rows_have_their_queries()
    {
        HomeViewModel.QueryFor(HomeShelfKind.MostPlayed, Now).Should().BeEquivalentTo(new { SortBy = AlbumSortField.PlayCount, Descending = true, PlayedOnly = true });
        HomeViewModel.QueryFor(HomeShelfKind.FavouriteAlbums, Now).Should().BeEquivalentTo(new { SortBy = AlbumSortField.Random, FavouritesOnly = true });

        // A year before today's start, the same all day.
        HomeViewModel.QueryFor(HomeShelfKind.NotPlayedInAYear, Now).Should().BeEquivalentTo(new
        {
            SortBy = AlbumSortField.DatePlayed,
            Descending = true,
            PlayedBefore = (DateTimeOffset?)new DateTimeOffset(2025, 11, 9, 0, 0, 0, TimeSpan.Zero),
        });
    }

    [Fact]
    public void Show_all_of_the_server_rows_opens_the_albums_page_like_the_row()
    {
        Rows(HomeShelfKind.MostPlayed, HomeShelfKind.FavouriteAlbums, HomeShelfKind.NotPlayedInAYear);
        using var sut = CreateSut();

        foreach (var shelf in sut.Shelves)
        {
            sut.ShowAllCommand.Execute(shelf);
        }

        _navigation.Received(1).NavigateTo(PageKeys.Albums, new AlbumsOrder(AlbumSortField.PlayCount, Descending: true));
        _navigation.Received(1).NavigateTo(PageKeys.Albums, new AlbumsOrder(AlbumSortField.Name, Descending: false) { FavouritesOnly = true });
        _navigation.Received(1).NavigateTo(PageKeys.Albums, new AlbumsOrder(AlbumSortField.DatePlayed, Descending: true)
        {
            Selection = new AlbumSelection("[HomeShelf_NotPlayedInAYear]") { PlayedBefore = HomeViewModel.YearAgo(Now) },
        });
    }

    [Fact]
    public async Task A_random_artist_comes_from_the_play_history_and_shows_all_on_its_page()
    {
        _strings.GetString("HomeShelf_MoreBy").Returns("More by {0}");
        History(("t1", Now.AddDays(-2), PlaySource.Groovefin, new PlayedTrack("al1", ["ar1"], TimeSpan.FromMinutes(3))));
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.AlbumArtistId == "ar1"), Arg.Any<CancellationToken>()).Returns(Page("ar1", 2));
        _library.GetArtistAsync("ar1", Arg.Any<CancellationToken>()).Returns(new Artist { Id = "ar1", Name = "Daft Punk" });
        Rows(HomeShelfKind.RandomArtist);
        using var sut = CreateSut();

        await sut.RefreshAsync();

        var shelf = sut.Shelves.Single();
        shelf.Title.Should().Be("More by Daft Punk");
        shelf.Albums.Select(a => a.Album.Id).Should().Equal("ar1-0", "ar1-1");
        sut.ShowAllCommand.Execute(shelf);
        _navigation.Received(1).NavigateTo(PageKeys.Artist, "ar1");
        await _library.DidNotReceiveWithAnyArgs().GetArtistsAsync(default!, default);
    }

    [Fact]
    public async Task Without_a_play_history_the_random_artist_is_any_artist()
    {
        _strings.GetString("HomeShelf_MoreBy").Returns("More by {0}");
        _library.GetArtistsAsync(Arg.Is<ArtistQuery>(q => q.SortBy == ArtistSortField.Random), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Artist>([new Artist { Id = "ar9", Name = "Air" }], 1, 0));
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.AlbumArtistId == "ar9"), Arg.Any<CancellationToken>()).Returns(Page("ar9", 1));
        Rows(HomeShelfKind.RandomArtist);
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Shelves.Single().Title.Should().Be("More by Air");
        sut.Shelves.Single().Albums.Should().ContainSingle();
    }

    [Fact]
    public async Task This_week_last_year_ranks_the_albums_played_most_then()
    {
        var day = Now.AddYears(-1);
        History(
            ("t1", day, PlaySource.Groovefin, new PlayedTrack("al1", [], TimeSpan.Zero)),
            ("t2", day.AddDays(1), PlaySource.Server, null),
            ("t2", day.AddDays(-2), PlaySource.Groovefin, null),
            ("t3", day.AddDays(-10), PlaySource.Groovefin, new PlayedTrack("al3", [], TimeSpan.Zero)),
            ("t4", day, PlaySource.Seeded, new PlayedTrack("al4", [], TimeSpan.Zero)));

        // t2 was played before the history kept albums: the server knows its album.
        _library.GetTracksByIdAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new Track { Id = "t2", Name = "Two", AlbumId = "al2" }]);
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.Ids.Count > 0), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Album>([new Album { Id = "al1", Name = "One" }, new Album { Id = "al2", Name = "Two" }], 2, 0));
        Rows(HomeShelfKind.ThisWeekLastYear);
        using var sut = CreateSut();

        await sut.RefreshAsync();

        var shelf = sut.Shelves.Single();
        shelf.Albums.Select(a => a.Album.Id).Should().Equal("al2", "al1");
        await _library.Received(1).GetTracksByIdAsync(Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "t2" })), Arg.Any<CancellationToken>());

        sut.ShowAllCommand.Execute(shelf);
        _navigation.Received(1).NavigateTo(PageKeys.Albums, Arg.Is<AlbumsOrder>(o => o.Selection!.Ids.SequenceEqual(new[] { "al2", "al1" }) && o.Selection.Title == "[HomeShelf_ThisWeekLastYear]"));
    }

    [Fact]
    public async Task This_week_last_year_without_plays_then_says_how_it_fills()
    {
        Rows(HomeShelfKind.ThisWeekLastYear);
        using var sut = CreateSut();

        await sut.RefreshAsync();

        var shelf = sut.Shelves.Single();
        shelf.IsEmpty.Should().BeTrue();
        shelf.EmptyText.Should().Be("[HomeShelfEmpty_ThisWeekLastYear]");
        await _library.DidNotReceive().GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.Ids.Count > 0), Arg.Any<CancellationToken>());
    }
}
