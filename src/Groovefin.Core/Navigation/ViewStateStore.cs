namespace Groovefin.Core.Navigation;

/// <summary>
/// What a page showed when it was left (#284): for each element the page marks, in the page's order, its
/// scroll position, its selected tab and the item that had focus.
/// </summary>
public sealed record ViewState(IReadOnlyList<ElementState> Elements);

/// <summary>The remembered state of one marked element of a page; what does not apply stays empty.</summary>
public sealed record ElementState
{
    /// <summary>Gets the vertical scroll offset of a list or scroll viewer.</summary>
    public double VerticalOffset { get; init; }

    /// <summary>Gets the index of a tab bar's selected tab.</summary>
    public int? SelectedIndex { get; init; }

    /// <summary>Gets the index of the list's item that had focus.</summary>
    public int? FocusedIndex { get; init; }

    /// <summary>Gets a value indicating whether that item had keyboard focus (it gets its focus visual back).</summary>
    public bool FocusedByKeyboard { get; init; }
}

/// <summary>
/// The view states of the pages in the back and forward stacks (#284), by the ID of their stack entry. A
/// page saves its state when it is left and gets it back on Back or Forward; a page opened anew has a new
/// entry and so starts at the top. Entries that left both stacks are forgotten, and at most
/// <see cref="Capacity"/> are kept.
/// </summary>
public sealed class ViewStateStore
{
    /// <summary>The most states kept; the oldest saved go first.</summary>
    public const int Capacity = 100;

    private readonly Dictionary<long, ViewState> _states = [];

    /// <summary>Entry IDs, the least recently saved first.</summary>
    private readonly List<long> _order = [];

    private long _lastEntryId;

    public int Count => _states.Count;

    /// <summary>Gets an ID for a new stack entry, unique while the app runs.</summary>
    public long NewEntryId() => ++_lastEntryId;

    public void Save(long entryId, ViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _order.Remove(entryId);
        _order.Add(entryId);
        _states[entryId] = state;
        while (_order.Count > Capacity)
        {
            _states.Remove(_order[0]);
            _order.RemoveAt(0);
        }
    }

    /// <summary>Gets the state saved for the entry, or <see langword="null"/> when there is none.</summary>
    public ViewState? Get(long entryId) => _states.GetValueOrDefault(entryId);

    /// <summary>Forgets the states of all entries but these (the ones still in the back and forward stacks).</summary>
    public void Retain(IEnumerable<long> entryIds)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var live = entryIds.ToHashSet();
        foreach (var id in _order.Where(id => !live.Contains(id)).ToList())
        {
            _order.Remove(id);
            _states.Remove(id);
        }
    }
}
