using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.Tests.Playback;

public sealed class TrackDragTests
{
    private static readonly Track[] Songs = [new() { Id = "t1", Name = "One" }, new() { Id = "t2", Name = "Two" }];
    private static readonly Album Discovery = new() { Id = "al1", Name = "Discovery" };
    private static readonly Playlist Mix = new() { Id = "p1", Name = "Mix" };

    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly TrackDrag _sut;

    public TrackDragTests()
    {
        var strings = Substitute.For<IStringResources>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var addToPlaylist = new AddToPlaylist(_playlists, Substitute.For<IDialogService>(), Substitute.For<INotificationService>(), _errors, strings, new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger());
        _sut = new TrackDrag(_library, _player, addToPlaylist, _errors);
    }

    [Fact]
    public async Task Dropped_tracks_are_inserted_into_the_queue_at_the_position()
    {
        var token = _sut.Begin(Songs);

        await _sut.DropOnQueueAsync(token, 2);

        _player.Received(1).InsertIntoQueue(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(Songs)), 2);
    }

    [Fact]
    public async Task A_dropped_album_loads_its_tracks_and_adds_them_to_the_playlist()
    {
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns(Songs);
        var token = _sut.BeginAlbum(Discovery);

        await _sut.DropOnPlaylistAsync(token, Mix);

        await _playlists.Received(1).AddTracksAsync("p1", Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "t1", "t2" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stale_or_unknown_token_drops_nothing()
    {
        var old = _sut.Begin(Songs);
        var current = _sut.Begin([Songs[0]]);

        await _sut.DropOnQueueAsync(old, 0);
        await _sut.DropOnQueueAsync(null, 0);
        await _sut.DropOnQueueAsync("other", 0);

        _sut.IsCurrent(current).Should().BeTrue();
        _sut.IsCurrent(old).Should().BeFalse();
        _player.DidNotReceiveWithAnyArgs().InsertIntoQueue(default!, default);
    }

    [Fact]
    public async Task A_failed_album_load_is_reported()
    {
        var failure = new JellyfinException("down");
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw failure);
        var token = _sut.BeginAlbum(Discovery);

        await _sut.DropOnQueueAsync(token, 0);

        _errors.Received(1).Report(failure, Operations.Playback);
        _player.DidNotReceiveWithAnyArgs().InsertIntoQueue(default!, default);
    }

    [Fact]
    public async Task An_empty_album_adds_nothing()
    {
        _library.GetAlbumTracksAsync("al1", Arg.Any<CancellationToken>()).Returns([]);

        await _sut.DropOnPlaylistAsync(_sut.BeginAlbum(Discovery), Mix);

        await _playlists.DidNotReceiveWithAnyArgs().AddTracksAsync(default!, default!, TestContext.Current.CancellationToken);
    }
}
