using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class PlaylistDetailViewModelTests
{
    private readonly PlayingTrack _playing = new();
    private const string Id = "pl1";

    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly PlaylistDetailViewModel _sut;

    public PlaylistDetailViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Album_TrackCount").Returns("{0} tracks");
        _strings.GetString("Album_TrackCountOne").Returns("{0} track");
        _strings.GetString("Playlist_DeleteMessage").Returns("Delete {0}?");
        _playlists.GetPlaylistAsync(Id, Arg.Any<CancellationToken>()).Returns(new Playlist { Id = Id, Name = "Chill", PrimaryImageTag = "img" });
        _playlists.GetEntriesAsync(Id, Arg.Any<CancellationToken>()).Returns(Entries("a", "b", "c"));
        _messenger.Register<PlaylistDetailViewModelTests, PlaylistChangedMessage>(this, static (r, m) => r._sent.Add(m));
        _messenger.Register<PlaylistDetailViewModelTests, PlaylistDeletedMessage>(this, static (r, m) => r._sent.Add(m));
        _sut = new PlaylistDetailViewModel(_playlists, _player, _navigation, _dialogs, _errors, _strings, new FavouritesService(_library, _messenger, _errors), _messenger, _playing.Current, _settings);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlaylistEntry[] Entries(params string[] ids) => ids
        .Select(id => new PlaylistEntry($"e-{id}", new Track
        {
            Id = id,
            Name = $"Song {id}",
            AlbumId = "al",
            AlbumName = "Album",
            AlbumImageTag = "t",
            Artists = [new ArtistRef("x", "Artist")],
            Duration = TimeSpan.FromSeconds(60),
        }))
        .ToArray();

    private IEnumerable<string> Order => _sut.Entries.Select(e => e.Entry.Track.Id);

    private PlaylistEntryItem Row(string id) => _sut.Entries.Single(e => e.Entry.Track.Id == id);

    private readonly List<object> _sent = [];

    /// <summary>The messages of type <typeparamref name="T"/> sent since the test began listening.</summary>
    private IEnumerable<T> Sent<T>()
        where T : class => _sent.OfType<T>();

    /// <summary>Loads the page; messages sent while loading are not counted as the test's.</summary>
    private async Task LoadAsync()
    {
        await _sut.LoadAsync(Id, Ct);
        _sent.Clear();
    }

    [Fact]
    public async Task Load_shows_the_playlist_and_its_entries()
    {
        await LoadAsync();

        _sut.Name.Should().Be("Chill");
        _sut.Image!.ItemId.Should().Be(Id);
        Order.Should().Equal("a", "b", "c");
        _sut.Details.Should().Be("3 tracks · 3:00");
        _sut.Entries.Select(e => e.Number).Should().Equal("1", "2", "3");
        _sut.Entries[0].Artist.Should().Be("Artist");
        _sut.Entries[0].Album.Should().Be("Album");
        _sut.Entries[0].AccessibleName.Should().Be("1, Song a, Artist, Album, 1:00");
        _sut.PlayCommand.CanExecute(null).Should().BeTrue();
        _sut.RenameCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_playlist_cannot_be_played()
    {
        _playlists.GetEntriesAsync(Id, Arg.Any<CancellationToken>()).Returns([]);

        await LoadAsync();

        _sut.HasEntries.Should().BeFalse();
        _sut.Details.Should().Be("0 tracks");
        _sut.PlayCommand.CanExecute(null).Should().BeFalse();
        _sut.ShuffleCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_load_is_reported()
    {
        _playlists.GetPlaylistAsync(Id, Arg.Any<CancellationToken>()).Returns<Playlist>(_ => throw new JellyfinException("boom"));

        await LoadAsync();

        _sut.LoadFailed.Should().BeTrue();
        _sut.HasPlaylist.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadPlaylist);
    }

    [Fact]
    public async Task Cancelled_loads_are_quiet()
    {
        _playlists.GetPlaylistAsync(Id, Arg.Any<CancellationToken>()).Returns<Playlist>(_ => throw new OperationCanceledException());

        await LoadAsync();

        _sut.LoadFailed.Should().BeFalse();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Play_shuffle_queue_and_a_row_use_the_entries_in_order()
    {
        await LoadAsync();

        _sut.PlayCommand.Execute(null);
        _sut.ShuffleCommand.Execute(null);
        _sut.AddToQueueCommand.Execute(null);
        _sut.PlayEntryCommand.Execute(Row("b"));

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Select(x => x.Id).SequenceEqual(new[] { "a", "b", "c" })), 0);
        _player.Received(1).Shuffle(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 3));
        _player.Received(1).AddToQueue(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 3));
        _player.Received(1).Play(Arg.Any<IReadOnlyList<Track>>(), 1);
    }

    [Fact]
    public async Task Rename_saves_the_trimmed_name()
    {
        await LoadAsync();
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs(" Calm ");

        await _sut.RenameCommand.ExecuteAsync(null);

        await _playlists.Received(1).RenameAsync(Id, " Calm ", Arg.Any<CancellationToken>());
        _sut.Name.Should().Be("Calm");
        Sent<PlaylistChangedMessage>().Should().ContainSingle().Which.Playlist.Name.Should().Be("Calm", "the navigation pane shows the new name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("Chill")]
    public async Task Cancelled_blank_or_unchanged_names_are_not_sent(string? name)
    {
        await LoadAsync();
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs(name);

        await _sut.RenameCommand.ExecuteAsync(null);

        await _playlists.DidNotReceiveWithAnyArgs().RenameAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_failed_rename_keeps_the_name_and_is_reported()
    {
        await LoadAsync();
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs("Calm");
        _playlists.RenameAsync(default!, default!, Ct).ReturnsForAnyArgs(_ => throw new JellyfinException("boom"));

        await _sut.RenameCommand.ExecuteAsync(null);

        _sut.Name.Should().Be("Chill");
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.EditPlaylist);
    }

    [Fact]
    public async Task Delete_asks_first_then_deletes_and_goes_back()
    {
        await LoadAsync();
        _dialogs.ConfirmAsync("Playlist_DeleteTitle", "Delete Chill?", "Playlist_DeleteConfirm", "Dialog_Cancel").Returns(true);

        await _sut.DeleteCommand.ExecuteAsync(null);

        await _playlists.Received(1).DeleteAsync(Id, Arg.Any<CancellationToken>());
        _navigation.Received(1).GoBack();
        Sent<PlaylistDeletedMessage>().Should().Equal(new PlaylistDeletedMessage(Id));
    }

    [Fact]
    public async Task The_heart_shows_the_servers_state_and_saves_a_change()
    {
        _playlists.GetPlaylistAsync(Id, Arg.Any<CancellationToken>()).Returns(new Playlist { Id = Id, Name = "Chill", IsFavorite = true });
        await LoadAsync();
        _sut.IsFavourite.Should().BeTrue();

        await _sut.ToggleFavouriteCommand.ExecuteAsync(null);

        _sut.IsFavourite.Should().BeFalse();
        await _library.Received(1).SetFavouriteAsync(Id, false, Arg.Any<CancellationToken>());
        Sent<PlaylistChangedMessage>().Should().ContainSingle().Which.Playlist.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task A_refused_heart_goes_back_and_tells_nobody()
    {
        await LoadAsync();
        _library.SetFavouriteAsync(Id, true, Arg.Any<CancellationToken>()).Returns(Task.FromException(new JellyfinException("no")));

        await _sut.ToggleFavouriteCommand.ExecuteAsync(null);

        _sut.IsFavourite.Should().BeFalse();
        Sent<PlaylistChangedMessage>().Should().BeEmpty();
    }

    [Fact]
    public async Task Declining_the_confirmation_deletes_nothing()
    {
        await LoadAsync();

        await _sut.DeleteCommand.ExecuteAsync(null);

        await _playlists.DidNotReceiveWithAnyArgs().DeleteAsync(default!, Ct);
        _navigation.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task A_failed_delete_stays_on_the_page()
    {
        await LoadAsync();
        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(true);
        _playlists.DeleteAsync(default!, Ct).ReturnsForAnyArgs(_ => throw new JellyfinException("boom"));

        await _sut.DeleteCommand.ExecuteAsync(null);

        _navigation.DidNotReceive().GoBack();
        _sut.HasPlaylist.Should().BeTrue();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.EditPlaylist);
    }

    [Fact]
    public async Task Remove_takes_the_entry_out_at_once_and_on_the_server()
    {
        await LoadAsync();

        await _sut.RemoveCommand.ExecuteAsync(Row("b"));

        Order.Should().Equal("a", "c");
        _sut.Details.Should().Be("2 tracks · 2:00");
        await _playlists.Received(1).RemoveEntriesAsync(Id, Arg.Is<IReadOnlyList<string>>(ids => ids.Single() == "e-b"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_entries_tells_the_navigation_pane_the_new_count_and_length()
    {
        await LoadAsync();

        await _sut.RemoveCommand.ExecuteAsync(Row("b"));

        Sent<PlaylistChangedMessage>().Should().ContainSingle().Which.Playlist.Should().BeEquivalentTo(
            new { Id, TrackCount = 2, Duration = TimeSpan.FromMinutes(2) }, "the pane shows them for a favourite (#299)");
    }

    [Fact]
    public async Task Loading_tells_the_pane_only_when_the_count_or_length_differs()
    {
        _playlists.GetPlaylistAsync(Id, Arg.Any<CancellationToken>()).Returns(new Playlist { Id = Id, Name = "Chill", TrackCount = 3, Duration = TimeSpan.FromMinutes(3) });

        await _sut.LoadAsync(Id, Ct);

        Sent<PlaylistChangedMessage>().Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_a_row_that_is_gone_does_nothing()
    {
        await LoadAsync();
        var b = Row("b");
        await _sut.RemoveCommand.ExecuteAsync(b);

        await _sut.RemoveCommand.ExecuteAsync(b);

        await _playlists.ReceivedWithAnyArgs(1).RemoveEntriesAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task Several_selected_entries_go_in_one_request()
    {
        await LoadAsync();
        var a = Row("a");
        var c = Row("c");
        await _sut.RemoveCommand.ExecuteAsync(c);
        _playlists.ClearReceivedCalls();

        await _sut.RemoveEntriesAsync([a, c, Row("b")]);

        Order.Should().BeEmpty();
        _sut.HasEntries.Should().BeFalse();
        await _playlists.Received(1).RemoveEntriesAsync(Id, Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "e-a", "e-b" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Move_up_and_down_move_the_row_and_send_the_new_index()
    {
        await LoadAsync();

        await _sut.MoveDownCommand.ExecuteAsync(Row("a"));
        Order.Should().Equal("b", "a", "c");
        await _playlists.Received(1).MoveEntryAsync(Id, "e-a", 1, Arg.Any<CancellationToken>());

        await _sut.MoveUpCommand.ExecuteAsync(Row("c"));
        Order.Should().Equal("b", "c", "a");
        await _playlists.Received(1).MoveEntryAsync(Id, "e-c", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moves_past_the_ends_do_nothing()
    {
        await LoadAsync();

        await _sut.MoveUpCommand.ExecuteAsync(Row("a"));
        await _sut.MoveDownCommand.ExecuteAsync(Row("c"));

        Order.Should().Equal("a", "b", "c");
        await _playlists.DidNotReceiveWithAnyArgs().MoveEntryAsync(default!, default!, default, Ct);
    }

    [Fact]
    public async Task A_drag_sends_the_new_index()
    {
        await LoadAsync();
        var c = Row("c");
        _sut.Entries.Move(2, 0);

        await _sut.MoveEntryAsync(c, 0);

        await _playlists.Received(1).MoveEntryAsync(Id, "e-c", 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_refused_edit_reloads_the_entries_and_is_reported()
    {
        await LoadAsync();
        _playlists.MoveEntryAsync(default!, default!, default, Ct).ReturnsForAnyArgs(_ => throw new JellyfinException("boom"));

        await _sut.MoveDownCommand.ExecuteAsync(Row("a"));

        Order.Should().Equal("a", "b", "c");
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.EditPlaylist);
    }

    [Fact]
    public async Task When_the_reload_fails_too_the_list_stays()
    {
        await LoadAsync();
        _playlists.RemoveEntriesAsync(default!, default!, Ct).ReturnsForAnyArgs(_ => throw new JellyfinException("boom"));
        _playlists.GetEntriesAsync(Id, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<PlaylistEntry>>(_ => throw new JellyfinException("offline"));

        await _sut.RemoveCommand.ExecuteAsync(Row("a"));

        Order.Should().Equal("b", "c");
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.EditPlaylist);
    }

    [Fact]
    public async Task Edits_before_the_playlist_loaded_are_ignored()
    {
        var row = new PlaylistEntryItem(Entries("x")[0]);

        await _sut.MoveEntryAsync(row, 0);

        await _playlists.DidNotReceiveWithAnyArgs().MoveEntryAsync(default!, default!, default, Ct);
    }

    [Fact]
    public async Task The_positions_follow_moves_drags_and_removals()
    {
        await LoadAsync();

        await _sut.MoveDownCommand.ExecuteAsync(Row("a"));
        Row("b").Number.Should().Be("1");
        Row("a").Number.Should().Be("2");

        // A drag: the list moved the row itself, then the page tells the view model.
        _sut.Entries.Move(2, 0);
        await _sut.MoveEntryAsync(Row("c"), 0);
        _sut.Entries.Select(e => e.Number).Should().Equal("1", "2", "3");
        Row("c").Number.Should().Be("1");

        await _sut.RemoveCommand.ExecuteAsync(Row("c"));
        _sut.Entries.Select(e => e.Number).Should().Equal("1", "2");
    }

    [Fact]
    public async Task A_heart_toggles_on_the_server_and_rolls_back_when_refused()
    {
        await LoadAsync();

        await _sut.ToggleEntryFavouriteCommand.ExecuteAsync(Row("a"));
        Row("a").IsFavourite.Should().BeTrue();
        await _library.Received(1).SetFavouriteAsync("a", true, Arg.Any<CancellationToken>());

        _library.SetFavouriteAsync("a", false, Arg.Any<CancellationToken>()).Returns(Task.FromException(new JellyfinException("no")));
        await _sut.ToggleEntryFavouriteCommand.ExecuteAsync(Row("a"));
        Row("a").IsFavourite.Should().BeTrue();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.SetFavourite);
    }

    [Fact]
    public async Task Hearts_toggled_elsewhere_show_in_the_rows()
    {
        await LoadAsync();

        _messenger.Send(new FavouriteChangedMessage("b", true));

        _sut.Entries.Select(e => e.IsFavourite).Should().Equal(false, true, false);
    }

    [Fact]
    public async Task The_playing_entry_is_marked_also_after_a_move()
    {
        _playing.Play("b");
        await LoadAsync();
        _sut.Entries.Select(e => e.IsCurrent).Should().Equal(false, true, false);

        await _sut.MoveUpCommand.ExecuteAsync(Row("b"));

        Row("b").IsCurrent.Should().BeTrue();
        _sut.Entries.Count(e => e.IsCurrent).Should().Be(1);
    }

    [Fact]
    public async Task Reverse_order_shows_and_plays_the_last_entry_first_and_keeps_the_positions()
    {
        await LoadAsync();
        _sut.IsReversed.Should().BeFalse();

        _sut.ToggleReverseCommand.Execute(null);

        _sut.IsReversed.Should().BeTrue();
        Order.Should().Equal("c", "b", "a");
        _sut.Entries.Select(e => e.Number).Should().Equal("3", "2", "1");
        _sut.PlayCommand.Execute(null);
        _sut.PlayEntryCommand.Execute(Row("b"));
        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Select(x => x.Id).SequenceEqual(new[] { "c", "b", "a" })), 0);
        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Select(x => x.Id).SequenceEqual(new[] { "c", "b", "a" })), 1);

        _sut.ToggleReverseCommand.Execute(null);

        Order.Should().Equal("a", "b", "c");
        _sut.Entries.Select(e => e.Number).Should().Equal("1", "2", "3");
    }

    [Fact]
    public async Task Reverse_order_is_remembered_for_that_playlist_only()
    {
        _settings.Update(s => s with { Browse = s.Browse with { ReversedPlaylists = ["other"] } });
        await LoadAsync();

        _sut.ToggleReverseCommand.Execute(null);
        _settings.Current.Browse.ReversedPlaylists.Should().Equal("other", Id);

        var reopened = new PlaylistDetailViewModel(_playlists, _player, _navigation, _dialogs, _errors, _strings, new FavouritesService(_library, _messenger, _errors), _messenger, _playing.Current, _settings);
        await reopened.LoadAsync(Id, Ct);
        reopened.IsReversed.Should().BeTrue();
        reopened.Entries.Select(e => e.Entry.Track.Id).Should().Equal("c", "b", "a");

        reopened.ToggleReverseCommand.Execute(null);
        _settings.Current.Browse.ReversedPlaylists.Should().Equal("other");
    }

    [Fact]
    public async Task While_reversed_entries_cannot_be_moved_but_can_be_removed()
    {
        await LoadAsync();
        _sut.ToggleReverseCommand.Execute(null);

        _sut.CanReorder.Should().BeFalse();
        _sut.MoveUpCommand.CanExecute(Row("a")).Should().BeFalse();
        await _sut.MoveEntryAsync(Row("a"), 0);
        await _sut.RemoveCommand.ExecuteAsync(Row("b"));

        await _playlists.DidNotReceiveWithAnyArgs().MoveEntryAsync(default!, default!, default, Ct);
        await _playlists.Received(1).RemoveEntriesAsync(Id, Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "e-b" })), Arg.Any<CancellationToken>());
        Order.Should().Equal("c", "a");
        _sut.Entries.Select(e => e.Number).Should().Equal("2", "1");
    }

    [Fact]
    public async Task A_failed_edit_reloads_the_entries_still_reversed()
    {
        await LoadAsync();
        _sut.ToggleReverseCommand.Execute(null);
        _playlists.RemoveEntriesAsync(default!, default!, Ct).ReturnsForAnyArgs(Task.FromException(new JellyfinException("no")));

        await _sut.RemoveCommand.ExecuteAsync(Row("b"));

        Order.Should().Equal("c", "b", "a");
        _sut.Entries.Select(e => e.Number).Should().Equal("3", "2", "1");
    }

    [Fact]
    public async Task The_toggle_button_reverses_through_the_property_too()
    {
        await LoadAsync();

        _sut.IsReversed = true;

        Order.Should().Equal("c", "b", "a");
        _settings.Current.Browse.ReversedPlaylists.Should().Equal(Id);
    }

    [Fact]
    public async Task Deleting_a_reversed_playlist_forgets_its_order()
    {
        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(true);
        await LoadAsync();
        _sut.ToggleReverseCommand.Execute(null);

        await _sut.DeleteCommand.ExecuteAsync(null);

        _settings.Current.Browse.ReversedPlaylists.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_row_shows_its_albums_cover_at_the_display_scale()
    {
        var entries = Entries("a", "b");
        entries[1] = entries[1] with { Track = entries[1].Track with { AlbumId = "other", AlbumImageTag = null } };
        _playlists.GetEntriesAsync(Id, Arg.Any<CancellationToken>()).Returns(entries);
        _sut.DisplayScale = 2;

        await LoadAsync();

        Row("a").Cover.Should().Be(new ImageRequest("al", ImageKind.Primary, "t", 80));
        Row("b").Cover.Should().BeNull("an album without a cover shows the placeholder");
    }
}
