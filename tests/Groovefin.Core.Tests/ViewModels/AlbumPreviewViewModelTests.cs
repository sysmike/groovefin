using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class AlbumPreviewViewModelTests
{
    private static readonly ArtistRef DaftPunk = new("artist-1", "Daft Punk");

    private static readonly Album Discovery = new()
    {
        Id = "album-1",
        Name = "Discovery",
        AlbumArtist = "Daft Punk",
        AlbumArtists = [DaftPunk],
        Year = 2001,
    };

    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly AlbumPreviewViewModel _sut;

    public AlbumPreviewViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Album_TrackCount").Returns("{0} tracks");
        _strings.GetString("Album_Disc").Returns("Disc {0}");
        _strings.GetString("Duration_Minutes").Returns("{0} min");
        _strings.GetString("Cover_Play").Returns("Play {0}");
        _sut = new AlbumPreviewViewModel(_library, _player, _errors, _strings);
    }

    private static Track Track(string id, int number, int? disc, params ArtistRef[] artists) => new()
    {
        Id = id,
        Name = $"Track {number}",
        TrackNumber = number,
        DiscNumber = disc,
        Duration = TimeSpan.FromSeconds(240),
        Artists = artists,
    };

    [Fact]
    public async Task Loading_shows_the_album_and_its_tracks_in_one_group()
    {
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns(
            [Track("t1", 1, null, DaftPunk), Track("t2", 2, null, DaftPunk, new ArtistRef("artist-2", "Romanthony"))]);

        await _sut.LoadAsync(Discovery, TestContext.Current.CancellationToken);

        _sut.Title.Should().Be("Discovery");
        _sut.ArtistText.Should().Be("Daft Punk");
        _sut.PlayName.Should().Be("Play Discovery");
        _sut.Details.Should().Be("2001 · 2 tracks · 8 min");
        _sut.Discs.Should().ContainSingle().Which.HasHeader.Should().BeFalse();
        _sut.Discs[0].Tracks.Select(t => t.Number).Should().Equal("1", "2");
        _sut.Discs[0].Tracks[0].HasArtist.Should().BeFalse("the album artist is not repeated");
        _sut.Discs[0].Tracks[1].Artist.Should().Be("Daft Punk, Romanthony");
        _sut.IsLoading.Should().BeFalse();
        _sut.PlayCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Albums_with_several_discs_get_a_header_per_disc()
    {
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns(
            [Track("t1", 1, 1), Track("t2", 2, 1), Track("t3", 1, 2)]);

        await _sut.LoadAsync(Discovery, TestContext.Current.CancellationToken);

        _sut.Discs.Select(d => d.Header).Should().Equal("Disc 1", "Disc 2");
        _sut.Discs[1].Tracks.Should().ContainSingle().Which.Track.Id.Should().Be("t3");
    }

    [Fact]
    public async Task A_click_on_a_song_plays_the_album_from_it()
    {
        IReadOnlyList<Track> tracks = [Track("t1", 1, null), Track("t2", 2, null), Track("t3", 3, null)];
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns(tracks);
        await _sut.LoadAsync(Discovery, TestContext.Current.CancellationToken);

        _sut.PlayTrackCommand.Execute(_sut.Discs[0].Tracks[1]);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(tracks)), 1);
    }

    [Fact]
    public async Task Play_plays_the_whole_album()
    {
        IReadOnlyList<Track> tracks = [Track("t1", 1, null), Track("t2", 2, null)];
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns(tracks);
        await _sut.LoadAsync(Discovery, TestContext.Current.CancellationToken);

        _sut.PlayCommand.Execute(null);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(tracks)), 0);
    }

    [Fact]
    public async Task A_loading_failure_is_reported_and_nothing_can_be_played()
    {
        var failure = new JellyfinException("offline");
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<Track>>(failure));

        await _sut.LoadAsync(Discovery, TestContext.Current.CancellationToken);

        _sut.LoadFailed.Should().BeTrue();
        _sut.Discs.Should().BeEmpty();
        _sut.PlayCommand.CanExecute(null).Should().BeFalse();
        _errors.Received(1).Report(failure, Operations.LoadAlbum);
    }
}
