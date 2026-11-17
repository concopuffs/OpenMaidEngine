namespace Ome.Engine.Model;

/// <summary>
/// A cache bounded by the bytes its entries hold. <see cref="Trim"/> drops the least recently used entries until the
/// cache is within its budget, except those used since the last <see cref="BeginFrame"/>: a renderer that calls it
/// once per frame never loses what the frame it is drawing uses, and the cache may exceed its budget for that frame.
/// Without a frame, every entry may go. Not thread-safe: each cache belongs to one thread.
/// </summary>
public sealed class ByteBudgetCache<TKey, TValue> where TKey : notnull
{
    private sealed class Entry(TValue value, long bytes, long lastUse)
    {
        public TValue Value = value;
        public long Bytes = bytes;
        public long LastUse = lastUse;
    }

    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly Action<TKey, TValue>? _dropped;
    private long _clock;
    private long _frameStart = long.MaxValue;

    /// <param name="budgetBytes">The bytes <see cref="Trim"/> brings the cache back to.</param>
    /// <param name="dropped">Called for each entry the cache drops (trimmed, removed or cleared), to release what it
    /// holds outside the managed heap.</param>
    public ByteBudgetCache(long budgetBytes, Action<TKey, TValue>? dropped = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        BudgetBytes = budgetBytes;
        _dropped = dropped;
    }

    public long BudgetBytes { get; }

    /// <summary>The bytes the entries hold, as each was set.</summary>
    public long Bytes { get; private set; }

    public int Count => _entries.Count;

    /// <summary>The entries <see cref="Trim"/> has dropped since the cache was made.</summary>
    public long Trimmed { get; private set; }

    /// <summary>Marks the start of a frame: the entries used from now on are the frame's, which <see cref="Trim"/>
    /// keeps.</summary>
    public void BeginFrame() => _frameStart = _clock + 1;

    /// <summary>The entry under <paramref name="key"/>, which now counts as just used.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            entry.LastUse = ++_clock;
            value = entry.Value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Adds the entry, or replaces the one under <paramref name="key"/> without calling the drop callback
    /// for it (the caller made the new value from the old one, or released the old one itself).</summary>
    public void Set(TKey key, TValue value, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            Bytes += bytes - entry.Bytes;
            entry.Value = value;
            entry.Bytes = bytes;
            entry.LastUse = ++_clock;
            return;
        }
        _entries[key] = new Entry(value, bytes, ++_clock);
        Bytes += bytes;
    }

    public void Remove(TKey key)
    {
        if (_entries.Remove(key, out Entry? entry)) Drop(key, entry);
    }

    /// <summary>Removes every entry whose key matches.</summary>
    public void RemoveWhere(Func<TKey, bool> match)
    {
        foreach (TKey key in _entries.Keys.Where(match).ToList()) Remove(key);
    }

    public void Clear()
    {
        foreach (TKey key in _entries.Keys.ToList()) Remove(key);
    }

    /// <summary>Drops the least recently used entries, outside the current frame, until the cache holds no more than
    /// its budget.</summary>
    public void Trim()
    {
        if (Bytes <= BudgetBytes) return;
        foreach (var (key, entry) in _entries.Where(pair => pair.Value.LastUse < _frameStart)
                     .OrderBy(pair => pair.Value.LastUse).ToList())
        {
            if (Bytes <= BudgetBytes) break;
            _entries.Remove(key);
            Drop(key, entry);
            Trimmed++;
        }
    }

    private void Drop(TKey key, Entry entry)
    {
        Bytes -= entry.Bytes;
        _dropped?.Invoke(key, entry.Value);
    }
}
