using System.Globalization;
using Groovefin.Core.Models;

namespace Groovefin.Core.Queue;

/// <summary>
/// The play queue: order, the current entry, shuffle, repeat and history. Pure logic without playback;
/// the engine and the queue panel follow <see cref="Changed"/>. Not thread-safe: use it from the UI
/// thread.
/// </summary>
/// <remarks>
/// Two lists are kept. <c>_original</c> is the order the user built (play now, add, move). <c>_order</c>
/// is the play order: the same list, or a shuffled copy while <see cref="Shuffle"/> is on. Turning
/// shuffle off restores the original order and keeps the current entry.
/// </remarks>
public sealed class QueueManager
{
    /// <summary>Most entries kept in <see cref="History"/>; older ones are dropped.</summary>
    public const int HistoryLimit = 200;

    private readonly Random _random;
    private readonly List<QueueItem> _original = [];
    private readonly List<QueueItem> _order = [];
    private readonly List<QueueItem> _history = [];
    private int _nextId;
    private bool _shuffle;
    private RepeatMode _repeat;

    public QueueManager()
        : this(Random.Shared)
    {
    }

    /// <summary>Creates a queue that shuffles with <paramref name="random"/> (fixed seeds in tests).</summary>
    public QueueManager(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <summary>Raised after any change to the entries, the current entry, shuffle, repeat or history.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the entries in play order.</summary>
    public IReadOnlyList<QueueItem> Items => _order;

    /// <summary>Gets the index of <see cref="Current"/> in <see cref="Items"/>, or -1 when nothing is current.</summary>
    public int CurrentIndex { get; private set; } = -1;

    public QueueItem? Current => CurrentIndex >= 0 ? _order[CurrentIndex] : null;

    /// <summary>Gets the entries played before the current one, oldest first.</summary>
    public IReadOnlyList<QueueItem> History => _history;

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value)
            {
                return;
            }

            _shuffle = value;
            var current = Current;
            if (value)
            {
                ShuffleOrder(current);
            }
            else
            {
                _order.Clear();
                _order.AddRange(_original);
                CurrentIndex = current is null ? -1 : _order.IndexOf(current);
            }

            OnChanged();
        }
    }

    public RepeatMode Repeat
    {
        get => _repeat;
        set
        {
            if (_repeat != value)
            {
                _repeat = value;
                OnChanged();
            }
        }
    }

    /// <summary>
    /// Replaces the queue with <paramref name="tracks"/> and makes the track at <paramref name="startIndex"/>
    /// current. With shuffle on, that track plays first and the rest are shuffled.
    /// </summary>
    public void PlayNow(IEnumerable<Track> tracks, int startIndex = 0)
    {
        var items = Wrap(tracks);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        if (items.Count > 0)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startIndex, items.Count);
        }

        MoveCurrentToHistory();
        _original.Clear();
        _original.AddRange(items);
        _order.Clear();
        _order.AddRange(items);
        CurrentIndex = items.Count > 0 ? startIndex : -1;
        if (_shuffle)
        {
            ShuffleOrder(Current);
        }

        OnChanged();
    }

    /// <summary>Inserts <paramref name="tracks"/> right after the current entry (at the start when nothing is current).</summary>
    public void PlayNext(IEnumerable<Track> tracks)
    {
        var items = Wrap(tracks);
        if (items.Count == 0)
        {
            return;
        }

        var current = Current;
        _order.InsertRange(CurrentIndex + 1, items);
        var originalIndex = current is null ? 0 : _original.IndexOf(current) + 1;
        _original.InsertRange(originalIndex, items);
        OnChanged();
    }

    /// <summary>Appends <paramref name="tracks"/> to the end of the queue.</summary>
    public void AddLast(IEnumerable<Track> tracks)
    {
        var items = Wrap(tracks);
        if (items.Count == 0)
        {
            return;
        }

        _order.AddRange(items);
        _original.AddRange(items);
        OnChanged();
    }

    /// <summary>
    /// Inserts <paramref name="tracks"/> among the upcoming entries: at <paramref name="position"/> 0 they
    /// play next, at the number of upcoming entries (or more) they go last. With shuffle on, the original
    /// order gets them before the entry they now precede.
    /// </summary>
    public void InsertUpcoming(IEnumerable<Track> tracks, int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        var items = Wrap(tracks);
        if (items.Count == 0)
        {
            return;
        }

        var index = CurrentIndex + 1 + Math.Min(position, _order.Count - CurrentIndex - 1);
        var following = index < _order.Count ? _order[index] : null;
        _order.InsertRange(index, items);
        _original.InsertRange(following is null ? _original.Count : _original.IndexOf(following), items);
        OnChanged();
    }

    /// <summary>
    /// Removes an entry. When it is the current entry, the next one becomes current (or the previous one
    /// when it was the last). Returns <see langword="false"/> when the entry is not in the queue.
    /// </summary>
    public bool Remove(string itemId)
    {
        var index = IndexOf(itemId);
        if (index < 0)
        {
            return false;
        }

        var item = _order[index];
        _order.RemoveAt(index);
        _original.Remove(item);

        if (index < CurrentIndex || (index == CurrentIndex && CurrentIndex == _order.Count))
        {
            CurrentIndex--;
        }

        OnChanged();
        return true;
    }

    /// <summary>
    /// Moves an entry to <paramref name="newIndex"/> in play order; the current entry stays current.
    /// Without shuffle this also changes the original order.
    /// </summary>
    public bool Move(string itemId, int newIndex)
    {
        var index = IndexOf(itemId);
        if (index < 0)
        {
            return false;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, _order.Count);
        if (index == newIndex)
        {
            return true;
        }

        var current = Current;
        var item = _order[index];
        _order.RemoveAt(index);
        _order.Insert(newIndex, item);
        if (!_shuffle)
        {
            _original.Clear();
            _original.AddRange(_order);
        }

        CurrentIndex = current is null ? -1 : _order.IndexOf(current);
        OnChanged();
        return true;
    }

    /// <summary>Empties the queue. History is kept.</summary>
    public void Clear()
    {
        if (_order.Count == 0)
        {
            return;
        }

        MoveCurrentToHistory();
        _order.Clear();
        _original.Clear();
        CurrentIndex = -1;
        OnChanged();
    }

    /// <summary>Removes the entries after the current one; the current entry keeps playing.</summary>
    public void ClearUpcoming()
    {
        var start = CurrentIndex + 1;
        if (CurrentIndex < 0 || start >= _order.Count)
        {
            return;
        }

        var removed = _order.GetRange(start, _order.Count - start).ToHashSet();
        _order.RemoveRange(start, _order.Count - start);
        _original.RemoveAll(removed.Contains);
        OnChanged();
    }

    /// <summary>Makes an entry current, e.g. when the user picks it in the queue panel.</summary>
    public bool JumpTo(string itemId)
    {
        var index = IndexOf(itemId);
        if (index < 0)
        {
            return false;
        }

        if (index != CurrentIndex)
        {
            MoveCurrentToHistory();
            CurrentIndex = index;
            OnChanged();
        }

        return true;
    }

    /// <summary>Gets the entry that plays when the current one ends, for gapless pre-loading.</summary>
    public QueueItem? PeekNext()
    {
        if (CurrentIndex < 0)
        {
            return null;
        }

        return _repeat == RepeatMode.One ? Current : At(CurrentIndex + 1);
    }

    /// <summary>
    /// The current track ended on its own. Repeat One keeps it; otherwise the next entry becomes current.
    /// Returns the entry to play, or <see langword="null"/> at the end of the queue (the last entry stays
    /// current so Previous still works).
    /// </summary>
    public QueueItem? TrackEnded()
    {
        if (CurrentIndex < 0)
        {
            return null;
        }

        if (_repeat == RepeatMode.One)
        {
            AddToHistory(Current!);
            OnChanged();
            return Current;
        }

        return Advance();
    }

    /// <summary>
    /// The user pressed Next: always moves on, also with Repeat One. Returns the new current entry, or
    /// <see langword="null"/> at the end of the queue without Repeat All.
    /// </summary>
    public QueueItem? Next() => CurrentIndex < 0 ? null : Advance();

    /// <summary>
    /// The user pressed Previous: the entry before the current one becomes current. At the start it wraps
    /// to the end with Repeat All, otherwise the first entry stays current. Restarting the current track
    /// instead (after a few seconds) is the engine's decision.
    /// </summary>
    public QueueItem? Previous()
    {
        if (CurrentIndex < 0)
        {
            return null;
        }

        var target = CurrentIndex > 0 ? CurrentIndex - 1 : _repeat == RepeatMode.All ? _order.Count - 1 : 0;
        if (target != CurrentIndex)
        {
            MoveCurrentToHistory();
            CurrentIndex = target;
            OnChanged();
        }

        return Current;
    }

    private QueueItem? Advance()
    {
        var next = At(CurrentIndex + 1);
        if (next is null)
        {
            return null;
        }

        MoveCurrentToHistory();
        CurrentIndex = _order.IndexOf(next);
        OnChanged();
        return next;
    }

    /// <summary>The entry at <paramref name="index"/>, wrapping past the end with Repeat All.</summary>
    private QueueItem? At(int index)
    {
        if (index < _order.Count)
        {
            return _order[index];
        }

        return _repeat == RepeatMode.All && _order.Count > 0 ? _order[index % _order.Count] : null;
    }

    /// <summary>Shuffles the play order; <paramref name="first"/> (the current entry) goes to the front.</summary>
    private void ShuffleOrder(QueueItem? first)
    {
        _order.Clear();
        _order.AddRange(_original);
        if (first is not null)
        {
            _order.Remove(first);
        }

        // Fisher-Yates.
        for (var i = _order.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }

        if (first is not null)
        {
            _order.Insert(0, first);
            CurrentIndex = 0;
        }
    }

    private void MoveCurrentToHistory()
    {
        if (Current is { } current)
        {
            AddToHistory(current);
        }
    }

    private void AddToHistory(QueueItem item)
    {
        _history.Add(item);
        if (_history.Count > HistoryLimit)
        {
            _history.RemoveRange(0, _history.Count - HistoryLimit);
        }
    }

    private int IndexOf(string itemId) => _order.FindIndex(i => i.Id == itemId);

    private List<QueueItem> Wrap(IEnumerable<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        return tracks.Select(t => new QueueItem((++_nextId).ToString(CultureInfo.InvariantCulture), t)).ToList();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
