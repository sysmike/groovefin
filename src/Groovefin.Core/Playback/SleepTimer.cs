using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Services;

namespace Groovefin.Core.Playback;

/// <summary>When the sleep timer pauses the music (#396).</summary>
public enum SleepTimerMode
{
    Off,

    /// <summary>After a number of minutes.</summary>
    Minutes,

    /// <summary>At the end of the playing song.</summary>
    EndOfTrack,

    /// <summary>At the end of the playing album: the song after which the queue goes on with another album, or ends.</summary>
    EndOfAlbum,
}

/// <summary>
/// The sleep timer (#396): pauses the music after 15, 30 or 60 minutes, or at the end of the song or the album, fading
/// out over <see cref="FadeDuration"/> first. The volume is put back once it paused, so the next Play is as loud as
/// before. The end of a song is watched by its position, so the pause comes before a gapless next song starts. Lives
/// as long as the app; use it from the UI thread.
/// </summary>
public sealed partial class SleepTimer : ObservableObject, IDisposable
{
    /// <summary>How long the music fades out before it pauses.</summary>
    public static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(4);

    /// <summary>How often the fade turns the volume down, and the minutes left are counted.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private readonly IPlayer _player;
    private readonly TimeProvider _time;
    private readonly IDispatcher? _dispatcher;
    private readonly IStringResources _strings;
    private ITimer? _timer;
    private DateTimeOffset _deadline;

    /// <summary>The fade: when it started and the volume it started from, or <see langword="null"/>.</summary>
    private (DateTimeOffset Start, TimeSpan Length, double Volume)? _fade;

    public SleepTimer(IPlayer player, TimeProvider time, IStringResources strings, IDispatcher? dispatcher = null)
    {
        _player = player;
        _time = time;
        _strings = strings;
        _dispatcher = dispatcher;
        _player.Engine.PositionChanged += OnPositionChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(Description))]
    public partial SleepTimerMode Mode { get; private set; }

    public bool IsActive => Mode != SleepTimerMode.Off;

    /// <summary>Gets what the timer does, for the button's tooltip and name: "Sleep timer", "Pauses in 23 min", ….</summary>
    public string Description => Mode switch
    {
        SleepTimerMode.Minutes => _strings.GetString("SleepTimer_PausesIn").Replace("{0}", MinutesLeft.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal),
        SleepTimerMode.EndOfTrack => _strings.GetString("SleepTimer_PausesAfterTrack"),
        SleepTimerMode.EndOfAlbum => _strings.GetString("SleepTimer_PausesAfterAlbum"),
        _ => _strings.GetString("SleepTimer_Off"),
    };

    /// <summary>Gets the whole minutes left, rounded up, while <see cref="SleepTimerMode.Minutes"/> runs.</summary>
    public int MinutesLeft => Mode == SleepTimerMode.Minutes
        ? Math.Max(0, (int)Math.Ceiling((_deadline - _time.GetUtcNow()).TotalMinutes))
        : 0;

    public void Dispose()
    {
        _player.Engine.PositionChanged -= OnPositionChanged;
        StopTimer();
    }

    /// <summary>Pauses after <paramref name="minutes"/>, fading out over the last seconds.</summary>
    [RelayCommand]
    private void StartMinutes(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minutes);
        Reset();
        _deadline = _time.GetUtcNow().AddMinutes(minutes);
        Mode = SleepTimerMode.Minutes;
        StartTimer();
    }

    [RelayCommand]
    private void StartEndOfTrack()
    {
        Reset();
        Mode = SleepTimerMode.EndOfTrack;
    }

    [RelayCommand]
    private void StartEndOfAlbum()
    {
        Reset();
        Mode = SleepTimerMode.EndOfAlbum;
    }

    /// <summary>Turns the timer off; a fade under way stops and the volume comes back.</summary>
    [RelayCommand]
    private void Cancel() => Reset();

    /// <summary>Near the end of a song, the end-of-song and end-of-album timers start the fade.</summary>
    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (_fade is not null || Mode is not (SleepTimerMode.EndOfTrack or SleepTimerMode.EndOfAlbum))
        {
            return;
        }

        var engine = _player.Engine;
        var left = engine.Duration - engine.Position;
        if (engine.Duration <= TimeSpan.Zero || left > FadeDuration || engine.State != PlaybackState.Playing)
        {
            return;
        }

        if (Mode == SleepTimerMode.EndOfAlbum && !AlbumEndsHere())
        {
            return;
        }

        // Pause a little before the end, so the engine does not move on to the next song first.
        StartFade(left - TimeSpan.FromMilliseconds(300));
    }

    /// <summary>Whether the playing song is its album's last one in the queue: the next entry is another album, or none.</summary>
    private bool AlbumEndsHere()
    {
        var current = _player.Queue.Current?.Track;
        var next = _player.Queue.PeekNext()?.Track;
        return next is null || current?.AlbumId is null || next.AlbumId != current.AlbumId;
    }

    private void StartFade(TimeSpan length)
    {
        _fade = (_time.GetUtcNow(), length > TimeSpan.Zero ? length : TimeSpan.Zero, _player.Engine.Volume);
        StartTimer();
    }

    private void StartTimer()
    {
        _timer ??= _time.CreateTimer(_ => OnUiThread(OnTick), null, Tick, Tick);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick()
    {
        var now = _time.GetUtcNow();
        if (Mode == SleepTimerMode.Minutes && _fade is null)
        {
            OnPropertyChanged(nameof(MinutesLeft));
            OnPropertyChanged(nameof(Description));
            var left = _deadline - now;
            if (left <= FadeDuration)
            {
                if (_player.Engine.State != PlaybackState.Playing)
                {
                    // Nothing to fade: it is paused already.
                    Reset();
                    return;
                }

                StartFade(left);
            }

            return;
        }

        if (_fade is not { } fade)
        {
            return;
        }

        var done = fade.Length <= TimeSpan.Zero ? 1 : Math.Clamp((now - fade.Start) / fade.Length, 0, 1);
        if (done < 1)
        {
            _player.Engine.Volume = fade.Volume * (1 - done);
            return;
        }

        // Pause, then put the volume back for the next Play.
        _player.Engine.Pause();
        _player.Engine.Volume = fade.Volume;
        _fade = null;
        StopTimer();
        Mode = SleepTimerMode.Off;
    }

    /// <summary>Stops the timer and any fade, putting the volume back.</summary>
    private void Reset()
    {
        StopTimer();
        if (_fade is { } fade)
        {
            _player.Engine.Volume = fade.Volume;
            _fade = null;
        }

        Mode = SleepTimerMode.Off;
    }

    private void OnUiThread(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _ = _dispatcher.EnqueueAsync(action);
        }
    }
}
