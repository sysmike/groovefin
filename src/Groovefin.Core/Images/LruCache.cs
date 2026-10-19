namespace Groovefin.Core.Images;

/// <summary>
/// Small least-recently-used cache. Not thread-safe; the app uses it on the UI thread to keep decoded
/// bitmaps of visible items.
/// </summary>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    public int Capacity { get; }

    public int Count => _map.Count;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
        }
        else if (_map.Count == Capacity)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
        }

        _map[key] = _order.AddFirst((key, value));
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }
}
