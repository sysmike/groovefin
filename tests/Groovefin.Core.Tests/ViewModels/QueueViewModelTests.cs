using Groovefin.Core.Api;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class QueueViewModelTests : IDisposable
{
    private readonly QueueManager _queue = new(new Random(1));
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly QueueViewModel _sut;

    public QueueViewModelTests()
    {
        _player.Queue.Returns(_queue);
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() == "Queue_SavedMessage" ? "Saved {0}" : call.Arg<string>());
        _sut = new QueueViewModel(_player, _playlists, _dialogs, _notifications, _errors, _strings);
    }

    public void Dispose() => _sut.Dispose();

    private static Track[] Tracks(params string[] ids) => ids
        .Select(id => new Track
        {
            Id = id,
            Name = $"Song {id}",
            AlbumId = $"album-{id}",
            AlbumImageTag = "tag",
            Artists = [new ArtistRef("x", "Artist X")],
            Duration = TimeSpan.FromSeconds(185),
        })
        .ToArray();

    private IEnumerable<string> UpcomingIds => _sut.Upcoming.Select(e => e.Track.Id);

    private QueueEntry UpcomingEntry(string trackId) => _sut.Upcoming.Single(e => e.Track.Id == trackId);

    [Fact]
    public void An_empty_queue_shows_nothing()
    {
        _sut.IsEmpty.Should().BeTrue();
        _sut.HasCurrent.Should().BeFalse();
        _sut.HasUpcoming.Should().BeFalse();
        _sut.HasHistory.Should().BeFalse();
        _sut.ClearUpcomingCommand.CanExecute(null).Should().BeFalse();
        _sut.SaveAsPlaylistCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Shows_the_playing_entry_and_the_upcoming_ones_in_play_order()
    {
        _queue.PlayNow(Tracks("a", "b", "c"), startIndex: 1);

        _sut.Current!.Track.Id.Should().Be("b");
        UpcomingIds.Should().Equal("c");
        _sut.IsEmpty.Should().BeFalse();
        _sut.ClearUpcomingCommand.CanExecute(null).Should().BeTrue();
        _sut.SaveAsPlaylistCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Rows_describe_the_track_for_people_and_screen_readers()
    {
        _queue.PlayNow(Tracks("a", "b"));

        var row = _sut.Upcoming[0];
        row.Title.Should().Be("Song b");
        row.Artist.Should().Be("Artist X");
        row.Duration.Should().Be("3:05");
        row.AccessibleName.Should().Be("Song b, Artist X, 3:05");
        row.Cover.Should().Be(new ImageRequest("album-b", ImageKind.Primary, "tag", 40));
    }

    [Fact]
    public void Covers_follow_the_display_scale()
    {
        _queue.PlayNow(Tracks("a", "b"));

        _sut.DisplayScale = 2;

        _sut.Current!.Cover!.Width.Should().Be(80);
        _sut.Upcoming[0].Cover!.Width.Should().Be(80);
    }

    [Fact]
    public void History_lists_played_entries_newest_first()
    {
        _queue.PlayNow(Tracks("a", "b", "c"));
        _queue.Next();
        _queue.Next();

        _sut.History.Select(e => e.Track.Id).Should().Equal("b", "a");
        _sut.HasHistory.Should().BeTrue();
    }

    [Fact]
    public void History_can_hold_the_same_entry_twice()
    {
        _queue.PlayNow(Tracks("a"));
        _queue.Repeat = RepeatMode.One;
        _queue.TrackEnded();
        _queue.TrackEnded();

        _sut.History.Select(e => e.Track.Id).Should().Equal("a", "a");
    }

    [Fact]
    public void Queue_changes_keep_the_rows_of_entries_that_stay()
    {
        _queue.PlayNow(Tracks("a", "b", "c", "d"));
        var c = UpcomingEntry("c");

        _queue.Move(c.Id, 1);
        _queue.Remove(UpcomingEntry("d").Id);
        _queue.AddLast(Tracks("e"));

        UpcomingIds.Should().Equal("c", "b", "e");
        _sut.Upcoming[0].Should().BeSameAs(c);
    }

    [Fact]
    public void Picking_an_entry_plays_it()
    {
        _queue.PlayNow(Tracks("a", "b"));
        var b = UpcomingEntry("b");

        _sut.PlayCommand.Execute(b);

        _player.Received(1).JumpTo(b.Id);
    }

    [Fact]
    public void A_history_entry_plays_again_right_away()
    {
        _queue.PlayNow(Tracks("a", "b"));
        _queue.Next();

        _sut.PlayAgainCommand.Execute(_sut.History[0]);

        Received.InOrder(() =>
        {
            _player.PlayNext(Arg.Is<IReadOnlyList<Track>>(t => t.Single().Id == "a"));
            _player.Next();
        });
    }

    [Fact]
    public void A_history_entry_after_the_queue_was_cleared_starts_a_new_queue()
    {
        _queue.PlayNow(Tracks("a"));
        _queue.Clear();

        _sut.PlayAgainCommand.Execute(_sut.History[0]);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Single().Id == "a"));
    }

    [Fact]
    public void Remove_takes_the_entry_out_of_the_queue()
    {
        _queue.PlayNow(Tracks("a", "b", "c"));

        _sut.RemoveCommand.Execute(UpcomingEntry("b"));

        UpcomingIds.Should().Equal("c");
    }

    [Fact]
    public void Move_up_and_down_stay_within_the_upcoming_entries()
    {
        _queue.PlayNow(Tracks("a", "b", "c"));

        _sut.MoveUpCommand.Execute(UpcomingEntry("b"));
        UpcomingIds.Should().Equal("b", "c");

        _sut.MoveDownCommand.Execute(UpcomingEntry("b"));
        UpcomingIds.Should().Equal("c", "b");

        _sut.MoveDownCommand.Execute(UpcomingEntry("b"));
        _sut.MoveUpCommand.Execute(UpcomingEntry("c"));
        UpcomingIds.Should().Equal("c", "b");
        _sut.Current!.Track.Id.Should().Be("a");
    }

    [Fact]
    public void Moving_an_entry_that_left_the_queue_does_nothing()
    {
        _queue.PlayNow(Tracks("a", "b"));
        var b = UpcomingEntry("b");
        _queue.Remove(b.Id);

        _sut.MoveUpCommand.Execute(b);
        _sut.MoveDownCommand.Execute(b);

        UpcomingIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, new[] { "d", "b", "c" })]
    [InlineData(1, new[] { "b", "d", "c" })]
    [InlineData(9, new[] { "b", "c", "d" })]
    [InlineData(-3, new[] { "d", "b", "c" })]
    public void A_drag_moves_the_entry_within_the_upcoming_entries(int upcomingIndex, string[] expected)
    {
        _queue.PlayNow(Tracks("a", "b", "c", "d"));

        _sut.MoveUpcoming(UpcomingEntry("d"), upcomingIndex);

        UpcomingIds.Should().Equal(expected);
        _sut.Current!.Track.Id.Should().Be("a");
    }

    [Fact]
    public void Clear_removes_the_upcoming_entries_and_keeps_the_playing_one()
    {
        _queue.PlayNow(Tracks("a", "b", "c"));

        _sut.ClearUpcomingCommand.Execute(null);

        UpcomingIds.Should().BeEmpty();
        _sut.Current!.Track.Id.Should().Be("a");
        _sut.ClearUpcomingCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Toggle_and_close_show_and_hide_the_panel()
    {
        _sut.ToggleCommand.Execute(null);
        _sut.IsOpen.Should().BeTrue();

        _sut.ToggleCommand.Execute(null);
        _sut.IsOpen.Should().BeFalse();

        _sut.IsOpen = true;
        _sut.CloseCommand.Execute(null);
        _sut.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Save_as_playlist_saves_the_whole_queue_in_play_order()
    {
        _queue.PlayNow(Tracks("a", "b", "c"), startIndex: 1);
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs(" Road trip ");

        await _sut.SaveAsPlaylistCommand.ExecuteAsync(null);

        await _playlists.Received(1).CreateAsync(
            " Road trip ",
            Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "a", "b", "c" })),
            Arg.Any<CancellationToken>());
        _notifications.Received(1).Show(new AppNotification(NotificationSeverity.Success, "Queue_SavedTitle", "Saved Road trip"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Cancelling_or_a_blank_name_saves_nothing(string? name)
    {
        _queue.PlayNow(Tracks("a"));
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs(name);

        await _sut.SaveAsPlaylistCommand.ExecuteAsync(null);

        await _playlists.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_failed_save_is_reported()
    {
        _queue.PlayNow(Tracks("a"));
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs("Mix");
        var failure = new JellyfinException("boom");
        _playlists.CreateAsync(default!, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs<string>(_ => throw failure);

        await _sut.SaveAsPlaylistCommand.ExecuteAsync(null);

        _errors.Received(1).Report(failure, Operations.SavePlaylist);
        _notifications.DidNotReceiveWithAnyArgs().Show(default!);
    }

    [Fact]
    public void Dispose_stops_following_the_queue()
    {
        _sut.Dispose();

        _queue.PlayNow(Tracks("a"));

        _sut.HasCurrent.Should().BeFalse();
    }
}
