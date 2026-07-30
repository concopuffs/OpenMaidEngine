namespace Age.Engine.Text;

/// <summary>A small thread-safe LRU used by glyph masks and future backend-owned font handles.</summary>
public sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private sealed record Entry(TKey Key, TValue Value);

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Action<TValue>? _onEvicted;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recency = new();

    public BoundedLruCache(int capacity, Action<TValue>? onEvicted = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _onEvicted = onEvicted;
    }

    public int Capacity => _capacity;

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                value = default!;
                return false;
            }
            _recency.Remove(node);
            _recency.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value)
    {
        List<TValue>? evicted = null;
        lock (_gate)
        {
            if (_entries.Remove(key, out LinkedListNode<Entry>? existing))
            {
                _recency.Remove(existing);
                (evicted ??= new List<TValue>(2)).Add(existing.Value.Value);
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value));
            _recency.AddFirst(node);
            _entries.Add(key, node);
            if (_entries.Count > _capacity)
            {
                LinkedListNode<Entry> oldest = _recency.Last!;
                _recency.RemoveLast();
                _entries.Remove(oldest.Value.Key);
                (evicted ??= new List<TValue>(1)).Add(oldest.Value.Value);
            }
        }
        if (_onEvicted != null && evicted != null)
            foreach (TValue removed in evicted) _onEvicted(removed);
    }

    public void Clear()
    {
        TValue[] removed;
        lock (_gate)
        {
            removed = _recency.Select(entry => entry.Value).ToArray();
            _entries.Clear();
            _recency.Clear();
        }
        if (_onEvicted != null)
            foreach (TValue value in removed) _onEvicted(value);
    }
}

/// <summary>Bounded caching decorator shared by exact and portable glyph-mask backends.</summary>
public sealed class CachedGlyphMaskRasterizer : IGlyphMaskRasterizer
{
    private readonly IGlyphMaskRasterizer _inner;
    private readonly BoundedLruCache<GlyphRasterRequest, GlyphMask> _cache;
    private long _hits;
    private long _misses;

    public CachedGlyphMaskRasterizer(IGlyphMaskRasterizer inner, int capacity)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = new BoundedLruCache<GlyphRasterRequest, GlyphMask>(capacity);
    }

    public int Count => _cache.Count;
    public int Capacity => _cache.Capacity;
    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);

    public GlyphMask Rasterize(GlyphRasterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_cache.TryGetValue(request, out GlyphMask? cached))
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        GlyphMask result = _inner.Rasterize(request)
            ?? throw new InvalidOperationException("The glyph rasterizer returned null.");
        _cache.Set(request, result);
        Interlocked.Increment(ref _misses);
        return result;
    }

    public void Clear() => _cache.Clear();
}
