using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.History;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class OnRepeatViewModelTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePlaybackEngine _engine;
    private readonly IJellyfinClient _client = Substitute.For<IJellyfinClient>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly MemoryPlayHistoryStore _store = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly PlaybackReporting _reporting;
    private readonly PlayHistory _history;
    private readonly OnRepeatViewModel _sut;

    public OnRepeatViewModelTests()
    {
        _engine = new FakePlaybackEngine(time: _time);
        _client.Connection.Returns(new ServerConnection(new Uri("https://jellyfin.example/"), "alice", "token"));
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("OnRepeat_Plays").Returns("{0} plays");
        _strings.GetString("OnRepeat_PlaylistName").Returns("On repeat {0}");
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<Track>([], 0, 0));
        _library.GetTracksByIdAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<string>>().Where(id => id != "gone").Reverse().Select(Song).ToList());

        var privacy = new PrivacyMode();
        _reporting = new PlaybackReporting(_engine, Substitute.For<IPlaybackReporter>(), privacy, new ImmediateDispatcher(), _time, NullLogger<PlaybackReporting>.Instance);
        _history = new PlayHistory(_engine, _reporting, _client, _library, _store, privacy, _time, NullLogger<PlayHistory>.Instance);
        _sut = new OnRepeatViewModel(
            _history,
            _library,
            _playlists,
            _player,
            _navigation,
            _errors,
            _strings,
            new FavouritesService(_library, _messenger, _errors),
            _messenger,
            new CurrentTrack(_engine),
            _time);
    }

    public void Dispose()
    {
        _history.Dispose();
        _reporting.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track Song(string id) => new()
    {
        Id = id,
        Name = $"Song {id}",
        AlbumName = "Album",
        Artists = [new ArtistRef("x", "Artist")],
        Duration = TimeSpan.FromSeconds(60),
    };

    /// <summary>A saved history with <paramref name="plays"/> plays of each track, from yesterday back.</summary>
    private void Played(params (string TrackId, int Plays)[] plays)
    {
        var data = new PlayHistoryData();
        foreach (var (trackId, count) in plays)
        {
            for (var i = 0; i < count; i++)
            {
                data.Plays.Add(new PlayRecord(trackId, _time.GetUtcNow().AddDays(-1 - i), PlaySource.Groovefin));
            }
        }

        _store.Preload(data);
    }

    [Fact]
    public async Task Load_lists_the_most_played_tracks_in_rank_order()
    {
        Played(("a", 2), ("b", 5), ("gone", 9), ("c", 3));

        await _sut.LoadAsync(Ct);

        _sut.Entries.Select(e => e.Track.Id).Should().Equal("b", "c", "a");
        _sut.Entries.Select(e => e.Number).Should().Equal("1", "2", "3");
        _sut.Entries[0].PlaysCount.Should().Be("5");
        _sut.Entries[0].AccessibleName.Should().Be("1, Song b, Artist, Album, 5 plays, 1:00");
        _sut.HasEntries.Should().BeTrue();
        _sut.IsEmpty.Should().BeFalse();
        _sut.PlayCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Load_first_reads_the_plays_on_other_devices()
    {
        await _sut.LoadAsync(Ct);

        await _library.Received().GetTracksAsync(Arg.Is<TrackQuery>(q => q.SortBy == TrackSortField.DatePlayed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_plays_the_list_is_empty_and_cannot_be_played()
    {
        await _sut.LoadAsync(Ct);

        _sut.IsEmpty.Should().BeTrue();
        _sut.PlayCommand.CanExecute(null).Should().BeFalse();
        _sut.SaveAsPlaylistCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_load_is_reported()
    {
        Played(("a", 2));
        _library.GetTracksByIdAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Track>>(new JellyfinException("offline")));

        await _sut.LoadAsync(Ct);

        _sut.LoadFailed.Should().BeTrue();
        _sut.IsEmpty.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadOnRepeat);
    }

    [Fact]
    public async Task Play_shuffle_and_a_row_play_the_list()
    {
        Played(("a", 3), ("b", 2));
        await _sut.LoadAsync(Ct);

        _sut.PlayCommand.Execute(null);
        _sut.ShuffleCommand.Execute(null);
        _sut.PlayEntryCommand.Execute(_sut.Entries[1]);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Select(x => x.Id).SequenceEqual(new[] { "a", "b" })), 0);
        _player.Received(1).Shuffle(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 2));
        _player.Received(1).Play(Arg.Any<IReadOnlyList<Track>>(), 1);
    }

    [Fact]
    public async Task Saving_as_a_playlist_creates_it_with_todays_date_and_opens_it()
    {
        Played(("a", 3), ("b", 2));
        _playlists.CreateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns("pl1");
        await _sut.LoadAsync(Ct);

        await _sut.SaveAsPlaylistCommand.ExecuteAsync(null);

        var today = _time.GetLocalNow().ToString("d", System.Globalization.CultureInfo.CurrentCulture);
        await _playlists.Received(1).CreateAsync(
            $"On repeat {today}", Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "a", "b" })), Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo(PageKeys.Playlist, "pl1");
    }

    [Fact]
    public async Task A_failed_save_is_reported_and_stays_on_the_page()
    {
        Played(("a", 2));
        _playlists.CreateAsync(default!, default!, Ct).ReturnsForAnyArgs(Task.FromException<string>(new JellyfinException("no")));
        await _sut.LoadAsync(Ct);

        await _sut.SaveAsPlaylistCommand.ExecuteAsync(null);

        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.SavePlaylist);
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);
    }

    [Fact]
    public async Task A_heart_toggles_on_the_server_and_follows_hearts_toggled_elsewhere()
    {
        Played(("a", 3), ("b", 2));
        await _sut.LoadAsync(Ct);

        await _sut.ToggleEntryFavouriteCommand.ExecuteAsync(_sut.Entries[0]);
        _messenger.Send(new FavouriteChangedMessage("b", true));

        _sut.Entries.Select(e => e.IsFavourite).Should().Equal(true, true);
        await _library.Received(1).SetFavouriteAsync("a", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_playing_track_is_marked()
    {
        Played(("a", 3), ("b", 2));
        _engine.Load(new PlaybackSource("q-b", new Uri("http://stream/b"), Song("b")));

        await _sut.LoadAsync(Ct);

        _sut.Entries.Select(e => e.IsCurrent).Should().Equal(false, true);
    }
}
