using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class ArtistsViewModelTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly InMemorySettingsService _settings = new();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly List<ArtistQuery> _queries = [];

    public ArtistsViewModelTests() => StubArtists(total: 150);

    private static Artist Artist(int i) => new() { Id = $"artist-{i}", Name = $"Artist {i}", PrimaryImageTag = "tag" };

    private void StubArtists(int total) =>
        _library.GetArtistsAsync(Arg.Do<ArtistQuery>(_queries.Add), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var q = call.Arg<ArtistQuery>();
                var count = Math.Max(0, Math.Min(q.Limit, total - q.StartIndex));
                return new PagedResult<Artist>(Enumerable.Range(q.StartIndex, count).Select(Artist).ToList(), total, q.StartIndex);
            });

    private ArtistsViewModel CreateSut() => new(_library, _settings, _navigation, _errors);

    [Fact]
    public async Task Initialize_loads_the_first_page_of_album_artists()
    {
        using var sut = CreateSut();
        sut.DisplayScale = 2;

        await sut.InitializeAsync();

        sut.Artists.Should().HaveCount(ArtistsViewModel.PageSize);
        sut.TotalCount.Should().Be(150);
        sut.Artists[0].Image!.Width.Should().Be((int)Math.Ceiling(ArtistsViewModel.TileWidth * 2));
        sut.Artists[0].AccessibleName.Should().Be("Artist 0");
        _queries.Single().Should().BeEquivalentTo(new { StartIndex = 0, AlbumArtistsOnly = true, SortBy = ArtistSortField.Name, Descending = false, FavouritesOnly = false });
        sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task LoadMore_appends_until_everything_is_loaded()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        await sut.LoadMoreCommand.ExecuteAsync(null);

        sut.Artists.Should().HaveCount(150);
        _queries.Select(q => q.StartIndex).Should().Equal(0, 100);
        sut.LoadMoreCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Scope_sort_and_view_are_remembered_and_reload()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();
        await sut.LoadMoreCommand.ExecuteAsync(null);

        sut.AlbumArtistsOnly = false;
        sut.SortBy = ArtistSortField.Random;
        sut.Descending = true;
        sut.ViewMode = CollectionViewMode.List;
        await Task.Yield();

        _queries[^1].Should().BeEquivalentTo(new { StartIndex = 0, AlbumArtistsOnly = false, SortBy = ArtistSortField.Random, Descending = true });
        _settings.Current.Browse.Should().BeEquivalentTo(new
        {
            AlbumArtistsOnly = false,
            ArtistSort = ArtistSortField.Random,
            ArtistSortDescending = true,
            ArtistView = CollectionViewMode.List,
        });

        using var restored = CreateSut();
        restored.AlbumArtistsOnly.Should().BeFalse();
        restored.SortBy.Should().Be(ArtistSortField.Random);
        restored.ViewMode.Should().Be(CollectionViewMode.List);
    }

    [Fact]
    public async Task Favourites_filter_reloads()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.FavouritesOnly = true;
        await Task.Yield();

        _queries[^1].FavouritesOnly.Should().BeTrue();
        _queries[^1].StartIndex.Should().Be(0);
    }

    [Fact]
    public async Task No_artists_shows_the_empty_state()
    {
        StubArtists(total: 0);
        using var sut = CreateSut();

        await sut.InitializeAsync();

        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Failure_is_reported()
    {
        _library.GetArtistsAsync(Arg.Any<ArtistQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Artist>>(new JellyfinException("boom")));
        using var sut = CreateSut();

        await sut.InitializeAsync();

        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadArtists);
        sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Opening_an_artist_navigates_to_the_detail_page()
    {
        using var sut = CreateSut();
        await sut.InitializeAsync();

        sut.OpenArtistCommand.Execute(sut.Artists[3]);

        _navigation.Received(1).NavigateTo(PageKeys.Artist, "artist-3");
    }
}
