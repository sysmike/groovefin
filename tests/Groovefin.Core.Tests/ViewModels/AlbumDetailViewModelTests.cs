using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class AlbumDetailViewModelTests
{
    private static readonly ArtistRef DaftPunk = new("artist-1", "Daft Punk");

    private static readonly Album Discovery = new()
    {
        Id = "album-1",
        Name = "Discovery",
        AlbumArtist = "Daft Punk",
        AlbumArtists = [DaftPunk],
        Year = 2001,
        PrimaryImageTag = "tag",
        IsFavorite = false,
    };

    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public AlbumDetailViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Album_TrackCount").Returns("{0} tracks");
        _strings.GetString("Album_TrackCountOne").Returns("{0} track");
        _strings.GetString("Album_Disc").Returns("Disc {0}");
        _strings.GetString("Duration_Minutes").Returns("{0} min");
        _library.GetAlbumAsync("album-1", Arg.Any<CancellationToken>()).Returns(Discovery);
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns(
        [
            Track("t1", 1, null, 300, DaftPunk),
            Track("t2", 2, null, 240, DaftPunk, new ArtistRef("artist-2", "Romanthony")),
        ]);
    }

    private static Track Track(string id, int number, int? disc, int seconds, params ArtistRef[] artists) => new()
    {
        Id = id,
        Name = $"Track {number}",
        TrackNumber = number,
        DiscNumber = disc,
        Duration = TimeSpan.FromSeconds(seconds),
        Artists = artists,
    };

    private AlbumDetailViewModel CreateSut() => new(_library, _navigation, _errors, _strings);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_fills_the_header()
    {
        var sut = CreateSut();
        sut.DisplayScale = 2;

        await sut.LoadAsync("album-1", Ct);

        sut.Title.Should().Be("Discovery");
        sut.ArtistText.Should().Be("Daft Punk");
        sut.Artists.Should().ContainSingle();
        sut.Details.Should().Be("2001 · 2 tracks · 9 min");
        sut.Cover!.Width.Should().Be((int)(AlbumDetailViewModel.CoverWidth * 2));
        sut.Background!.Width.Should().Be((int)AlbumDetailViewModel.BackgroundWidth);
        sut.IsLoading.Should().BeFalse();
        sut.LoadFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Single_disc_album_has_one_group_without_header()
    {
        var sut = CreateSut();

        await sut.LoadAsync("album-1", Ct);

        sut.Discs.Should().ContainSingle().Which.Header.Should().BeEmpty();
        sut.Discs[0].Tracks.Select(t => t.Number).Should().Equal("1", "2");
    }

    [Fact]
    public async Task Tracks_show_their_artist_only_when_it_differs_from_the_album_artist()
    {
        var sut = CreateSut();

        await sut.LoadAsync("album-1", Ct);

        var tracks = sut.Discs[0].Tracks;
        tracks[0].HasArtist.Should().BeFalse();
        tracks[1].Artist.Should().Be("Daft Punk, Romanthony");
        tracks[1].AccessibleName.Should().Be("2, Track 2, Daft Punk, Romanthony, 4:00");
    }

    [Fact]
    public async Task Multi_disc_albums_are_grouped_by_disc()
    {
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns(
        [
            Track("a", 1, 1, 60), Track("b", 2, 1, 60), Track("c", 1, 2, 60),
        ]);
        var sut = CreateSut();

        await sut.LoadAsync("album-1", Ct);

        sut.Discs.Select(d => (d.Header, d.Tracks.Count)).Should().Equal(("Disc 1", 2), ("Disc 2", 1));
    }

    [Fact]
    public async Task Load_failure_is_reported_and_shown()
    {
        var failure = new JellyfinException("gone", System.Net.HttpStatusCode.NotFound);
        _library.GetAlbumAsync("album-1", Arg.Any<CancellationToken>()).Returns<Album>(_ => throw failure);
        var sut = CreateSut();

        await sut.LoadAsync("album-1", Ct);

        sut.LoadFailed.Should().BeTrue();
        sut.HasAlbum.Should().BeFalse();
        _errors.Received(1).Report(failure, Operations.LoadAlbum);
    }

    [Fact]
    public async Task ToggleFavourite_updates_at_once_and_on_the_server()
    {
        var sut = CreateSut();
        await sut.LoadAsync("album-1", Ct);

        await sut.ToggleFavouriteCommand.ExecuteAsync(null);

        sut.IsFavourite.Should().BeTrue();
        await _library.Received(1).SetFavouriteAsync("album-1", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleFavourite_rolls_back_when_the_server_refuses()
    {
        var failure = new JellyfinException("no", System.Net.HttpStatusCode.Forbidden);
        _library.SetFavouriteAsync("album-1", true, Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        var sut = CreateSut();
        await sut.LoadAsync("album-1", Ct);

        await sut.ToggleFavouriteCommand.ExecuteAsync(null);

        sut.IsFavourite.Should().BeFalse();
        _errors.Received(1).Report(failure, Operations.SetFavourite);
    }

    [Fact]
    public void Favourite_and_playback_are_unavailable_before_loading()
    {
        var sut = CreateSut();

        sut.ToggleFavouriteCommand.CanExecute(null).Should().BeFalse();
        sut.PlayCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void OpenArtist_navigates_to_the_artist()
    {
        CreateSut().OpenArtistCommand.Execute(DaftPunk);

        _navigation.Received(1).NavigateTo(PageKeys.Artist, "artist-1");
    }
}
