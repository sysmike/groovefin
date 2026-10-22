using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;

namespace Groovefin.Core.Tests.Playback;

public sealed class QuickPlayTests
{
    private static readonly Track First = new() { Id = "t1", Name = "One More Time" };
    private static readonly Track Second = new() { Id = "t2", Name = "Aerodynamic" };

    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly QuickPlay _sut;

    public QuickPlayTests() => _sut = new QuickPlay(_library, _player, _errors);

    [Fact]
    public async Task Playing_an_album_plays_its_tracks_from_the_start()
    {
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns([First, Second]);

        await _sut.PlayAlbumAsync(new Album { Id = "album-1", Name = "Discovery" });

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(new[] { First, Second })));
    }

    [Fact]
    public async Task Playing_an_artist_plays_the_artists_tracks()
    {
        _library.GetArtistTracksAsync("artist-1", Arg.Any<CancellationToken>()).Returns([Second]);

        await _sut.PlayArtistAsync(new Artist { Id = "artist-1", Name = "Daft Punk" });

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(new[] { Second })));
    }

    [Fact]
    public async Task An_empty_album_plays_nothing()
    {
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns([]);

        await _sut.PlayAlbumAsync(new Album { Id = "album-1", Name = "Empty" });

        _player.DidNotReceiveWithAnyArgs().Play(default!);
    }

    [Fact]
    public async Task A_loading_failure_is_reported()
    {
        var failure = new JellyfinException("offline");
        _library.GetAlbumTracksAsync("album-1", Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<Track>>(failure));

        await _sut.PlayAlbumAsync(new Album { Id = "album-1", Name = "Discovery" });

        _errors.Received(1).Report(failure, Operations.Playback);
        _player.DidNotReceiveWithAnyArgs().Play(default!);
    }
}
