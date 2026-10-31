using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Servers;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Groovefin.Core.Tests.Favourites;

public sealed class FavouritePlaylistsTests : IDisposable
{
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeLogger<FavouritePlaylists> _logger = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly FavouritesService _favourites;
    private readonly FavouritePlaylists _sut;

    public FavouritePlaylistsTests()
    {
        _favourites = new FavouritesService(_library, _messenger, Substitute.For<IErrorReporter>());
        SignIn("s1", "u1");
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Chill, Road, Gym]);
        _sut = new FavouritePlaylists(_playlists, _servers, _favourites, _settings, _messenger, new ImmediateDispatcher(), _logger);
    }

    private static Playlist Chill => new() { Id = "p1", Name = "chill", IsFavorite = true };

    private static Playlist Road => new() { Id = "p2", Name = "Road trip" };

    private static Playlist Gym => new() { Id = "p3", Name = "Gym", IsFavorite = true };

    private IEnumerable<string> Names => _sut.Items.Select(p => p.Name);

    private IReadOnlyDictionary<string, IReadOnlyList<string>> Orders => _settings.Current.Appearance.FavouritePlaylistOrder;

    public void Dispose() => _sut.Dispose();

    private void SignIn(string serverId, string userId)
    {
        _servers.IsSignedIn.Returns(true);
        _servers.ActiveServer.Returns(new ServerProfile { Id = serverId, Name = serverId, Address = new Uri("https://music.example"), UserId = userId });
    }

    private void RaiseChanged() => _servers.Changed += Raise.Event();

    [Fact]
    public async Task Only_the_favourites_are_listed_by_name()
    {
        await _sut.StartAsync();

        Names.Should().Equal("chill", "Gym");
    }

    [Fact]
    public async Task Nothing_loads_while_nobody_is_signed_in()
    {
        _servers.IsSignedIn.Returns(false);

        await _sut.StartAsync();

        _sut.Items.Should().BeEmpty();
        await _playlists.DidNotReceiveWithAnyArgs().GetPlaylistsAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Signing_out_empties_the_list_and_another_user_gets_their_own()
    {
        await _sut.StartAsync();

        _servers.IsSignedIn.Returns(false);
        RaiseChanged();
        _sut.Items.Should().BeEmpty();

        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Road with { IsFavorite = true }]);
        SignIn("s2", "u7");
        RaiseChanged();
        Names.Should().Equal("Road trip");
    }

    [Fact]
    public async Task Other_server_changes_do_not_load_again()
    {
        await _sut.StartAsync();

        RaiseChanged();

        await _playlists.Received(1).GetPlaylistsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_new_favourite_takes_its_place_by_name_and_leaves_when_unfavourited()
    {
        await _sut.StartAsync();

        _messenger.Send(new PlaylistChangedMessage(Road with { IsFavorite = true }));
        Names.Should().Equal("chill", "Gym", "Road trip");

        _messenger.Send(new PlaylistChangedMessage(Road));
        Names.Should().Equal("chill", "Gym");
    }

    [Fact]
    public async Task A_renamed_favourite_moves_to_its_new_place()
    {
        await _sut.StartAsync();

        _messenger.Send(new PlaylistChangedMessage(Chill with { Name = "Zen" }));

        Names.Should().Equal("Gym", "Zen");
    }

    [Fact]
    public async Task A_deleted_or_unfavourited_playlist_leaves()
    {
        await _sut.StartAsync();

        _messenger.Send(new PlaylistDeletedMessage("p1"));
        await _favourites.SetAsync("p3", false);

        _sut.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task A_change_saved_in_this_session_wins_over_the_loaded_flag()
    {
        await _favourites.SetAsync("p2", true);
        await _favourites.SetAsync("p3", false);

        await _sut.StartAsync();

        Names.Should().Equal("chill", "Road trip");
    }

    [Fact]
    public async Task A_failed_load_is_logged_and_lists_nothing()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Playlist>>(_ => throw new JellyfinException("offline"));

        await _sut.StartAsync();

        _sut.Items.Should().BeEmpty();
        _logger.Collector.LatestRecord.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task A_load_for_the_previous_user_does_not_fill_the_list()
    {
        var first = new TaskCompletionSource<IReadOnlyList<Playlist>>();
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns(first.Task, Task.FromResult<IReadOnlyList<Playlist>>([Gym]));
        var start = _sut.StartAsync();

        SignIn("s2", "u7");
        RaiseChanged();
        first.SetResult([Chill]);
        await start;

        Names.Should().Equal("Gym");
    }

    [Fact]
    public async Task A_favourite_that_keeps_its_place_is_replaced_there()
    {
        await _sut.StartAsync();
        var changes = new List<NotifyCollectionChangedAction>();
        _sut.Items.CollectionChanged += (_, e) => changes.Add(e.Action);

        _messenger.Send(new PlaylistChangedMessage(Gym with { TrackCount = 12 }));

        changes.Should().Equal([NotifyCollectionChangedAction.Replace], "its item stays in the pane (#299)");
        _sut.Items[1].TrackCount.Should().Be(12);
    }

    [Fact]
    public async Task Tracks_added_to_a_favourite_load_its_new_count()
    {
        await _sut.StartAsync();
        _playlists.GetPlaylistAsync("p3", Arg.Any<CancellationToken>()).Returns(Gym with { TrackCount = 7, Duration = TimeSpan.FromMinutes(30) });

        _messenger.Send(new PlaylistTracksAddedMessage("p3"));

        _sut.Items.Single(p => p.Id == "p3").Should().BeEquivalentTo(new { Name = "Gym", TrackCount = 7, Duration = TimeSpan.FromMinutes(30) });
    }

    [Fact]
    public async Task Tracks_added_to_another_playlist_load_nothing()
    {
        await _sut.StartAsync();

        _messenger.Send(new PlaylistTracksAddedMessage("p2"));

        await _playlists.DidNotReceiveWithAnyArgs().GetPlaylistAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_reload_that_returns_after_unfavouriting_is_dropped()
    {
        await _sut.StartAsync();
        var reload = new TaskCompletionSource<Playlist>();
        _playlists.GetPlaylistAsync("p3", Arg.Any<CancellationToken>()).Returns(reload.Task);

        _messenger.Send(new PlaylistTracksAddedMessage("p3"));
        await _favourites.SetAsync("p3", false);
        reload.SetResult(Gym with { TrackCount = 7 });

        Names.Should().Equal("chill");
    }

    [Fact]
    public async Task A_failed_reload_keeps_the_entry()
    {
        await _sut.StartAsync();
        _playlists.GetPlaylistAsync("p3", Arg.Any<CancellationToken>()).Returns<Playlist>(_ => throw new JellyfinException("offline"));

        _messenger.Send(new PlaylistTracksAddedMessage("p3"));

        Names.Should().Equal("chill", "Gym");
        _logger.Collector.LatestRecord.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void Changes_while_signed_out_are_ignored()
    {
        _messenger.Send(new PlaylistChangedMessage(Chill));

        _sut.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Moving_a_favourite_keeps_the_order_for_this_server_and_user()
    {
        await _sut.StartAsync();

        _sut.MoveBy("p3", -1);

        Names.Should().Equal("Gym", "chill");
        Orders.Should().ContainKey("s1/u1").WhoseValue.Should().Equal("p3", "p1");
    }

    [Fact]
    public async Task The_order_comes_back_on_the_next_sign_in_and_another_user_keeps_their_own()
    {
        _settings.Update(s => s with
        {
            Appearance = s.Appearance with
            {
                FavouritePlaylistOrder = new Dictionary<string, IReadOnlyList<string>> { ["s1/u1"] = ["p3", "p1"], ["s1/u2"] = ["p1", "p3"] },
            },
        });

        await _sut.StartAsync();
        Names.Should().Equal("Gym", "chill");

        SignIn("s1", "u2");
        RaiseChanged();
        Names.Should().Equal("chill", "Gym");

        SignIn("s2", "u1");
        RaiseChanged();
        Names.Should().Equal(["chill", "Gym"], "this server has no order: by name");
    }

    [Fact]
    public async Task Once_rearranged_a_new_favourite_goes_at_the_end()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Chill, Road, Gym, new Playlist { Id = "p4", Name = "Zen", IsFavorite = true }]);
        await _sut.StartAsync();
        _sut.MoveTo("p4", 0);

        _messenger.Send(new PlaylistChangedMessage(Road with { IsFavorite = true }));

        Names.Should().Equal("Zen", "chill", "Gym", "Road trip");
    }

    [Fact]
    public async Task Favourites_not_in_the_order_follow_it_by_name()
    {
        _settings.Update(s => s with
        {
            Appearance = s.Appearance with { FavouritePlaylistOrder = new Dictionary<string, IReadOnlyList<string>> { ["s1/u1"] = ["p3", "gone"] } },
        });
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Chill, Road with { IsFavorite = true }, Gym]);

        await _sut.StartAsync();

        Names.Should().Equal("Gym", "chill", "Road trip");
    }

    [Fact]
    public async Task A_renamed_favourite_keeps_its_place_in_the_order()
    {
        await _sut.StartAsync();
        _sut.MoveBy("p1", 1);

        _messenger.Send(new PlaylistChangedMessage(Chill with { Name = "Aaa" }));

        Names.Should().Equal("Gym", "Aaa");
    }

    [Fact]
    public async Task Unfavouriting_or_deleting_takes_the_playlist_out_of_the_order()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Chill, Road with { IsFavorite = true }, Gym]);
        await _sut.StartAsync();
        _sut.MoveBy("p3", -2);

        await _favourites.SetAsync("p3", false);
        _messenger.Send(new PlaylistDeletedMessage("p2"));

        Orders["s1/u1"].Should().Equal("p1");
    }

    [Fact]
    public async Task A_favourite_dropped_on_another_goes_above_it_or_below()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Chill, Road with { IsFavorite = true }, Gym]);
        await _sut.StartAsync();

        _sut.MoveNextTo("p1", "p2", after: true);
        Names.Should().Equal("Gym", "Road trip", "chill");

        _sut.MoveNextTo("p1", "p3", after: false);
        Names.Should().Equal("chill", "Gym", "Road trip");

        _sut.MoveNextTo("p1", "p3", after: true);
        Names.Should().Equal("Gym", "chill", "Road trip");
    }

    [Fact]
    public async Task Only_a_move_that_stays_in_the_list_can_be_made()
    {
        await _sut.StartAsync();

        _sut.CanMove("p1", -1).Should().BeFalse();
        _sut.CanMove("p1", 1).Should().BeTrue();
        _sut.CanMove("p3", 1).Should().BeFalse();
        _sut.CanMove("p2", 1).Should().BeFalse("not a favourite");

        _sut.MoveBy("p1", -1);
        Orders.Should().BeEmpty("nothing moved");
    }
}
