using Groovefin.Core.Api;
using Groovefin.Core.Menus;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class GenreDetailViewModelTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly GenreDetailViewModel _sut;

    public GenreDetailViewModelTests()
    {
        _library.GetGenreAsync("g1", Arg.Any<CancellationToken>()).Returns(new Genre("g1", "Electronic"));
        _library.GetAlbumsAsync(Arg.Any<AlbumQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Album>([new Album { Id = "al1", Name = "Discovery", PrimaryImageTag = "c" }], 1, 0));
        _library.GetGenreTracksAsync("g1", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Songs(3));
        _sut = new GenreDetailViewModel(_library, _player, _navigation, _errors);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track[] Songs(int count) => Enumerable.Range(1, count)
        .Select(i => new Track { Id = $"t{i}", Name = $"Song {i}", AlbumId = "al1", AlbumName = "Discovery", Artists = [new ArtistRef("a", "Daft Punk")], Duration = TimeSpan.FromSeconds(90) })
        .ToArray();

    [Fact]
    public async Task Load_shows_the_genre_its_albums_and_songs()
    {
        await _sut.LoadAsync("g1", Ct);

        _sut.Name.Should().Be("Electronic");
        _sut.Albums.Single().Name.Should().Be("Discovery");
        _sut.Tracks.Select(t => t.Name).Should().Equal("Song 1", "Song 2", "Song 3");
        _sut.Tracks[0].AccessibleName.Should().Be("Song 1, Daft Punk · Discovery, 1:30");
        (_sut.HasAlbums, _sut.HasTracks).Should().Be((true, true));
        await _library.Received(1).GetAlbumsAsync(Arg.Is<AlbumQuery>(q => q.GenreIds.Single() == "g1" && q.Limit == GenreDetailViewModel.AlbumLimit), Arg.Any<CancellationToken>());
        await _library.Received(1).GetGenreTracksAsync("g1", GenreDetailViewModel.TrackLimit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_the_first_songs_are_listed_but_all_are_played()
    {
        _library.GetGenreTracksAsync("g1", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Songs(GenreDetailViewModel.TracksShown + 5));
        await _sut.LoadAsync("g1", Ct);

        _sut.PlayCommand.Execute(null);

        _sut.Tracks.Should().HaveCount(GenreDetailViewModel.TracksShown);
        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Count == GenreDetailViewModel.TracksShown + 5), 0);
    }

    [Fact]
    public async Task Shuffle_a_row_and_an_album_tile_do_what_they_say()
    {
        await _sut.LoadAsync("g1", Ct);

        _sut.ShuffleCommand.Execute(null);
        _sut.PlayTrackCommand.Execute(_sut.Tracks[2]);
        _sut.OpenAlbumCommand.Execute(_sut.Albums[0]);

        _player.Received(1).Shuffle(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 3));
        _player.Received(1).Play(Arg.Any<IReadOnlyList<Track>>(), 2);
        _navigation.Received(1).NavigateTo(PageKeys.Album, "al1");
    }

    [Fact]
    public async Task Instant_mix_plays_the_genre_mix()
    {
        await _sut.LoadAsync("g1", Ct);
        _library.GetInstantMixAsync("g1", ContextMenus.InstantMixSize, Arg.Any<CancellationToken>()).Returns(Songs(2));

        await _sut.InstantMixCommand.ExecuteAsync(null);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 2));
    }

    [Fact]
    public async Task An_empty_mix_plays_nothing_and_a_failed_one_is_reported()
    {
        await _sut.LoadAsync("g1", Ct);
        _library.GetInstantMixAsync("g1", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        await _sut.InstantMixCommand.ExecuteAsync(null);
        _player.DidNotReceiveWithAnyArgs().Play(default!);

        _library.GetInstantMixAsync("g1", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw new JellyfinException("boom"));
        await _sut.InstantMixCommand.ExecuteAsync(null);
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.Playback);
    }

    [Fact]
    public async Task A_genre_without_songs_cannot_be_played()
    {
        _library.GetGenreTracksAsync("g1", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await _sut.LoadAsync("g1", Ct);

        _sut.PlayCommand.CanExecute(null).Should().BeFalse();
        _sut.ShuffleCommand.CanExecute(null).Should().BeFalse();
        _sut.InstantMixCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_load_is_reported()
    {
        _library.GetGenreAsync("g1", Arg.Any<CancellationToken>()).Returns<Genre>(_ => throw new JellyfinException("boom"));

        await _sut.LoadAsync("g1", Ct);

        _sut.LoadFailed.Should().BeTrue();
        _sut.InstantMixCommand.CanExecute(null).Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadGenre);
    }

    [Fact]
    public async Task Cancelled_loads_are_quiet()
    {
        _library.GetGenreAsync("g1", Arg.Any<CancellationToken>()).Returns<Genre>(_ => throw new OperationCanceledException());

        await _sut.LoadAsync("g1", Ct);

        _sut.LoadFailed.Should().BeFalse();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }
}
