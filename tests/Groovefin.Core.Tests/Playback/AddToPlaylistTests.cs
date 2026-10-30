using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.Tests.Playback;

public sealed class AddToPlaylistTests
{
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<string> _added = [];
    private readonly AddToPlaylist _sut;

    public AddToPlaylistTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Playlist_Added").Returns("{0} songs to {1}");
        _strings.GetString("Playlist_AddedOne").Returns("{0} song to {1}");
        _messenger.Register<AddToPlaylistTests, PlaylistTracksAddedMessage>(this, static (r, m) => r._added.Add(m.PlaylistId));
        _sut = new AddToPlaylist(_playlists, _dialogs, _notifications, _errors, _strings, _messenger);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Playlist Chill = new() { Id = "p1", Name = "Chill" };

    private static Track[] Tracks(params string[] ids) => ids.Select(id => new Track { Id = id, Name = id }).ToArray();

    [Fact]
    public async Task Targets_are_the_users_playlists()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([Chill]);

        (await _sut.GetTargetsAsync(Ct)).Should().Equal(Chill);
    }

    [Fact]
    public async Task Targets_that_cannot_be_loaded_are_reported_and_empty()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Playlist>>(_ => throw new JellyfinException("boom"));

        (await _sut.GetTargetsAsync(Ct)).Should().BeEmpty();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadPlaylists);
    }

    [Fact]
    public async Task Add_appends_the_tracks_and_confirms()
    {
        await _sut.AddAsync(Chill, Tracks("a", "b"));

        await _playlists.Received(1).AddTracksAsync("p1", Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "a", "b" })), Arg.Any<CancellationToken>());
        _notifications.Received(1).Show(new AppNotification(NotificationSeverity.Success, "Playlist_AddedTitle", "2 songs to Chill"));
        _added.Should().Equal(["p1"], "the navigation pane shows a favourite's new count (#299)");
    }

    [Fact]
    public async Task Adding_nothing_sends_nothing()
    {
        await _sut.AddAsync(Chill, []);

        await _playlists.DidNotReceiveWithAnyArgs().AddTracksAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_failed_add_is_reported()
    {
        _playlists.AddTracksAsync(default!, default!, Ct).ReturnsForAnyArgs(_ => throw new JellyfinException("boom"));

        await _sut.AddAsync(Chill, Tracks("a"));

        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.EditPlaylist);
        _notifications.DidNotReceiveWithAnyArgs().Show(default!);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_with_asks_for_a_name_and_creates_the_playlist_with_the_tracks()
    {
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs(" Mix ");

        await _sut.CreateWithAsync(Tracks("a"));

        await _playlists.Received(1).CreateAsync(" Mix ", Arg.Is<IReadOnlyList<string>>(ids => ids.Single() == "a"), Arg.Any<CancellationToken>());
        _notifications.Received(1).Show(new AppNotification(NotificationSeverity.Success, "Playlist_AddedTitle", "1 song to Mix"));
    }

    [Fact]
    public async Task Cancelling_create_with_creates_nothing()
    {
        await _sut.CreateWithAsync(Tracks("a"));

        await _playlists.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_failed_create_with_is_reported()
    {
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs("Mix");
        _playlists.CreateAsync(default!, default!, Ct).ReturnsForAnyArgs<string>(_ => throw new JellyfinException("boom"));

        await _sut.CreateWithAsync(Tracks("a"));

        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.EditPlaylist);
    }
}
