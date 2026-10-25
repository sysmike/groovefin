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

public sealed class FavouritesViewModelTests
{
    private readonly PlayingTrack _playing = new();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FavouritesViewModel _sut;

    public FavouritesViewModelTests()
    {
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([Song("t1"), Song("t2")]);
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Album>([new Album { Id = "al1", Name = "Discovery" }], 1, 0));
        _library.GetArtistsAsync(Arg.Any<ArtistQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Artist>([new Artist { Id = "ar1", Name = "Daft Punk" }], 1, 0));
        _sut = new FavouritesViewModel(_library, _player, _navigation, _errors, new FavouritesService(_library, _messenger, _errors), _messenger, _playing.Current);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track Song(string id) => new()
    {
        Id = id,
        Name = $"Song {id}",
        AlbumId = "al1",
        AlbumName = "Discovery",
        AlbumImageTag = "t",
        Artists = [new ArtistRef("ar1", "Daft Punk")],
        Duration = TimeSpan.FromSeconds(61),
        IsFavorite = true,
    };

    private IEnumerable<string> TrackIds => _sut.Tracks.Select(t => t.Track.Id);

    [Fact]
    public async Task Load_shows_favourite_songs_albums_and_artists()
    {
        await _sut.LoadAsync(Ct);

        TrackIds.Should().Equal("t1", "t2");
        _sut.Tracks[0].AccessibleName.Should().Be("Song t1, Daft Punk · Discovery, 1:01");
        _sut.Albums.Single().Name.Should().Be("Discovery");
        _sut.Artists.Single().Name.Should().Be("Daft Punk");
        (_sut.HasTracks, _sut.HasAlbums, _sut.HasArtists).Should().Be((true, true, true));
        await _library.Received(1).GetFavouriteTracksAsync(FavouritesViewModel.Limit, Arg.Any<CancellationToken>());
        await _library.Received(1).GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.FavouritesOnly && q.Limit == FavouritesViewModel.Limit), Arg.Any<CancellationToken>());
        await _library.Received(1).GetArtistsAsync(Arg.Is<ArtistQuery>(q => q.FavouritesOnly && !q.AlbumArtistsOnly), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_load_is_reported()
    {
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw new JellyfinException("boom"));

        await _sut.LoadAsync(Ct);

        _sut.LoadFailed.Should().BeTrue();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadFavourites);
    }

    [Fact]
    public async Task Cancelled_loads_are_quiet()
    {
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw new OperationCanceledException());

        await _sut.LoadAsync(Ct);

        _sut.LoadFailed.Should().BeFalse();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Play_shuffle_and_a_row_play_the_favourite_songs()
    {
        await _sut.LoadAsync(Ct);

        _sut.PlayAllCommand.Execute(null);
        _sut.ShuffleCommand.Execute(null);
        _sut.PlayTrackCommand.Execute(_sut.Tracks[1]);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 2), 0);
        _player.Received(1).Shuffle(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 2));
        _player.Received(1).Play(Arg.Any<IReadOnlyList<Track>>(), 1);
    }

    [Fact]
    public async Task Albums_and_artists_open_their_pages()
    {
        await _sut.LoadAsync(Ct);

        _sut.OpenAlbumCommand.Execute(_sut.Albums[0]);
        _sut.OpenArtistCommand.Execute(_sut.Artists[0]);

        _navigation.Received(1).NavigateTo(PageKeys.Album, "al1");
        _navigation.Received(1).NavigateTo(PageKeys.Artist, "ar1");
    }

    [Fact]
    public async Task Unfavouriting_a_song_removes_it_at_once()
    {
        await _sut.LoadAsync(Ct);

        await _sut.UnfavouriteTrackCommand.ExecuteAsync(_sut.Tracks[0]);

        TrackIds.Should().Equal("t2");
        await _library.Received(1).SetFavouriteAsync("t1", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_refused_unfavourite_puts_the_song_back()
    {
        await _sut.LoadAsync(Ct);
        _library.SetFavouriteAsync("t1", false, Arg.Any<CancellationToken>()).Returns(Task.FromException(new JellyfinException("no")));

        await _sut.UnfavouriteTrackCommand.ExecuteAsync(_sut.Tracks[0]);

        TrackIds.Should().Equal("t1", "t2");
        _sut.HasTracks.Should().BeTrue();
    }

    [Fact]
    public async Task Unfavouriting_a_song_that_is_gone_does_nothing()
    {
        await _sut.LoadAsync(Ct);
        var row = _sut.Tracks[0];
        await _sut.UnfavouriteTrackCommand.ExecuteAsync(row);

        await _sut.UnfavouriteTrackCommand.ExecuteAsync(row);

        await _library.ReceivedWithAnyArgs(1).SetFavouriteAsync(default!, default, Ct);
    }

    [Fact]
    public async Task Hearts_removed_elsewhere_take_items_off_the_page()
    {
        await _sut.LoadAsync(Ct);

        _messenger.Send(new FavouriteChangedMessage("al1", false));
        _messenger.Send(new FavouriteChangedMessage("ar1", false));
        _messenger.Send(new FavouriteChangedMessage("t2", false));
        _messenger.Send(new FavouriteChangedMessage("t1", true));

        _sut.Albums.Should().BeEmpty();
        _sut.Artists.Should().BeEmpty();
        TrackIds.Should().Equal("t1");
        (_sut.HasAlbums, _sut.HasArtists).Should().Be((false, false));
    }

    [Fact]
    public async Task Nothing_to_play_without_songs()
    {
        _library.GetFavouriteTracksAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await _sut.LoadAsync(Ct);

        _sut.PlayAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task The_playing_favourite_song_is_marked()
    {
        await _sut.LoadAsync(Ct);

        _playing.Play("t2");

        _sut.Tracks.Select(t => t.IsCurrent).Should().Equal(false, true);
    }
}
