using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
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

    private readonly IPlayer _player;
    private readonly ILibraryService _library;
    private readonly INavigationService _navigation;
    private readonly IErrorReporter _errors;
    private readonly IStringResources _strings;

    public NowPlayingViewModel(
        IPlayer player,
        ILibraryService library,
        INavigationService navigation,
        IErrorReporter errors,
        IStringResources strings)
    {
        _player = player;
        _library = library;
        _navigation = navigation;
        _errors = errors;
        _strings = strings;

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

    [RelayCommand(CanExecute = nameof(HasTrack))]
    private async Task ToggleFavouriteAsync()
    {
        var track = Track!;
        var target = !IsFavourite;

        // Optimistic: show the new state at once, roll back if the server refuses.
        IsFavourite = target;
        try
        {
            await _library.SetFavouriteAsync(track.Id, target);
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            if (ReferenceEquals(track, Track))
            {
                IsFavourite = !target;
            }

            _errors.Report(ex, Operations.SetFavourite);
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
        Cover = track?.AlbumId is { } albumId ? ImageRequest.Primary(albumId, track.AlbumImageTag, CoverWidth, DisplayScale) : null;
        IsFavourite = track?.IsFavorite ?? false;
        OnPropertyChanged(nameof(Track));
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artists));
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
