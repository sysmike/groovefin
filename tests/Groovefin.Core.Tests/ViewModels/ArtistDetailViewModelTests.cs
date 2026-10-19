using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class ArtistDetailViewModelTests
{
    private const string ArtistId = "artist-1";

    private static readonly Artist DaftPunk = new()
    {
        Id = ArtistId,
        Name = "Daft Punk",
        PrimaryImageTag = "img",
        Biography = "French duo.",
        Genres = ["Electronic", "House"],
    };

    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();

    public ArtistDetailViewModelTests()
    {
        _library.GetArtistAsync(ArtistId, Arg.Any<CancellationToken>()).Returns(DaftPunk);
        _library.GetArtistTopTracksAsync(ArtistId, ArtistDetailViewModel.TopTrackCount, Arg.Any<CancellationToken>())
            .Returns([Track("t1", "One More Time", "Discovery", "album-1"), Track("t2", "Around the World", "Homework", "album-2")]);
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.AlbumArtistId == ArtistId), Arg.Any<CancellationToken>())
            .Returns(Page(Album("album-1", "Discovery", ArtistId), Album("album-2", "Homework", ArtistId)));
        _library.GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.ContributingArtistId == ArtistId), Arg.Any<CancellationToken>())
            .Returns(Page(Album("album-9", "Compilation", "various"), Album("album-1", "Discovery", ArtistId)));
        _library.GetSimilarArtistsAsync(ArtistId, ArtistDetailViewModel.SimilarCount, Arg.Any<CancellationToken>())
            .Returns([new Artist { Id = "artist-2", Name = "Justice" }]);
    }

    private static Track Track(string id, string name, string album, string albumId) =>
        new() { Id = id, Name = name, AlbumName = album, AlbumId = albumId, Duration = TimeSpan.FromSeconds(320) };

    private static Album Album(string id, string name, string albumArtistId) =>
        new() { Id = id, Name = name, AlbumArtists = [new ArtistRef(albumArtistId, "x")], PrimaryImageTag = "tag" };

    private static PagedResult<Album> Page(params Album[] albums) => new(albums, albums.Length, 0);

    private ArtistDetailViewModel CreateSut() => new(_library, _navigation, _errors);

    [Fact]
    public async Task Load_fills_header_and_all_sections()
    {
        var sut = CreateSut();
        sut.DisplayScale = 1.5;

        await sut.LoadAsync(ArtistId, TestContext.Current.CancellationToken);

        sut.HasArtist.Should().BeTrue();
        sut.Name.Should().Be("Daft Punk");
        sut.GenresText.Should().Be("Electronic · House");
        sut.Biography.Should().Be("French duo.");
        sut.HasBiography.Should().BeTrue();
        sut.Image!.Width.Should().Be((int)Math.Ceiling(ArtistDetailViewModel.ImageWidth * 1.5));
        sut.TopTracks.Select(t => (t.Number, t.Name, t.Album)).Should().Equal(("1", "One More Time", "Discovery"), ("2", "Around the World", "Homework"));
        sut.TopTracks[0].AccessibleName.Should().Be("1, One More Time, Discovery, 5:20");
        sut.Albums.Select(a => a.Album.Id).Should().Equal("album-1", "album-2");
        sut.Similar.Should().ContainSingle().Which.Name.Should().Be("Justice");
        (sut.HasTopTracks, sut.HasAlbums, sut.HasAppearsOn, sut.HasSimilar).Should().Be((true, true, true, true));
        sut.IsLoading.Should().BeFalse();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Albums_are_newest_first_and_appears_on_leaves_out_the_artists_own_albums()
    {
        var sut = CreateSut();

        await sut.LoadAsync(ArtistId, TestContext.Current.CancellationToken);

        await _library.Received(1).GetAlbumsAsync(
            Arg.Is<AlbumQuery>(q => q.AlbumArtistId == ArtistId && q.SortBy == AlbumSortField.Year && q.Descending),
            Arg.Any<CancellationToken>());
        sut.AppearsOn.Select(a => a.Album.Id).Should().Equal("album-9");
    }

    [Fact]
    public async Task Empty_sections_are_hidden()
    {
        _library.GetSimilarArtistsAsync(default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs([]);
        _library.GetArtistTopTracksAsync(default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs([]);
        _library.GetArtistAsync(ArtistId, Arg.Any<CancellationToken>()).Returns(DaftPunk with { Biography = null });
        var sut = CreateSut();

        await sut.LoadAsync(ArtistId, TestContext.Current.CancellationToken);

        sut.HasSimilar.Should().BeFalse();
        sut.HasTopTracks.Should().BeFalse();
        sut.HasBiography.Should().BeFalse();
        sut.HasAlbums.Should().BeTrue();
    }

    [Fact]
    public async Task A_failing_section_is_hidden_and_reported_once_while_the_rest_loads()
    {
        var failure = new JellyfinException("boom");
        _library.GetSimilarArtistsAsync(ArtistId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Artist>>(failure));
        _library.GetArtistTopTracksAsync(ArtistId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Track>>(new JellyfinException("boom too")));
        var sut = CreateSut();

        await sut.LoadAsync(ArtistId, TestContext.Current.CancellationToken);

        sut.LoadFailed.Should().BeFalse();
        sut.HasSimilar.Should().BeFalse();
        sut.HasTopTracks.Should().BeFalse();
        sut.HasAlbums.Should().BeTrue();
        _errors.Received(1).Report(Arg.Any<Exception>(), Operations.LoadArtist);
    }

    [Fact]
    public async Task Missing_artist_shows_the_failure_state()
    {
        _library.GetArtistAsync(ArtistId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Artist>(new JellyfinException("gone", System.Net.HttpStatusCode.NotFound)));
        var sut = CreateSut();

        await sut.LoadAsync(ArtistId, TestContext.Current.CancellationToken);

        sut.LoadFailed.Should().BeTrue();
        sut.HasArtist.Should().BeFalse();
        sut.IsLoading.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadArtist);
    }

    [Fact]
    public async Task Favourite_is_optimistic_and_rolls_back_on_failure()
    {
        var sut = CreateSut();
        await sut.LoadAsync(ArtistId, TestContext.Current.CancellationToken);

        await sut.ToggleFavouriteCommand.ExecuteAsync(null);
        sut.IsFavourite.Should().BeTrue();
        await _library.Received(1).SetFavouriteAsync(ArtistId, true, Arg.Any<CancellationToken>());

        _library.SetFavouriteAsync(ArtistId, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new JellyfinException("no")));
        await sut.ToggleFavouriteCommand.ExecuteAsync(null);

        sut.IsFavourite.Should().BeTrue();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.SetFavourite);
    }

    [Fact]
    public async Task Navigation_opens_albums_tracks_albums_and_similar_artists()
    {
        var sut = CreateSut();
        await sut.LoadAsync(ArtistId, TestContext.Current.CancellationToken);

        sut.OpenAlbumCommand.Execute(sut.Albums[1]);
        sut.OpenTrackAlbumCommand.Execute(sut.TopTracks[0]);
        sut.OpenArtistCommand.Execute(sut.Similar[0]);

        Received.InOrder(() =>
        {
            _navigation.NavigateTo(PageKeys.Album, "album-2");
            _navigation.NavigateTo(PageKeys.Album, "album-1");
            _navigation.NavigateTo(PageKeys.Artist, "artist-2");
        });
    }

    [Fact]
    public void Playback_waits_for_the_engine()
    {
        var sut = CreateSut();

        sut.PlayCommand.CanExecute(null).Should().BeFalse();
        sut.ShuffleCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Track_rows_link_their_artists_only_when_they_show_them()
    {
        var artists = new[] { new ArtistRef("a1", "Daft Punk"), new ArtistRef("a2", "Romanthony") };
        var track = new Track { Id = "t", Name = "One More Time", Artists = artists };

        new TrackItem(track, "Daft Punk, Romanthony").ArtistLinks.Should().Equal(artists);
        new TrackItem(track, null).ArtistLinks.Should().BeEmpty();
    }
}
