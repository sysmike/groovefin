using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.JumpLists;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Groovefin.Core.Tests.JumpLists;

public sealed class JumpListSyncTests : IDisposable
{
    private readonly IJumpList _jumpList = Substitute.For<IJumpList>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly FavouritePlaylists _favourites;
    private readonly List<(IReadOnlyList<JumpListEntry> Tasks, string Group, IReadOnlyList<JumpListEntry> Playlists)> _sets = [];

    public JumpListSyncTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _servers.IsSignedIn.Returns(true);
        _servers.ActiveServer.Returns(new ServerProfile { Id = "s1", Name = "s1", Address = new Uri("https://music.example"), UserId = "u1" });
        _jumpList.SetAsync(Arg.Any<IReadOnlyList<JumpListEntry>>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<JumpListEntry>>())
            .Returns(call =>
            {
                _sets.Add((call.ArgAt<IReadOnlyList<JumpListEntry>>(0), call.ArgAt<string>(1), call.ArgAt<IReadOnlyList<JumpListEntry>>(2)));
                return Task.CompletedTask;
            });
        var messenger = new WeakReferenceMessenger();
        _favourites = new FavouritePlaylists(
            _playlists,
            _servers,
            new FavouritesService(Substitute.For<ILibraryService>(), messenger, Substitute.For<IErrorReporter>()),
            new InMemorySettingsService(),
            messenger,
            new ImmediateDispatcher(),
            NullLogger<FavouritePlaylists>.Instance);
    }

    public void Dispose() => _favourites.Dispose();

    private static Playlist Favourite(int i) => new() { Id = $"p{i}", Name = $"List {i:00}", IsFavorite = true };

    [Fact]
    public async Task The_tasks_are_set_at_once()
    {
        using var sut = new JumpListSync(_jumpList, _favourites, _strings);
        await sut.LastUpdate;

        var (tasks, group, playlists) = _sets.Should().ContainSingle().Subject;
        tasks.Select(t => (t.Title, t.Command)).Should().Equal(
            ("JumpList_PlayPause", JumpListCommand.PlayPause),
            ("JumpList_Next", JumpListCommand.Next),
            ("JumpList_ShuffleAll", JumpListCommand.ShuffleAll));
        group.Should().Be("JumpList_Playlists");
        playlists.Should().BeEmpty();
    }

    [Fact]
    public async Task The_favourite_playlists_follow_in_their_order_up_to_the_limit()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(1, JumpListSync.MaxPlaylists + 2).Select(Favourite)]);
        using var sut = new JumpListSync(_jumpList, _favourites, _strings);

        await _favourites.StartAsync();
        await sut.LastUpdate;

        var playlists = _sets[^1].Playlists;
        playlists.Should().HaveCount(JumpListSync.MaxPlaylists);
        playlists[0].Should().Be(new JumpListEntry("List 01", JumpListCommand.ForPlaylist("p1")));
    }

    [Fact]
    public async Task Disposed_it_stops_following()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Favourite(1)]);
        var sut = new JumpListSync(_jumpList, _favourites, _strings);
        await sut.LastUpdate;
        sut.Dispose();

        await _favourites.StartAsync();

        _sets.Should().ContainSingle();
    }
}
