using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class NowPlayingViewModelTests : IDisposable
{
    private static readonly ArtistRef DaftPunk = new("artist-1", "Daft Punk");

    private static readonly Track OneMoreTime = new()
    {
        Id = "t1",
        Name = "One More Time",
        AlbumId = "album-1",
        AlbumImageTag = "tag",
        Artists = [DaftPunk],
        Duration = TimeSpan.FromSeconds(320),
        IsFavorite = true,
    };

    private readonly QueueManager _queue = new(new Random(1));
    private readonly FakePlaybackEngine _engine = new();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly NowPlayingViewModel _sut;
    private readonly List<string?> _changed = [];

    public NowPlayingViewModelTests()
    {
        _player.Queue.Returns(_queue);
        _player.Engine.Returns(_engine);
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _sut = new NowPlayingViewModel(_player, new FavouritesService(_library, _messenger, _errors), _messenger, _navigation, _strings) { DisplayScale = 2 };
        _sut.PropertyChanged += (_, e) => _changed.Add(e.PropertyName);
    }

    public void Dispose() => _sut.Dispose();

    private void PlayOneMoreTime()
    {
        _queue.PlayNow([OneMoreTime]);
        _engine.Load(new PlaybackSource(_queue.Current!.Id, new Uri("http://stream/t1"), OneMoreTime));
    }

    [Fact]
    public void Without_a_track_the_bar_is_empty_and_transport_is_disabled()
    {
        _sut.HasTrack.Should().BeFalse();
        _sut.Title.Should().BeEmpty();
        _sut.Artists.Should().BeEmpty();
        _sut.Cover.Should().BeNull();
        _sut.RemainingText.Should().BeEmpty();
        _sut.DurationSeconds.Should().Be(1);
        _sut.PlayPauseCommand.CanExecute(null).Should().BeFalse();
        _sut.NextCommand.CanExecute(null).Should().BeFalse();
        _sut.PreviousCommand.CanExecute(null).Should().BeFalse();
        _sut.ToggleFavouriteCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void A_loaded_track_is_shown_with_its_cover_and_favourite()
    {
        PlayOneMoreTime();

        _sut.HasTrack.Should().BeTrue();
        _sut.Title.Should().Be("One More Time");
        _sut.Artists.Should().Equal(DaftPunk);
        _sut.Cover.Should().Be(new ImageRequest("album-1", ImageKind.Primary, "tag", (int)(NowPlayingViewModel.CoverWidth * 2)));
        _sut.IsFavourite.Should().BeTrue();
        _sut.PlayPauseCommand.CanExecute(null).Should().BeTrue();
        _changed.Should().Contain([nameof(_sut.Title), nameof(_sut.HasTrack)]);
    }

    [Fact]
    public void A_track_without_album_has_no_cover()
    {
        _queue.PlayNow([OneMoreTime with { AlbumId = null }]);
        _engine.Load(new PlaybackSource(_queue.Current!.Id, new Uri("http://stream/t1"), _queue.Current.Track));

        _sut.Cover.Should().BeNull();
    }

    [Fact]
    public void After_playback_stopped_the_queue_entry_is_still_shown()
    {
        PlayOneMoreTime();

        _engine.Stop();

        _sut.HasTrack.Should().BeTrue();
        _sut.Title.Should().Be("One More Time");
    }

    [Fact]
    public void Position_and_times_follow_the_engine()
    {
        PlayOneMoreTime();
        _changed.Clear();

        _engine.Seek(TimeSpan.FromSeconds(80));

        _sut.PositionSeconds.Should().Be(80);
        _sut.DurationSeconds.Should().Be(320);
        _sut.ElapsedText.Should().Be("1:20");
        _sut.RemainingText.Should().Be("-4:00");
        _changed.Should().Contain([nameof(_sut.PositionSeconds), nameof(_sut.ElapsedText), nameof(_sut.RemainingText)]);
    }

    [Fact]
    public void Seek_goes_to_the_engine_and_negative_values_mean_the_start()
    {
        PlayOneMoreTime();

        _sut.Seek(42.5);
        _sut.PositionSeconds.Should().Be(42.5);

        _sut.Seek(-3);
        _sut.PositionSeconds.Should().Be(0);
    }

    [Fact]
    public void IsPlaying_follows_the_engine_state()
    {
        PlayOneMoreTime();

        _engine.SimulateState(PlaybackState.Buffering);
        _sut.IsPlaying.Should().BeTrue();
        _sut.PlayPauseName.Should().Be("NowPlaying_Pause");
        _engine.SimulateState(PlaybackState.Paused);
        _sut.IsPlaying.Should().BeFalse();
        _sut.PlayPauseName.Should().Be("NowPlaying_Play");
        _changed.Should().Contain([nameof(_sut.IsPlaying), nameof(_sut.PlayPauseName)]);
    }

    [Fact]
    public void Transport_commands_go_to_the_player()
    {
        PlayOneMoreTime();

        _sut.PlayPauseCommand.Execute(null);
        _sut.NextCommand.Execute(null);
        _sut.PreviousCommand.Execute(null);

        Received.InOrder(() =>
        {
            _player.TogglePlayPause();
            _player.Next();
            _player.Previous();
        });
    }

    [Fact]
    public void Volume_is_shown_in_percent_and_mute_toggles()
    {
        _sut.Volume.Should().Be(100);

        _sut.Volume = 35;
        _sut.ToggleMuteCommand.Execute(null);

        _engine.Volume.Should().BeApproximately(0.35, 0.0001);
        _sut.Volume.Should().Be(35);
        _sut.IsMuted.Should().BeTrue();
        _engine.IsMuted.Should().BeTrue();
        _changed.Should().Contain([nameof(_sut.Volume), nameof(_sut.IsMuted)]);
    }

    [Fact]
    public void Volume_shortcuts_step_by_five_within_bounds()
    {
        _sut.VolumeDownCommand.Execute(null);
        _sut.Volume.Should().Be(95);
        _changed.Should().Contain(nameof(_sut.Volume));

        _sut.Volume = 98;
        _sut.VolumeUpCommand.Execute(null);
        _sut.Volume.Should().Be(100);

        _sut.Volume = 3;
        _sut.VolumeDownCommand.Execute(null);
        _sut.Volume.Should().Be(0);
    }

    [Fact]
    public void The_full_screen_player_has_a_large_cover_and_a_small_backdrop_only_while_shown()
    {
        PlayOneMoreTime();
        _sut.LargeCover.Should().BeNull();
        _sut.BackdropCover.Should().BeNull();

        _sut.IsFullScreen = true;

        _sut.LargeCover.Should().Be(new ImageRequest("album-1", ImageKind.Primary, "tag", (int)(NowPlayingViewModel.LargeCoverWidth * 2)));
        _sut.BackdropCover.Should().Be(new ImageRequest("album-1", ImageKind.Primary, "tag", (int)(NowPlayingViewModel.BackdropWidth * 2)));
        _sut.ArtistNames.Should().Be("Daft Punk");
        _changed.Should().Contain([nameof(_sut.LargeCover), nameof(_sut.ArtistNames), nameof(_sut.AlbumName)]);
    }

    [Fact]
    public void Full_screen_toggles_and_exits()
    {
        _sut.ToggleFullScreenCommand.Execute(null);
        _sut.IsFullScreen.Should().BeTrue();

        _sut.ExitFullScreenCommand.Execute(null);
        _sut.IsFullScreen.Should().BeFalse();
        _sut.ExitFullScreenCommand.Execute(null);
        _sut.IsFullScreen.Should().BeFalse();
    }

    [Fact]
    public void Toggle_shuffle_turns_shuffle_on_and_off()
    {
        _sut.ToggleShuffleCommand.Execute(null);
        _queue.Shuffle.Should().BeTrue();

        _sut.ToggleShuffleCommand.Execute(null);
        _queue.Shuffle.Should().BeFalse();
    }

    [Fact]
    public void Shuffle_and_repeat_change_the_queue()
    {
        _sut.Shuffle = true;
        _queue.Shuffle.Should().BeTrue();
        _changed.Should().Contain(nameof(_sut.Shuffle));

        _sut.RepeatName.Should().Be("NowPlaying_RepeatOff");
        _sut.IsRepeatOn.Should().BeFalse();
        _sut.CycleRepeatCommand.Execute(null);
        _sut.Repeat.Should().Be(RepeatMode.All);
        _sut.IsRepeatOn.Should().BeTrue();
        _sut.CycleRepeatCommand.Execute(null);
        _sut.Repeat.Should().Be(RepeatMode.One);
        _sut.RepeatName.Should().Be("NowPlaying_RepeatOne");
        _sut.CycleRepeatCommand.Execute(null);
        _sut.Repeat.Should().Be(RepeatMode.Off);
    }

    [Fact]
    public async Task Favourite_is_optimistic_and_rolls_back_on_failure()
    {
        PlayOneMoreTime();

        await _sut.ToggleFavouriteCommand.ExecuteAsync(null);
        _sut.IsFavourite.Should().BeFalse();
        await _library.Received(1).SetFavouriteAsync("t1", false, Arg.Any<CancellationToken>());

        _library.SetFavouriteAsync("t1", true, Arg.Any<CancellationToken>()).Returns(Task.FromException(new JellyfinException("no")));
        await _sut.ToggleFavouriteCommand.ExecuteAsync(null);

        _sut.IsFavourite.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.SetFavourite);
    }

    [Fact]
    public async Task A_failed_favourite_for_a_track_that_is_gone_does_not_touch_the_new_track()
    {
        PlayOneMoreTime();
        var pending = new TaskCompletionSource();
        _library.SetFavouriteAsync("t1", false, Arg.Any<CancellationToken>()).Returns(pending.Task);
        var toggle = _sut.ToggleFavouriteCommand.ExecuteAsync(null);

        var next = OneMoreTime with { Id = "t2", Name = "Aerodynamic", IsFavorite = false };
        _queue.PlayNow([next]);
        _engine.Load(new PlaybackSource(_queue.Current!.Id, new Uri("http://stream/t2"), next));
        pending.SetException(new JellyfinException("no"));
        await toggle;

        _sut.IsFavourite.Should().BeFalse();
        _sut.Title.Should().Be("Aerodynamic");
    }

    [Fact]
    public void Album_and_artist_links_navigate()
    {
        _sut.OpenAlbumCommand.Execute(null);
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);

        PlayOneMoreTime();
        _sut.OpenAlbumCommand.Execute(null);
        _sut.OpenArtistCommand.Execute(DaftPunk);

        Received.InOrder(() =>
        {
            _navigation.NavigateTo(PageKeys.Album, "album-1");
            _navigation.NavigateTo(PageKeys.Artist, "artist-1");
        });
    }

    [Fact]
    public void Dispose_detaches_from_engine_and_queue()
    {
        _sut.Dispose();
        _changed.Clear();

        PlayOneMoreTime();
        _engine.SimulateState(PlaybackState.Playing);

        _changed.Should().BeEmpty();
    }

    [Fact]
    public void A_heart_toggled_elsewhere_updates_the_bar_for_its_track_only()
    {
        PlayOneMoreTime();

        _messenger.Send(new FavouriteChangedMessage("other", false));
        _sut.IsFavourite.Should().BeTrue();

        _messenger.Send(new FavouriteChangedMessage("t1", false));
        _sut.IsFavourite.Should().BeFalse();
    }

    [Fact]
    public async Task A_track_queued_before_its_heart_changed_shows_the_saved_state()
    {
        var favourites = new FavouritesService(_library, _messenger, _errors);
        await favourites.SetAsync("t1", false);
        using var sut = new NowPlayingViewModel(_player, favourites, _messenger, _navigation, _strings);

        PlayOneMoreTime();

        sut.IsFavourite.Should().BeFalse("the queued track still says it is a favourite");
    }

    [Fact]
    public void After_dispose_hearts_toggled_elsewhere_are_ignored()
    {
        PlayOneMoreTime();
        _sut.Dispose();

        _messenger.Send(new FavouriteChangedMessage("t1", false));

        _sut.IsFavourite.Should().BeTrue();
    }
}
