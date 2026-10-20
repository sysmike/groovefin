using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class AlbumsViewModelTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly InMemorySettingsService _settings = new();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly List<AlbumQuery> _queries = [];

    public AlbumsViewModelTests()
    {
        _library.GetAlbumGenresAsync(Arg.Any<CancellationToken>()).Returns([new Genre("g1", "Jazz")]);
        StubAlbums(total: 250);
    }

    private static Album Album(int i) => new() { Id = $"album-{i}", Name = $"Album {i}", AlbumArtist = "Artist", Year = 2000, PrimaryImageTag = "tag" };

    private void StubAlbums(int total) =>
        _library.GetAlbumsAsync(Arg.Do<AlbumQuery>(_queries.Add), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var q = call.Arg<AlbumQuery>();
                var count = Math.Max(0, Math.Min(q.Limit, total - q.StartIndex));
                var items = Enumerable.Range(q.StartIndex, count).Select(Album).ToList();
                return new PagedResult<Album>(items, total, q.StartIndex);
            });

    private AlbumsViewModel CreateSut() => new(_library, _settings, _navigation, _errors);

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
    public async Task Initialize_loads_genres_and_the_first_page()
    {
        using var sut = CreateSut();
        sut.DisplayScale = 1.5;

        await sut.InitializeAsync();

        sut.Albums.Should().HaveCount(AlbumsViewModel.PageSize);
        sut.TotalCount.Should().Be(250);
        sut.Genres.Should().Equal(null, new Genre("g1", "Jazz"));
        sut.Albums[0].Cover!.Width.Should().Be((int)Math.Ceiling(AlbumsViewModel.TileWidth * 1.5));
        sut.Albums[0].AccessibleName.Should().Be("Album 0, Artist, 2000");
        sut.IsLoading.Should().BeFalse();
        sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task LoadMore_appends_pages_until_everything_is_loaded()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        await sut.LoadMoreCommand.ExecuteAsync(null);
        await sut.LoadMoreCommand.ExecuteAsync(null);

        _queries.Select(q => q.StartIndex).Should().Equal(0, 100, 200);
        sut.Albums.Should().HaveCount(250);
        sut.LoadMoreCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Sort_change_is_remembered_and_reloads_from_the_start()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();
        await sut.LoadMoreCommand.ExecuteAsync(null);

        sut.SortBy = AlbumSortField.Year;
        sut.Descending = true;
        await Task.Yield();

        _settings.Current.Browse.AlbumSort.Should().Be(AlbumSortField.Year);
        _settings.Current.Browse.AlbumSortDescending.Should().BeTrue();
        _queries[^1].Should().BeEquivalentTo(new { StartIndex = 0, SortBy = AlbumSortField.Year, Descending = true });
        sut.Albums.Should().HaveCount(AlbumsViewModel.PageSize);
    }

    [Fact]
    public async Task Remembered_view_and_sort_are_restored()
    {
        _settings.Update(s => s with
        {
            Browse = new BrowseSettings { AlbumView = CollectionViewMode.List, AlbumSort = AlbumSortField.DateAdded, AlbumSortDescending = true },
        });

        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.ViewMode.Should().Be(CollectionViewMode.List);
        _queries[0].Should().BeEquivalentTo(new { SortBy = AlbumSortField.DateAdded, Descending = true });
    }

    [Fact]
    public async Task ViewMode_is_remembered_without_reloading()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.ViewMode = CollectionViewMode.List;

        _settings.Current.Browse.AlbumView.Should().Be(CollectionViewMode.List);
        _queries.Should().ContainSingle();
    }

    [Fact]
    public async Task Filters_are_sent_and_can_be_cleared()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.Genre = sut.Genres[1];
        sut.MinYear = 1990;
        sut.MaxYear = 1999;
        sut.FavouritesOnly = true;
        await Task.Yield();

        sut.HasFilters.Should().BeTrue();
        _queries[^1].Should().BeEquivalentTo(new { GenreIds = new[] { "g1" }, MinYear = 1990, MaxYear = 1999, FavouritesOnly = true });

        sut.ClearFiltersCommand.Execute(null);

        sut.HasFilters.Should().BeFalse();
        _queries[^1].Should().BeEquivalentTo(new { GenreIds = Array.Empty<string>(), MinYear = (int?)null, MaxYear = (int?)null, FavouritesOnly = false });
    }

    [Fact]
    public async Task A_slow_page_from_before_a_reload_is_dropped()
    {
        var slow = new TaskCompletionSource<PagedResult<Album>>();
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.SortBy == AlbumSortField.Name), Arg.Any<CancellationToken>()).Returns(slow.Task);
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.SortBy == AlbumSortField.Year), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Album>([Album(42)], 1, 0));
        using var sut = CreateSut();

        var init = sut.InitializeAsync();
        sut.SortBy = AlbumSortField.Year;
        slow.SetResult(new PagedResult<Album>([Album(1), Album(2)], 2, 0));
        await init;

        sut.Albums.Should().ContainSingle().Which.Album.Id.Should().Be("album-42");
        sut.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Empty_result_shows_the_empty_state()
    {
        StubAlbums(total: 0);
        using var sut = CreateSut();

        await sut.InitializeAsync();

        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Load_failure_is_reported()
    {
        var failure = new JellyfinException("down", statusCode: null);
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>()).Returns<PagedResult<Album>>(_ => throw failure);
        using var sut = CreateSut();

        await sut.InitializeAsync();

        _errors.Received(1).Report(failure, Operations.LoadAlbums);
        sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Genre_failure_still_loads_albums()
    {
        _library.GetAlbumGenresAsync(TestContext.Current.CancellationToken).ReturnsForAnyArgs<IReadOnlyList<Genre>>(_ => throw new JellyfinException("x", statusCode: null));
        using var sut = CreateSut();

        await sut.InitializeAsync();

        sut.Albums.Should().NotBeEmpty();
        sut.Genres.Should().Equal([null]);
    }

    [Fact]
    public async Task OpenAlbum_navigates_to_the_album()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.OpenAlbumCommand.Execute(sut.Albums[3]);

        _navigation.Received(1).NavigateTo(PageKeys.Album, "album-3");
    }

    [Fact]
    public async Task Reload_starts_over_with_the_first_page()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();
        await sut.LoadMoreCommand.ExecuteAsync(null);

        await sut.ReloadAsync();

        sut.Albums.Should().HaveCount(AlbumsViewModel.PageSize);
        _queries[^1].StartIndex.Should().Be(0);
    }

    [Fact]
    public async Task Reload_before_the_page_is_shown_loads_nothing()
    {
        using var sut = CreateSut();

        await sut.ReloadAsync();

        _queries.Should().BeEmpty();
    }
}
