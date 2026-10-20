using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// The now playing bar: current track, transport, seek position, volume, shuffle, repeat and favourite.
/// Follows the engine and the queue; both raise their events on the UI thread.
/// </summary>
public sealed partial class NowPlayingViewModel : ObservableObject, IDisposable
{
    /// <summary>Cover size in device-independent pixels.</summary>
    public const double CoverWidth = 56;

    /// <summary>How much the volume shortcuts change the volume (0–100).</summary>
    public const double VolumeStep = 5;

    /// <summary>Size of the cover in the full-screen player.</summary>
    public const double LargeCoverWidth = 480;

    /// <summary>Size of the cover behind the full-screen player; it is blurred, so a small one is enough.</summary>
    public const double BackdropWidth = 64;

    private readonly IPlayer _player;
    private readonly FavouritesService _favourites;
    private readonly IMessenger _messenger;
    private readonly INavigationService _navigation;
    private readonly IStringResources _strings;

    public NowPlayingViewModel(
        IPlayer player,
        FavouritesService favourites,
        IMessenger messenger,
        INavigationService navigation,
        IStringResources strings)
    {
        _player = player;
        _favourites = favourites;
        _messenger = messenger;
        _navigation = navigation;
        _strings = strings;

        _messenger.Register<NowPlayingViewModel, FavouriteChangedMessage>(this, static (r, m) => r.OnFavouriteChanged(m));

        Engine.StateChanged += OnStateChanged;
        Engine.PositionChanged += OnPositionChanged;
        Engine.CurrentChanged += OnCurrentChanged;
        Queue.Changed += OnQueueChanged;
        OnCurrentChanged(this, EventArgs.Empty);
    }

    /// <summary>Gets or sets the DPI scale of the view, used to request the cover in physical pixels.</summary>
    public double DisplayScale { get; set; } = 1;

    /// <summary>Gets the track shown: the loaded one, or the queue's current entry after playback stopped.</summary>
    public Track? Track => Engine.Current?.Track ?? Queue.Current?.Track;

    public bool HasTrack => Track is not null;

    public string Title => Track?.Name ?? string.Empty;

    public IReadOnlyList<ArtistRef> Artists => Track?.Artists ?? [];

    /// <summary>Gets the cover of the track's album.</summary>
    [ObservableProperty]
    public partial ImageRequest? Cover { get; private set; }

    /// <summary>Gets the cover of the full-screen player.</summary>
    [ObservableProperty]
    public partial ImageRequest? LargeCover { get; private set; }

    /// <summary>Gets the small cover blurred behind the full-screen player.</summary>
    [ObservableProperty]
    public partial ImageRequest? BackdropCover { get; private set; }

    /// <summary>Gets or sets a value indicating whether the full-screen player is shown (F11, Esc exits).</summary>
    [ObservableProperty]
    public partial bool IsFullScreen { get; set; }

    public string AlbumName => Track?.AlbumName ?? string.Empty;

    /// <summary>Gets the artists as one line, e.g. "Daft Punk, Romanthony".</summary>
    public string ArtistNames => string.Join(", ", Artists.Select(a => a.Name));

    public bool IsPlaying => Engine.State is PlaybackState.Playing or PlaybackState.Buffering or PlaybackState.Opening;

    /// <summary>Gets the name of the play/pause button: "Pause" while playing, "Play" otherwise.</summary>
    public string PlayPauseName => _strings.GetString(IsPlaying ? "NowPlaying_Pause" : "NowPlaying_Play");

    public double PositionSeconds => Engine.Position.TotalSeconds;

    /// <summary>Gets the seek range; at least one second so the slider stays usable while the length is unknown.</summary>
    public double DurationSeconds => Math.Max(1, Engine.Duration.TotalSeconds);

    public string ElapsedText => Durations.Track(Engine.Position);

    public string RemainingText => Engine.Duration > TimeSpan.Zero
        ? "-" + Durations.Track(Engine.Duration - Engine.Position)
        : string.Empty;

    /// <summary>Gets or sets the volume from 0 to 100.</summary>
    public double Volume
    {
        get => Math.Round(Engine.Volume * 100);
        set
        {
            Engine.Volume = value / 100;
            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => Engine.IsMuted;
        set
        {
            Engine.IsMuted = value;
            OnPropertyChanged();
        }
    }

    public bool Shuffle
    {
        get => Queue.Shuffle;
        set => Queue.Shuffle = value;
    }

    public RepeatMode Repeat => Queue.Repeat;

    public bool IsRepeatOn => Repeat != RepeatMode.Off;

    /// <summary>Gets the name of the repeat button for its current mode, e.g. "Repeat all".</summary>
    public string RepeatName => _strings.GetString($"NowPlaying_Repeat{Repeat}");

    [ObservableProperty]
    public partial bool IsFavourite { get; private set; }

    private IPlaybackEngine Engine => _player.Engine;

    private QueueManager Queue => _player.Queue;

    /// <summary>Moves to <paramref name="seconds"/> in the current track (seek slider).</summary>
    public void Seek(double seconds) => Engine.Seek(TimeSpan.FromSeconds(Math.Max(0, seconds)));

    public void Dispose()
    {
        Engine.StateChanged -= OnStateChanged;
        Engine.PositionChanged -= OnPositionChanged;
        Engine.CurrentChanged -= OnCurrentChanged;
        Queue.Changed -= OnQueueChanged;
        _messenger.UnregisterAll(this);
    }

    [RelayCommand(CanExecute = nameof(HasTrack))]
    private void PlayPause() => _player.TogglePlayPause();

    [RelayCommand(CanExecute = nameof(HasTrack))]
    private void Next() => _player.Next();

    [RelayCommand(CanExecute = nameof(HasTrack))]
    private void Previous() => _player.Previous();

    /// <summary>Off → all → one → off.</summary>
    [RelayCommand]
    private void CycleRepeat() => Queue.Repeat = Repeat switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.Off,
    };

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    /// <summary>Raises the volume by <see cref="VolumeStep"/> (Ctrl+Up).</summary>
    [RelayCommand]
    private void VolumeUp() => Volume = Math.Min(100, Volume + VolumeStep);

    /// <summary>Lowers the volume by <see cref="VolumeStep"/> (Ctrl+Down).</summary>
    [RelayCommand]
    private void VolumeDown() => Volume = Math.Max(0, Volume - VolumeStep);

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    [RelayCommand]
    private void ExitFullScreen() => IsFullScreen = false;

    private ImageRequest? CoverOf(Track? track, double width) =>
        track?.AlbumId is { } albumId ? ImageRequest.Primary(albumId, track.AlbumImageTag, width, DisplayScale) : null;

    /// <summary>Turns shuffle on or off (Ctrl+S).</summary>
    [RelayCommand]
    private void ToggleShuffle() => Shuffle = !Shuffle;

    [RelayCommand(CanExecute = nameof(HasTrack))]
    private async Task ToggleFavouriteAsync()
    {
        var track = Track!;
        var target = !IsFavourite;

        // Optimistic: show the new state at once, roll back if the server refuses.
        IsFavourite = target;
        if (!await _favourites.SetAsync(track.Id, target) && ReferenceEquals(track, Track))
        {
            IsFavourite = !target;
        }
    }

    /// <summary>A heart was toggled elsewhere (album page, favourites page, a context menu).</summary>
    private void OnFavouriteChanged(FavouriteChangedMessage message)
    {
        if (Track?.Id == message.ItemId)
        {
            IsFavourite = message.IsFavourite;
        }
    }

    [RelayCommand]
    private void OpenAlbum()
    {
        if (Track?.AlbumId is { } albumId)
        {
            _navigation.NavigateTo(PageKeys.Album, albumId);
        }
    }

    [RelayCommand]
    private void OpenArtist(ArtistRef artist) => _navigation.NavigateTo(PageKeys.Artist, artist.Id);

    private void OnStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseName));
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(RemainingText));
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        var track = Track;
        Cover = CoverOf(track, CoverWidth);
        LargeCover = CoverOf(track, LargeCoverWidth);
        BackdropCover = CoverOf(track, BackdropWidth);
        IsFavourite = track is not null && _favourites.IsFavourite(track.Id, track.IsFavorite);
        OnPropertyChanged(nameof(Track));
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artists));
        OnPropertyChanged(nameof(ArtistNames));
        OnPropertyChanged(nameof(AlbumName));
        PlayPauseCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        ToggleFavouriteCommand.NotifyCanExecuteChanged();
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Shuffle));
        OnPropertyChanged(nameof(Repeat));
        OnPropertyChanged(nameof(IsRepeatOn));
        OnPropertyChanged(nameof(RepeatName));

        // After playback stopped the bar shows the queue's current entry.
        if (Engine.Current is null)
        {
            OnCurrentChanged(sender, e);
        }
    }
}
