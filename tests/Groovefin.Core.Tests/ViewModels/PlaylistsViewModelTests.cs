using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class PlaylistsViewModelTests
{
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly PlaylistsViewModel _sut;

    public PlaylistsViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Album_TrackCount").Returns("{0} tracks");
        _sut = new PlaylistsViewModel(_playlists, _navigation, _dialogs, _errors, _strings);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_lists_the_playlists()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([new Playlist { Id = "p1", Name = "Chill", TrackCount = 4, PrimaryImageTag = "i" }]);
        _sut.DisplayScale = 2;

        await _sut.LoadAsync(Ct);

        var item = _sut.Playlists.Should().ContainSingle().Subject;
        item.Name.Should().Be("Chill");
        item.Details.Should().Be("4 tracks");
        item.Image!.Width.Should().Be(336);
        _sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task No_playlists_shows_the_empty_state()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await _sut.LoadAsync(Ct);

        _sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_load_is_reported_and_is_not_empty()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Playlist>>(_ => throw new JellyfinException("boom"));

        await _sut.LoadAsync(Ct);

        _sut.LoadFailed.Should().BeTrue();
        _sut.IsEmpty.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadPlaylists);
    }

    [Fact]
    public async Task Cancelled_loads_are_quiet()
    {
        _playlists.GetPlaylistsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Playlist>>(_ => throw new OperationCanceledException());

        await _sut.LoadAsync(Ct);

        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public void Open_navigates_to_the_playlist()
    {
        _sut.OpenCommand.Execute(new PlaylistItem(new Playlist { Id = "p1", Name = "Chill" }, null, string.Empty));

        _navigation.Received(1).NavigateTo(PageKeys.Playlist, "p1");
    }

    [Fact]
    public async Task Create_asks_for_a_name_creates_an_empty_playlist_and_opens_it()
    {
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs("Road trip");
        _playlists.CreateAsync("Road trip", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns("new");

        await _sut.CreateCommand.ExecuteAsync(null);

        await _playlists.Received(1).CreateAsync("Road trip", Arg.Is<IReadOnlyList<string>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo(PageKeys.Playlist, "new");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Cancelling_create_does_nothing(string? name)
    {
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs(name);

        await _sut.CreateCommand.ExecuteAsync(null);

        await _playlists.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_failed_create_is_reported()
    {
        _dialogs.PromptAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs("Mix");
        _playlists.CreateAsync(default!, default!, Ct).ReturnsForAnyArgs<string>(_ => throw new JellyfinException("boom"));

        await _sut.CreateCommand.ExecuteAsync(null);

        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.EditPlaylist);
    }

    [Fact]
    public void Details_show_what_is_known()
    {
        _strings.GetString("Album_TrackCountOne").Returns("{0} track");

        PlaylistItem.DetailsText(1, TimeSpan.FromMinutes(62), _strings).Should().Be("1 track · 1:02:00");
        PlaylistItem.DetailsText(null, TimeSpan.Zero, _strings).Should().BeEmpty();
        PlaylistItem.DetailsText(null, null, _strings).Should().BeEmpty();
    }
}
