using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
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
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();

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

    private AlbumDetailViewModel CreateSut() => new(_library, _player, _navigation, _errors, _strings, new FavouritesService(_library, _messenger, _errors), _messenger, new InstantMix(_library, _player, new InMemorySettingsService(), _errors));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_shows_genres_and_links_to_other_sites()
    {
        _library.GetAlbumAsync("album-1", Arg.Any<CancellationToken>()).Returns(Discovery with
        {
            Genres = [new Genre("g1", "Electronic")],
            MusicBrainzReleaseGroupId = "group-1",
        });
        var sut = CreateSut();

        await sut.LoadAsync("album-1", Ct);

        sut.Genres.Should().Equal(new Genre("g1", "Electronic"));
        sut.HasGenres.Should().BeTrue();
        sut.MusicBrainzUrl!.AbsoluteUri.Should().Be("https://musicbrainz.org/release-group/group-1");
        sut.LastFmUrl!.AbsoluteUri.Should().Be("https://www.last.fm/music/Daft+Punk/Discovery");
    }

    [Fact]
    public async Task Without_ids_and_genres_those_parts_are_hidden()
    {
        var sut = CreateSut();

        await sut.LoadAsync("album-1", Ct);

        sut.HasGenres.Should().BeFalse();
        sut.HasMusicBrainz.Should().BeFalse();
        sut.HasLastFm.Should().BeTrue();
    }

    [Fact]
    public void A_genre_opens_its_page()
    {
        var sut = CreateSut();

        sut.OpenGenreCommand.Execute(new Genre("g1", "Electronic"));

        _navigation.Received(1).NavigateTo(PageKeys.Genre, "g1");
    }

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

        sut.Discs.Should().ContainSingle().Which.HasHeader.Should().BeFalse();
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

        sut.Discs.Select(d => (d.Header, d.HasHeader, d.Tracks.Count)).Should().Equal(("Disc 1", true, 2), ("Disc 2", true, 1));
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
        sut.ShuffleCommand.CanExecute(null).Should().BeFalse();
        sut.AddToQueueCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Play_shuffle_and_add_to_queue_hand_the_tracks_to_the_player()
    {
        var sut = CreateSut();
        await sut.LoadAsync("album-1", TestContext.Current.CancellationToken);
        var ids = new[] { "t1", "t2" };

        sut.PlayCommand.Execute(null);
        sut.ShuffleCommand.Execute(null);
        sut.AddToQueueCommand.Execute(null);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Select(x => x.Id).SequenceEqual(ids)), 0);
        _player.Received(1).Shuffle(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 2));
        _player.Received(1).AddToQueue(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 2));
    }

    [Fact]
    public async Task Clicking_a_track_plays_the_album_from_there()
    {
        var sut = CreateSut();
        await sut.LoadAsync("album-1", TestContext.Current.CancellationToken);

        sut.PlayTrackCommand.Execute(sut.Discs[0].Tracks[1]);
        sut.PlayTrackCommand.Execute(new TrackItem(new Track { Id = "elsewhere", Name = "x" }, null));

        _player.Received(1).Play(Arg.Any<IReadOnlyList<Track>>(), 1);
        _player.ReceivedWithAnyArgs(1).Play(default!, default);
    }

    [Fact]
    public void OpenArtist_navigates_to_the_artist()
    {
        CreateSut().OpenArtistCommand.Execute(DaftPunk);

        _navigation.Received(1).NavigateTo(PageKeys.Artist, "artist-1");
    }

    [Fact]
    public async Task A_track_heart_is_optimistic_and_rolls_back_on_failure()
    {
        var sut = CreateSut();
        await sut.LoadAsync("album-1", Ct);
        var row = sut.Discs[0].Tracks[0];

        await sut.ToggleTrackFavouriteCommand.ExecuteAsync(row);
        row.IsFavourite.Should().BeTrue();
        await _library.Received(1).SetFavouriteAsync("t1", true, Arg.Any<CancellationToken>());

        _library.SetFavouriteAsync("t1", false, Arg.Any<CancellationToken>()).Returns(Task.FromException(new JellyfinException("no")));
        await sut.ToggleTrackFavouriteCommand.ExecuteAsync(row);

        row.IsFavourite.Should().BeTrue();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.SetFavourite);
    }

    [Fact]
    public async Task Hearts_toggled_elsewhere_update_the_album_and_its_rows()
    {
        var sut = CreateSut();
        await sut.LoadAsync("album-1", Ct);

        _messenger.Send(new FavouriteChangedMessage("album-1", true));
        _messenger.Send(new FavouriteChangedMessage("t2", true));

        sut.IsFavourite.Should().BeTrue();
        sut.Discs[0].Tracks.Select(t => t.IsFavourite).Should().Equal(false, true);
    }

    [Fact]
    public async Task Rows_start_with_the_state_saved_in_this_session()
    {
        var favourites = new FavouritesService(_library, _messenger, _errors);
        await favourites.SetAsync("t1", true);
        var sut = new AlbumDetailViewModel(_library, _player, _navigation, _errors, _strings, favourites, _messenger, new InstantMix(_library, _player, new InMemorySettingsService(), _errors));

        await sut.LoadAsync("album-1", Ct);
        await sut.LoadAsync("album-1", Ct);

        sut.Discs[0].Tracks[0].IsFavourite.Should().BeTrue();
    }

    [Fact]
    public async Task Instant_mix_starts_a_mix_of_the_album()
    {
        var sut = CreateSut();
        sut.InstantMixCommand.CanExecute(null).Should().BeFalse();
        await sut.LoadAsync("album-1", Ct);
        _library.GetInstantMixAsync("album-1", InstantMix.Size, Arg.Any<CancellationToken>()).Returns([new Track { Id = "m", Name = "m" }]);

        await sut.InstantMixCommand.ExecuteAsync(null);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Single().Id == "m"));
    }
}
