using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Lyrics;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;

namespace Groovefin.Core.ViewModels;

/// <summary>One line in the lyrics pane; <see cref="IsCurrent"/> marks the line being sung.</summary>
public sealed partial class LyricLineItem(LyricLine line, bool isSynced) : ObservableObject
{
    public LyricLine Line { get; } = line;

    /// <summary>Gets a value indicating whether the line is shown dimmed: synced and not being sung.</summary>
    public bool IsDimmed => isSynced && !IsCurrent;

    /// <summary>Gets the text to show; an empty synced line (an instrumental part) shows a note.</summary>
    public string Text => Line.Text.Length == 0 && Line.Start is not null ? "♪" : Line.Text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed))]
    public partial bool IsCurrent { get; internal set; }
}

/// <summary>
/// The lyrics pane (Ctrl+L): the playing track's lyrics. Synced lyrics follow playback and a line click
/// seeks to it; unsynced lyrics are plain text. Font size and alignment are remembered; the sync offset
/// applies to the current track only. Lyrics load when the pane is open.
/// </summary>
public sealed partial class LyricsViewModel : ObservableObject, IDisposable
{
    public const double MinFontSize = 12;

    public const double MaxFontSize = 48;

    public const double FontSizeStep = 2;

    /// <summary>How much one press of "earlier" or "later" moves the lines.</summary>
    public static readonly TimeSpan OffsetStep = TimeSpan.FromMilliseconds(250);

    private readonly IPlayer _player;
    private readonly ILyricsService _lyrics;
    private readonly ISettingsService _settings;
    private readonly IErrorReporter _errors;
    private readonly IStringResources _strings;
    private CancellationTokenSource? _loading;
    /// <summary>The track whose lyrics are shown or loading; reopening the pane keeps them.</summary>
    private string? _shownTrackId;
    private TrackLyrics? _current;

    public LyricsViewModel(IPlayer player, ILyricsService lyrics, ISettingsService settings, IErrorReporter errors, IStringResources strings)
    {
        _player = player;
        _lyrics = lyrics;
        _settings = settings;
        _errors = errors;
        _strings = strings;
        _player.Engine.CurrentChanged += OnTrackChanged;
        _player.Engine.PositionChanged += OnPositionChanged;
    }

    public ObservableCollection<LyricLineItem> Lines { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Gets a value indicating whether the lines follow playback and can be clicked.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OffsetEarlierCommand), nameof(OffsetLaterCommand), nameof(ResetOffsetCommand))]
    public partial bool IsSynced { get; private set; }

    /// <summary>Gets the message shown instead of lyrics (nothing playing, none found, failed), or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; private set; } = string.Empty;

    public bool HasStatus => Status.Length > 0;

    /// <summary>Gets the index of the line being sung, or -1.</summary>
    [ObservableProperty]
    public partial int CurrentIndex { get; private set; } = -1;

    /// <summary>Gets how far the lines are moved for this track; positive shows them later.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffsetText))]
    [NotifyCanExecuteChangedFor(nameof(ResetOffsetCommand))]
    public partial TimeSpan Offset { get; private set; }

    /// <summary>Gets the offset for display, e.g. "+0.25 s".</summary>
    public string OffsetText => _strings.GetString("Lyrics_OffsetValue")
        .Replace("{0}", Offset.TotalSeconds.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture), StringComparison.Ordinal);

    public double FontSize => _settings.Current.Lyrics.FontSize;

    public LyricsAlignment Alignment => _settings.Current.Lyrics.Alignment;

    public void Dispose()
    {
        _player.Engine.CurrentChanged -= OnTrackChanged;
        _player.Engine.PositionChanged -= OnPositionChanged;
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>Seeks to where the line is sung (with the offset applied).</summary>
    [RelayCommand]
    private void SeekTo(LyricLineItem item)
    {
        if (IsSynced && item.Line.Start is { } start)
        {
            var position = start + Offset;
            _player.Engine.Seek(position < TimeSpan.Zero ? TimeSpan.Zero : position);
        }
    }

    [RelayCommand]
    private void IncreaseFontSize() => SetFontSize(FontSize + FontSizeStep);

    [RelayCommand]
    private void DecreaseFontSize() => SetFontSize(FontSize - FontSizeStep);

    [RelayCommand]
    private void SetAlignment(LyricsAlignment alignment)
    {
        _settings.Update(s => s with { Lyrics = s.Lyrics with { Alignment = alignment } });
        OnPropertyChanged(nameof(Alignment));
    }

    /// <summary>Shows the lines sooner, for lyrics that lag behind the music.</summary>
    [RelayCommand(CanExecute = nameof(IsSynced))]
    private void OffsetEarlier() => SetOffset(Offset - OffsetStep);

    [RelayCommand(CanExecute = nameof(IsSynced))]
    private void OffsetLater() => SetOffset(Offset + OffsetStep);

    private bool CanResetOffset() => IsSynced && Offset != TimeSpan.Zero;

    [RelayCommand(CanExecute = nameof(CanResetOffset))]
    private void ResetOffset() => SetOffset(TimeSpan.Zero);

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            _ = LoadAsync();
        }
    }

    private void SetFontSize(double size)
    {
        var clamped = Math.Clamp(size, MinFontSize, MaxFontSize);
        _settings.Update(s => s with { Lyrics = s.Lyrics with { FontSize = clamped } });
        OnPropertyChanged(nameof(FontSize));
    }

    private void SetOffset(TimeSpan offset)
    {
        Offset = offset;
        UpdateCurrentLine();
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        if (IsOpen)
        {
            _ = LoadAsync();
        }
    }

    private void OnPositionChanged(object? sender, EventArgs e) => UpdateCurrentLine();

    /// <summary>Loads the playing track's lyrics, unless they are already shown.</summary>
    public async Task LoadAsync()
    {
        var track = _player.Engine.Current?.Track;
        if (track is not null && track.Id == _shownTrackId)
        {
            return;
        }

        _loading?.Cancel();
        _loading?.Dispose();
        _loading = new CancellationTokenSource();
        var cancellation = _loading.Token;
        _shownTrackId = track?.Id;
        Show(null, track is null ? "Lyrics_NothingPlaying" : null);
        Offset = TimeSpan.Zero;
        if (track is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var lyrics = await _lyrics.GetLyricsAsync(track.Id, cancellation);
            if (!cancellation.IsCancellationRequested)
            {
                Show(lyrics, lyrics is null ? "Lyrics_None" : null);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                Show(null, "Lyrics_Failed");
                _errors.Report(ex, Operations.LoadLyrics);

                // Opening the pane again retries.
                _shownTrackId = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Another track started while these lyrics were loading.
        }
        finally
        {
            if (!cancellation.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void Show(TrackLyrics? lyrics, string? statusKey)
    {
        _current = lyrics;
        Lines.Clear();
        var synced = lyrics?.IsSynced ?? false;
        foreach (var line in lyrics?.Lines ?? [])
        {
            Lines.Add(new LyricLineItem(line, synced));
        }

        IsSynced = synced;
        Status = statusKey is null ? string.Empty : _strings.GetString(statusKey);
        CurrentIndex = -1;
        UpdateCurrentLine();
    }

    private void UpdateCurrentLine()
    {
        var index = _current?.LineAt(_player.Engine.Position, Offset) ?? -1;
        if (index == CurrentIndex)
        {
            return;
        }

        if (CurrentIndex >= 0 && CurrentIndex < Lines.Count)
        {
            Lines[CurrentIndex].IsCurrent = false;
        }

        if (index >= 0 && index < Lines.Count)
        {
            Lines[index].IsCurrent = true;
        }

        CurrentIndex = index;
    }
}
