using System.Linq;

namespace Age.Engine.Model;

/// <summary>A renderable view of one visible gfx object — the host composites these in ascending-handle order
/// (= the engine's z-order) each frame. Built by <see cref="GfxState.SnapshotVisibleObjects"/>; the surface
/// resId/colorkey are resolved from the object's live source slot at snapshot time (see docs/engine-re.md,
/// "The full gfx render model").</summary>
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY);

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
        // draw-texture bind (gfx_object_bind_draw): the surface to draw + its source rect + the visible flag.
        public int SourceSlot = -1;
        public (int X, int Y, int W, int H) SrcRect;
        public bool Visible;
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
        // Registry/slot cleanup (native gfx_registry_erase): removes the object from the registry, so it stops
        // compositing next frame. Faithful to the engine (the render loop iterates the registry).
        lock (_lock)
        {
            if (count > 1) for (long i = handle; i < handle + count; i++) Release(i);
            else Release(handle);
        }
    }

    private readonly object _lock = new();

    // ---- surfaces (image buffers per slot): ctx+0x52bd4[slot], from create/set-texture ----
    private readonly Dictionary<int, (long ResId, long ColorKey)> _surfaces = new();
    public void SetSurface(int slot, long resId, long colorKey) { lock (_lock) { _surfaces[slot] = (resId, colorKey); } }
    public void ClearSurface(int slot) { lock (_lock) { _surfaces[slot] = (0, 0); } }   // create-texture (blank)

    /// <summary>draw-texture bind (gfx_object_bind_draw): object <paramref name="handle"/> draws surface
    /// <paramref name="slot"/>'s rect at (dstX,dstY) and becomes visible.</summary>
    public void BindDraw(long handle, int slot, int sx, int sy, int w, int h, int dstX, int dstY)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.SourceSlot = slot; o.SrcRect = (sx, sy, w, h); o.V24 = (dstX, dstY, 0); o.Visible = true;
        }
    }

    /// <summary>Visible objects in ascending-handle order (= the engine's z-order), each with its source
    /// surface (resId/colorkey) resolved from its live source slot — for the host per-frame compositor.</summary>
    public IReadOnlyList<RenderObject> SnapshotVisibleObjects()
    {
        lock (_lock)
        {
            var list = new List<RenderObject>();
            foreach (var kv in _objects.OrderBy(k => k.Key))
            {
                var o = kv.Value;
                if (!o.Visible) continue;
                var (resId, ck) = _surfaces.TryGetValue(o.SourceSlot, out var s) ? s : (0L, 0L);
                list.Add(new RenderObject(kv.Key, resId, ck, o.SrcRect.X, o.SrcRect.Y, o.SrcRect.W, o.SrcRect.H,
                                          (int)o.V24.X, (int)o.V24.Y));
            }
            return list;
        }
    }

    /// <summary>Pack (alpha, rgb) → 0xAARRGGBB, matching op 0x202/0x203's handler bit-manipulation for the
    /// common (non-negative-sentinel) case. The alpha&lt;0 / color&lt;0 native-fetch path is deferred.</summary>
    public static long PackColor(long alpha, long color)
        => ((alpha & 0xff) << 24) | (color & 0xffffff);
}
