using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class FoldersViewModelTests
{
    private static readonly Folder Artists = new("f1", "Artists");
    private static readonly Folder DaftPunk = new("f2", "Daft Punk");
    private static readonly Track Intro = new() { Id = "t1", Name = "Intro", AlbumId = "al1", AlbumImageTag = "tag" };
    private static readonly Track Outro = new() { Id = "t2", Name = "Outro" };

    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly FoldersViewModel _sut;

    public FoldersViewModelTests()
    {
        var strings = Substitute.For<IStringResources>();
        strings.GetString("Folders_Top").Returns("Library");
        _library.GetFolderAsync(null, Arg.Any<CancellationToken>()).Returns(new FolderContent([Artists], []));
        _library.GetFolderAsync("f1", Arg.Any<CancellationToken>()).Returns(new FolderContent([DaftPunk], [Intro, Outro]));
        _library.GetFolderAsync("f2", Arg.Any<CancellationToken>()).Returns(new FolderContent([], []));
        _sut = new FoldersViewModel(_library, _player, _errors, strings);
    }

    [Fact]
    public async Task The_top_lists_the_library_folders_and_cannot_be_played()
    {
        await _sut.LoadAsync(TestContext.Current.CancellationToken);

        _sut.Path.Should().Equal(new FolderCrumb(null, "Library"));
        _sut.Folders.Should().Equal(Artists);
        _sut.IsInFolder.Should().BeFalse();
        _sut.PlayCommand.CanExecute(null).Should().BeFalse();
        _sut.GoUpCommand.CanExecute(null).Should().BeFalse();
        _sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Opening_a_folder_extends_the_path_and_shows_subfolders_then_tracks()
    {
        await _sut.LoadAsync(TestContext.Current.CancellationToken);

        await _sut.OpenCommand.ExecuteAsync(Artists);

        _sut.Path.Select(c => c.Name).Should().Equal("Library", "Artists");
        _sut.Folders.Should().Equal(DaftPunk);
        _sut.Tracks.Select(t => t.Track).Should().Equal(Intro, Outro);
        _sut.Tracks[0].Cover.Should().NotBeNull();
        _sut.Tracks[1].Cover.Should().BeNull();
        _sut.IsInFolder.Should().BeTrue();
        _sut.HasFolders.Should().BeTrue();
        _sut.HasTracks.Should().BeTrue();
        _sut.PlayCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task The_breadcrumb_and_go_up_return_to_a_folder_of_the_path()
    {
        await _sut.LoadAsync(TestContext.Current.CancellationToken);
        await _sut.OpenCommand.ExecuteAsync(Artists);
        await _sut.OpenCommand.ExecuteAsync(DaftPunk);
        _sut.IsEmpty.Should().BeTrue();
        _sut.HasFolders.Should().BeFalse();

        await _sut.GoUpCommand.ExecuteAsync(null);
        _sut.Path.Select(c => c.Name).Should().Equal("Library", "Artists");

        await _sut.GoToCommand.ExecuteAsync(_sut.Path[0]);
        _sut.Path.Should().ContainSingle();
        _sut.Folders.Should().Equal(Artists);
    }

    [Fact]
    public async Task Play_and_add_to_queue_take_the_folder_with_its_subfolders()
    {
        Track[] all = [Intro, Outro, new() { Id = "t3", Name = "Deep" }];
        _library.GetFolderTracksAsync("f1", FoldersViewModel.TrackLimit, Arg.Any<CancellationToken>()).Returns(all);
        await _sut.LoadAsync(TestContext.Current.CancellationToken);
        await _sut.OpenCommand.ExecuteAsync(Artists);

        await _sut.PlayCommand.ExecuteAsync(null);
        await _sut.AddToQueueCommand.ExecuteAsync(null);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(all)));
        _player.Received(1).AddToQueue(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(all)));
    }

    [Fact]
    public async Task Playing_a_track_plays_the_folder_tracks_from_it()
    {
        await _sut.LoadAsync(TestContext.Current.CancellationToken);
        await _sut.OpenCommand.ExecuteAsync(Artists);

        _sut.PlayTrackCommand.Execute(_sut.Tracks[1]);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(new[] { Intro, Outro })), 1);
    }

    [Fact]
    public async Task A_failed_load_is_reported()
    {
        var failure = new JellyfinException("down");
        _library.GetFolderAsync("f1", Arg.Any<CancellationToken>()).Returns<FolderContent>(_ => throw failure);
        await _sut.LoadAsync(TestContext.Current.CancellationToken);

        await _sut.OpenCommand.ExecuteAsync(Artists);

        _sut.LoadFailed.Should().BeTrue();
        _sut.IsEmpty.Should().BeFalse();
        _errors.Received(1).Report(failure, Operations.LoadFolder);
    }

    [Fact]
    public async Task A_failed_folder_play_is_reported()
    {
        var failure = new JellyfinException("down");
        _library.GetFolderTracksAsync("f1", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw failure);
        await _sut.LoadAsync(TestContext.Current.CancellationToken);
        await _sut.OpenCommand.ExecuteAsync(Artists);

        await _sut.PlayCommand.ExecuteAsync(null);

        _errors.Received(1).Report(failure, Operations.Playback);
        _player.DidNotReceiveWithAnyArgs().Play(default!);
    }
}
