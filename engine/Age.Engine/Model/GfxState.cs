namespace Age.Engine.Model;

/// <summary>Host-agnostic model of the AGE native gfx command-buffer (reversed in
/// docs/engine-re.md, gfx op-contract table). One registry maps an object handle to a GfxObject — the
/// native ctx+0x408 map that op 0x215 queries and the geometry get/set ops share. Each object carries a
/// slot (returned by 0x215) and three 3-vectors: V18 (set 0x217 / get 0x218, anchor), V24 (set 0x219 /
/// get 0x21a, position), V16c (set 0x1ff). The native DirectDraw workers are NOT modelled — only the data
/// the query ops read back, which is all the bytecode geometry math needs.</summary>
public sealed class GfxState
{
    public sealed class GfxObject
    {
        public int Slot = -1;
        public (long X, long Y, long Z) V18, V24, V16c;
        public long Field64, Field68, Field6c;
        public long Color;
    }

    private readonly Dictionary<long, GfxObject> _objects = new();
    private readonly SortedSet<int> _free = new();
    private int _nextSlot = 4;                       // observed native slot range is 4..13
    private readonly Dictionary<long, long> _fieldTable = new();   // ctx+0x46d14 (0x216); no family writer -> default 0
    public long CurrentObject { get; private set; }

    /// <summary>Live objects and their slots — for the CLI gfx oracle (Task 3.7).</summary>
    public IEnumerable<(long Handle, int Slot)> Objects
    {
        get { foreach (var kv in _objects) yield return (kv.Key, kv.Value.Slot); }
    }

    private int AcquireSlot()
    {
        if (_free.Count > 0) { int s = _free.Min; _free.Remove(s); return s; }
        return _nextSlot++;
    }

    public GfxObject GetOrCreate(long handle)
    {
        if (!_objects.TryGetValue(handle, out var o))
        {
            o = new GfxObject { Slot = AcquireSlot() };
            _objects[handle] = o;
        }
        CurrentObject = handle;
        return o;
    }

    public GfxObject? TryGet(long handle) => _objects.TryGetValue(handle, out var o) ? o : null;
    public int QuerySlot(long handle) => _objects.TryGetValue(handle, out var o) ? o.Slot : -1;
    public long QueryField(long idx) => _fieldTable.TryGetValue(idx, out var v) ? v : 0;

    public void Release(long handle)
    {
        if (_objects.TryGetValue(handle, out var o)) { if (o.Slot >= 0) _free.Add(o.Slot); _objects.Remove(handle); }
    }

    /// <summary>Pack (alpha, rgb) → 0xAARRGGBB, matching op 0x202/0x203's handler bit-manipulation for the
    /// common (non-negative-sentinel) case. The alpha&lt;0 / color&lt;0 native-fetch path is deferred.</summary>
    public static long PackColor(long alpha, long color)
        => ((alpha & 0xff) << 24) | (color & 0xffffff);
}
