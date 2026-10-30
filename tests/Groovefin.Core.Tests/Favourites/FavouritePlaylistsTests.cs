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
    private readonly FavouritesService _favourites;
    private readonly FavouritePlaylists _sut;

    public FavouritePlaylistsTests()
    {
        _favourites = new FavouritesService(_library, _messenger, Substitute.For<IErrorReporter>());
        SignIn("s1", "u1");
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Chill, Road, Gym]);
        _sut = new FavouritePlaylists(_playlists, _servers, _favourites, _messenger, new ImmediateDispatcher(), _logger);
    }

    private static Playlist Chill => new() { Id = "p1", Name = "chill", IsFavorite = true };

    private static Playlist Road => new() { Id = "p2", Name = "Road trip" };

    private static Playlist Gym => new() { Id = "p3", Name = "Gym", IsFavorite = true };

    private IEnumerable<string> Names => _sut.Items.Select(p => p.Name);

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
}
