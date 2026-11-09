using Groovefin.Core.Api;
using Groovefin.Core.JumpLists;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;

namespace Groovefin.Core.Tests.JumpLists;

public sealed class JumpListActionsTests
{
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly JumpListActions _sut;

    public JumpListActionsTests() => _sut = new JumpListActions(_player, _library, _playlists, _errors);

    private static Track Song(string id) => new() { Id = id, Name = id };

    [Fact]
    public async Task Play_pause_and_next_drive_the_player()
    {
        await _sut.RunAsync(JumpListCommand.PlayPause);
        await _sut.RunAsync(JumpListCommand.Next);

        _player.Received(1).TogglePlayPause();
        _player.Received(1).Next();
    }

    [Fact]
    public async Task Shuffle_all_plays_random_songs_from_the_library()
    {
        Track[] songs = [Song("a"), Song("b")];
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<Track>(songs, 2, 0));

        await _sut.RunAsync(JumpListCommand.ShuffleAll);

        await _library.Received(1).GetTracksAsync(
            Arg.Is<TrackQuery>(q => q.SortBy == TrackSortField.Random && q.Limit == JumpListActions.ShuffleAllCount),
            Arg.Any<CancellationToken>());
        _player.Received(1).Play(songs);
    }

    [Fact]
    public async Task A_playlist_entry_plays_the_playlist()
    {
        _playlists.GetEntriesAsync("p1", Arg.Any<CancellationToken>()).Returns([new PlaylistEntry("e1", Song("a"))]);

        await _sut.RunAsync(JumpListCommand.ForPlaylist("p1"));

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 1 && t[0].Id == "a"));
    }

    [Fact]
    public async Task An_empty_playlist_plays_nothing_and_a_failure_is_shown()
    {
        _playlists.GetEntriesAsync("empty", Arg.Any<CancellationToken>()).Returns([]);
        _playlists.GetEntriesAsync("gone", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<PlaylistEntry>>(new JellyfinException("not found")));

        await _sut.RunAsync(JumpListCommand.ForPlaylist("empty"));
        await _sut.RunAsync(JumpListCommand.ForPlaylist("gone"));

        _player.DidNotReceiveWithAnyArgs().Play(default!);
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.Playback);
    }
}
