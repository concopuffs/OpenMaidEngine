namespace Age.Engine.Model;

/// <summary>A retained composite layer captured when the VM executes draw-texture — the host clears and
/// re-composites the layer set each frame (replacing the immediate-mode permanent canvas). Keyed by the
/// object <see cref="Handle"/> so a re-draw updates in place and an erase (op 0x1f7) removes it.</summary>
public readonly record struct DrawLayer(long Handle, int Slot, int SrcX, int SrcY, int W, int H, int DstX, int DstY);

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

    /// <summary>Op 0x1f7 semantics (native gfx_registry_erase_range @0x47d8b0): erase handles in
    /// [handle, handle+count) when count>1, else just <paramref name="handle"/>. It is a teardown/erase,
    /// NOT a create — objects are created lazily by the geometry SET ops (gfx_object_get_or_create).</summary>
    public void EraseRange(long handle, long count)
    {
        if (count > 1) for (long i = handle; i < handle + count; i++) { Release(i); RemoveLayers(i); }
        else { Release(handle); RemoveLayers(handle); }
    }

    // ---- retained composite layers (mutated on the VM thread; snapshotted on the host render thread) ----
    private readonly List<DrawLayer> _layers = new();
    private readonly object _lock = new();

    /// <summary>Record a draw: update the layer with this handle in place (preserving composite order), else
    /// append. Order = execution order = paint order.</summary>
    public void AddOrUpdateLayer(DrawLayer l)
    {
        lock (_lock)
        {
            for (int i = 0; i < _layers.Count; i++)
                if (_layers[i].Handle == l.Handle) { _layers[i] = l; return; }
            _layers.Add(l);
        }
    }

    public void RemoveLayers(long handle) { lock (_lock) { _layers.RemoveAll(l => l.Handle == handle); } }

    /// <summary>An immutable snapshot of the current layers, in composite order (for the host render loop).</summary>
    public IReadOnlyList<DrawLayer> SnapshotLayers() { lock (_lock) { return _layers.ToArray(); } }

    /// <summary>Pack (alpha, rgb) → 0xAARRGGBB, matching op 0x202/0x203's handler bit-manipulation for the
    /// common (non-negative-sentinel) case. The alpha&lt;0 / color&lt;0 native-fetch path is deferred.</summary>
    public static long PackColor(long alpha, long color)
        => ((alpha & 0xff) << 24) | (color & 0xffffff);
}
