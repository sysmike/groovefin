using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class HomeViewModelTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public HomeViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => $"[{call.Arg<string>()}]");
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => Page(call.Arg<AlbumQuery>().SortBy.ToString(), 3));
    }

    private static PagedResult<Album> Page(string prefix, int count)
    {
        var items = Enumerable.Range(0, count)
            .Select(i => new Album { Id = $"{prefix}-{i}", Name = $"{prefix} {i}", PrimaryImageTag = "tag" })
            .ToList();
        return new PagedResult<Album>(items, count, 0);
    }

    private HomeViewModel CreateSut() => new(_library, _navigation, _errors, _strings);

    [Fact]
    public void Disposing_twice_does_not_throw()
    {
        // The page disposes it when the user leaves; the container disposes it again at shutdown (#145).
        var sut = CreateSut();
        sut.Dispose();

        var act = sut.Dispose;

        act.Should().NotThrow();
    }

    private void FailFor(HomeShelfKind kind, Exception exception) =>
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q == HomeViewModel.QueryFor(kind)), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Album>>(exception));

    [Fact]
    public void Shelves_are_listed_in_order_with_localized_titles()
    {
        using var sut = CreateSut();

        sut.Shelves.Select(s => s.Kind).Should().Equal(Enum.GetValues<HomeShelfKind>());
        sut.Shelves[0].Title.Should().Be("[HomeShelf_RecentlyAdded]");
        sut.Shelves.Should().OnlyContain(s => !s.IsLoaded && !s.IsEmpty);
    }

    [Theory]
    [InlineData(HomeShelfKind.RecentlyAdded, AlbumSortField.DateAdded, true, false, false)]
    [InlineData(HomeShelfKind.RecentlyPlayed, AlbumSortField.DatePlayed, true, true, false)]
    [InlineData(HomeShelfKind.MostPlayed, AlbumSortField.PlayCount, true, true, false)]
    [InlineData(HomeShelfKind.Random, AlbumSortField.Random, false, false, false)]
    [InlineData(HomeShelfKind.Favourites, AlbumSortField.Random, false, false, true)]
    public void Each_shelf_has_its_query(HomeShelfKind kind, AlbumSortField sortBy, bool descending, bool playedOnly, bool favouritesOnly)
    {
        var query = HomeViewModel.QueryFor(kind);

        query.Should().BeEquivalentTo(new
        {
            StartIndex = 0,
            Limit = HomeViewModel.ShelfSize,
            SortBy = sortBy,
            Descending = descending,
            PlayedOnly = playedOnly,
            FavouritesOnly = favouritesOnly,
        });
    }

    [Fact]
    public async Task Refresh_fills_every_shelf()
    {
        using var sut = CreateSut();
        sut.DisplayScale = 2;

        await sut.RefreshAsync();

        sut.IsLoading.Should().BeFalse();
        sut.Shelves.Should().OnlyContain(s => s.IsLoaded && !s.IsLoading && !s.ShowsProgress && !s.LoadFailed && s.Albums.Count == 3);
        sut.Shelves[1].Albums[0].Album.Id.Should().Be("DatePlayed-0");
        sut.Shelves[0].Albums[0].Cover!.Width.Should().Be((int)Math.Ceiling(HomeViewModel.TileWidth * 2));
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Empty_shelf_reports_empty()
    {
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.FavouritesOnly), Arg.Any<CancellationToken>())
            .Returns(Page("none", 0));
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Shelves.Single(s => s.Kind == HomeShelfKind.Favourites).IsEmpty.Should().BeTrue();
        sut.Shelves.Count(s => s.IsEmpty).Should().Be(1);
    }

    [Fact]
    public async Task Failed_shelves_show_an_error_and_are_reported_once()
    {
        var first = new JellyfinException("boom");
        FailFor(HomeShelfKind.RecentlyPlayed, first);
        FailFor(HomeShelfKind.MostPlayed, new JellyfinException("boom again"));
        using var sut = CreateSut();

        await sut.RefreshAsync();

        sut.Shelves.Where(s => s.LoadFailed).Select(s => s.Kind)
            .Should().Equal(HomeShelfKind.RecentlyPlayed, HomeShelfKind.MostPlayed);
        sut.Shelves.Where(s => !s.LoadFailed).Should().OnlyContain(s => s.Albums.Count == 3);
        sut.Shelves.Where(s => s.LoadFailed).Should().OnlyContain(s => !s.IsLoading && !s.IsEmpty);
        _errors.Received(1).Report(Arg.Any<Exception>(), Operations.LoadHome);
        _errors.Received(1).Report(first, Operations.LoadHome);
    }

    [Fact]
    public async Task Refresh_after_a_failure_clears_the_error()
    {
        FailFor(HomeShelfKind.Random, new JellyfinException("boom"));
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
        _errors.Received(1).Report(Arg.Any<InvalidOperationException>(), Operations.LoadHome);
    }

    [Fact]
    public async Task Opening_an_album_navigates_to_its_detail_page()
    {
        using var sut = CreateSut();
        await sut.RefreshAsync();

        sut.OpenAlbumCommand.Execute(sut.Shelves[0].Albums[1]);

        _navigation.Received(1).NavigateTo(PageKeys.Album, "DateAdded-1");
    }
}
