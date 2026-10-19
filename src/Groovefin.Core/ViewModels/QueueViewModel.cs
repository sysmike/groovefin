using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Formatting;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Queue;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>A row of the queue panel.</summary>
public sealed class QueueEntry(QueueItem item, ImageRequest? cover)
{
    public QueueItem Item { get; } = item;

    /// <summary>Gets the queue entry's ID (not the track's: a track can be queued twice).</summary>
    public string Id => Item.Id;

    public Track Track => Item.Track;

    public string Title => Track.Name;

    public string Artist => string.Join(", ", Track.Artists.Select(a => a.Name));

    public string Duration => Durations.Track(Track.Duration);

    public ImageRequest? Cover { get; } = cover;

    /// <summary>Gets the row's name for screen readers: title, artists and length.</summary>
    public string AccessibleName => string.Join(", ", new[] { Title, Artist, Duration }.Where(s => s.Length > 0));
}

/// <summary>
/// The queue panel: the playing entry, what comes next (play, reorder, remove, clear, save as a
/// playlist) and what played before. Follows the queue, which changes on the UI thread.
/// </summary>
public sealed partial class QueueViewModel : ObservableObject, IDisposable
{
    /// <summary>Cover size in device-independent pixels.</summary>
    public const double CoverWidth = 40;

    /// <summary>Most history entries shown, newest first.</summary>
    public const int HistoryShown = 100;

    private readonly IPlayer _player;
    private readonly IPlaylistService _playlists;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IErrorReporter _errors;
    private readonly IStringResources _strings;
    private double _displayScale = 1;

    public QueueViewModel(
        IPlayer player,
        IPlaylistService playlists,
        IDialogService dialogs,
        INotificationService notifications,
        IErrorReporter errors,
        IStringResources strings)
    {
        _player = player;
        _playlists = playlists;
        _dialogs = dialogs;
        _notifications = notifications;
        _errors = errors;
        _strings = strings;

        Queue.Changed += OnQueueChanged;
        Refresh();
    }

    /// <summary>Gets or sets the DPI scale of the view, used to request covers in physical pixels.</summary>
    public double DisplayScale
    {
        get => _displayScale;
        set
        {
            if (value > 0 && Math.Abs(value - _displayScale) > 0.001)
            {
                _displayScale = value;
                Current = null;
                Upcoming.Clear();
                History.Clear();
                Refresh();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the panel is shown (<c>Ctrl+Q</c>, the now playing bar).</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Gets the playing entry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent), nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsPlaylistCommand))]
    public partial QueueEntry? Current { get; private set; }

    public bool HasCurrent => Current is not null;

    /// <summary>Gets the entries after the playing one, in play order.</summary>
    public ObservableCollection<QueueEntry> Upcoming { get; } = [];

    /// <summary>Gets the entries played before, newest first (at most <see cref="HistoryShown"/>).</summary>
    public ObservableCollection<QueueEntry> History { get; } = [];

    public bool HasUpcoming => Upcoming.Count > 0;

    public bool HasHistory => History.Count > 0;

    /// <summary>Gets a value indicating whether nothing is playing or queued.</summary>
    public bool IsEmpty => Current is null && Upcoming.Count == 0;

    private QueueManager Queue => _player.Queue;

    public void Dispose() => Queue.Changed -= OnQueueChanged;

    /// <summary>
    /// Moves <paramref name="entry"/> to <paramref name="upcomingIndex"/> within the upcoming entries, e.g.
    /// after a drag in the panel.
    /// </summary>
    public void MoveUpcoming(QueueEntry entry, int upcomingIndex)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var target = Queue.CurrentIndex + 1 + Math.Clamp(upcomingIndex, 0, Math.Max(0, Upcoming.Count - 1));
        if (target < Queue.Items.Count)
        {
            Queue.Move(entry.Id, target);
        }
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>Plays a queued entry (current or upcoming).</summary>
    [RelayCommand]
    private void Play(QueueEntry entry) => _player.JumpTo(entry.Id);

    /// <summary>Plays a history entry again: right away, keeping the rest of the queue.</summary>
    [RelayCommand]
    private void PlayAgain(QueueEntry entry)
    {
        if (Queue.Current is null)
        {
            _player.Play([entry.Track]);
        }
        else
        {
            _player.PlayNext([entry.Track]);
            _player.Next();
        }
    }

    [RelayCommand]
    private void Remove(QueueEntry entry) => Queue.Remove(entry.Id);

    [RelayCommand]
    private void MoveUp(QueueEntry entry)
    {
        var index = IndexOf(entry);
        if (index > Queue.CurrentIndex + 1)
        {
            Queue.Move(entry.Id, index - 1);
        }
    }

    [RelayCommand]
    private void MoveDown(QueueEntry entry)
    {
        var index = IndexOf(entry);
        if (index > Queue.CurrentIndex && index < Queue.Items.Count - 1)
        {
            Queue.Move(entry.Id, index + 1);
        }
    }

    /// <summary>Removes everything after the playing entry.</summary>
    [RelayCommand(CanExecute = nameof(HasUpcoming))]
    private void ClearUpcoming() => Queue.ClearUpcoming();

    /// <summary>Asks for a name and saves the whole queue, in play order, as a new playlist.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private async Task SaveAsPlaylistAsync()
    {
        var name = await _dialogs.PromptAsync(
            _strings.GetString("Queue_SaveTitle"),
            _strings.GetString("Queue_SaveLabel"),
            string.Empty,
            _strings.GetString("Dialog_Save"),
            _strings.GetString("Dialog_Cancel"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            await _playlists.CreateAsync(name, [.. Queue.Items.Select(i => i.Track.Id)]);
            _notifications.Show(new AppNotification(
                NotificationSeverity.Success,
                _strings.GetString("Queue_SavedTitle"),
                _strings.GetString("Queue_SavedMessage").Replace("{0}", name.Trim(), StringComparison.Ordinal)));
        }
        catch (JellyfinException ex)
        {
            _errors.Report(ex, Operations.SavePlaylist);
        }
    }

    private int IndexOf(QueueEntry entry)
    {
        var items = Queue.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == entry.Id)
            {
                return i;
            }
        }

        return -1;
    }

    private void OnQueueChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var current = Queue.Current;
        if (current?.Id != Current?.Id)
        {
            Current = current is null ? null : Entry(current);
        }

        Sync(Upcoming, Queue.Items.Skip(Queue.CurrentIndex + 1));

        // History can hold an entry more than once (Repeat One), so it is replaced rather than synced.
        var history = Queue.History.Reverse().Take(HistoryShown).ToList();
        if (!history.Select(i => i.Id).SequenceEqual(History.Select(e => e.Id)))
        {
            History.Clear();
            foreach (var item in history)
            {
                History.Add(Entry(item));
            }
        }

        OnPropertyChanged(nameof(HasUpcoming));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(IsEmpty));
        ClearUpcomingCommand.NotifyCanExecuteChanged();
    }

    private QueueEntry Entry(QueueItem item) =>
        new(item, item.Track.AlbumId is { } albumId ? ImageRequest.Primary(albumId, item.Track.AlbumImageTag, CoverWidth, _displayScale) : null);

    /// <summary>
    /// Makes <paramref name="rows"/> show <paramref name="items"/> (distinct entries) with as few changes as
    /// possible, so the list keeps its scroll position, focus and running animations (e.g. while a drag
    /// completes).
    /// </summary>
    private void Sync(ObservableCollection<QueueEntry> rows, IEnumerable<QueueItem> items)
    {
        var target = items.ToList();
        var wanted = target.Select(i => i.Id).ToHashSet();
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(rows[i].Id))
            {
                rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            if (i < rows.Count && rows[i].Id == target[i].Id)
            {
                continue;
            }

            var existing = FindIndex(rows, target[i].Id, i + 1);
            if (existing >= 0)
            {
                rows.Move(existing, i);
            }
            else
            {
                rows.Insert(i, Entry(target[i]));
            }
        }
    }

    private static int FindIndex(ObservableCollection<QueueEntry> rows, string id, int start)
    {
        for (var i = start; i < rows.Count; i++)
        {
            if (rows[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
