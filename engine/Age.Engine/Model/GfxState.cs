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
        public (long X, long Y, long Z) V18, V24, V16c;
        public long Field64, Field68, Field6c;
        public long Color;
        // draw-texture bind (gfx_object_bind_draw): the surface to draw + its source rect + the visible flag.
        public int SourceSlot = -1;
        public (int X, int Y, int W, int H) SrcRect;
        public bool Visible;

        // ---- animation channel (cluster 0x21c-0x243; see docs/engine-re.md "sprite transform / ANIMATION").
        // 0x21e/0x220 set the transform directly (worker gfx_anim_set_channel@0x47eaa0: obj+0x3c=p1, +0x50=p2,
        // +0xac=target, +0x68=enable); 0x234 anim-start animates toward a target over the GLOBAL clock (0x238).
        // Passive: recorded here, interpolated by the Godot compositor over wall-clock. ----
        public (long X, long Y, long Z) AnimTarget;
        public long AnimParam1, AnimParam2;
        public bool AnimNormalized;         // 0x21e (operands ~percent, /_DAT_00571c28) vs 0x220 (absolute)
        public bool AnimEnabled;            // obj+0x68
        public long AnimDurationTicks;      // 0x234 anim-start op2 (this object's duration; maxed into the clock)
        public long AnimGeneration;         // bumped by anim-start (0x234); the compositor's per-object re-trigger
    }

    // ---- geometry/draw object store (V18/V24/draw bind, the compositor's input) ----
    // Populated lazily by the geometry SET ops and draw-texture. Membership here does NOT mean the object is
    // in the op-0x215 query registry (that is a SEPARATE native structure; see _registry below).
    private readonly Dictionary<long, GfxObject> _objects = new();

    // ---- op-0x215 query registry (native std::map queried by gfx_op_0x215, populated ONLY by op 0x1a2
    // gfx-cmd-register -> FUN_0042cf70 hash insert). map[handle] = handle (native stores operand1 as the value;
    // small system/UI handles double as their surface slot). CG handles are NEVER 0x1a2-registered, so
    // query-gfx-object returns -1 for them and label_12649 takes its fresh branch (correct anchor from the
    // INIT2 arrays) instead of collapsing onto a fabricated slot. See docs/engine-re.md op 0x215/0x1a2. ----
    private readonly HashSet<long> _registry = new();

    private readonly Dictionary<long, long> _fieldTable = new();   // ctx+0x46d14 (0x216); no family writer -> default 0
    public long CurrentObject { get; private set; }

    // ---- GLOBAL animation clock (op 0x238 set-anim-clock; native ctx+0x51b7c total / +0x51b78 elapsed).
    // Non-blocking: the op only configures duration; the host advances elapsed per-frame and tweens all armed
    // objects over it (docs/engine-re.md, "anim_start/set_anim_clock decoded"). Generation bumps on each set so
    // the compositor resets its wall-clock elapsed. ----
    public long AnimClockDurationTicks { get; private set; }
    public long AnimClockGeneration { get; private set; }

    /// <summary>Live geometry objects and the surface slot they draw from — for the CLI gfx oracle.</summary>
    public IEnumerable<(long Handle, int Slot)> Objects
    {
        get { foreach (var kv in _objects) yield return (kv.Key, kv.Value.SourceSlot); }
    }

    public GfxObject GetOrCreate(long handle)
    {
        if (!_objects.TryGetValue(handle, out var o)) { o = new GfxObject(); _objects[handle] = o; }
        CurrentObject = handle;
        return o;
    }

    /// <summary>Op 0x1a2 (gfx-cmd-register, native FUN_0042d360 -> FUN_0042cf70 hash insert): add the handle to
    /// the op-0x215 query registry. Native inserts map[handle]=handle; QuerySlot returns that value (handle) or
    /// -1. Only this op populates the query registry — geometry/draw ops do not.</summary>
    public void Register(long handle) => _registry.Add(handle);

    public GfxObject? TryGet(long handle) => _objects.TryGetValue(handle, out var o) ? o : null;

    /// <summary>Op 0x215 (query-gfx-object): native returns std::map::find(handle) — the registered value (=handle),
    /// or 0xffffffff (=-1) when the handle was never 0x1a2-registered. NOT a fabricated slot allocator.</summary>
    public int QuerySlot(long handle) => _registry.Contains(handle) ? (int)handle : -1;
    public long QueryField(long idx) => _fieldTable.TryGetValue(idx, out var v) ? v : 0;

    public void Release(long handle)
    {
        _objects.Remove(handle);
        _registry.Remove(handle);        // op 0x1fa/0x1f7 also tear down the query registration
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

    /// <summary>Op 0x21e/0x220 (set-anim-transform): record the transform target + two scalar params on the
    /// object and enable its animation channel. normalized = 0x21e (operands ~percent, /_DAT_00571c28);
    /// absolute = 0x220. Native worker gfx_anim_set_channel@0x47eaa0 sets obj+0x3c=p1, +0x50=p2, +0xac=target,
    /// +0x68=1.</summary>
    public void SetAnimTransform(long handle, long p1, long p2, (long X, long Y, long Z) target, bool normalized)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.AnimParam1 = p1; o.AnimParam2 = p2; o.AnimTarget = target;
            o.AnimNormalized = normalized; o.AnimEnabled = true;
        }
    }

    /// <summary>Op 0x234 (anim-start): animate the object toward <paramref name="target"/> over the global
    /// clock; <paramref name="durationTicks"/> is this object's duration (native label_1235a maxes them into
    /// the clock). Bumps AnimGeneration — the compositor's per-object re-trigger.</summary>
    public void StartAnim(long handle, long durationTicks, (long X, long Y, long Z) target)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.AnimTarget = target; o.AnimDurationTicks = durationTicks;
            o.AnimEnabled = true; o.AnimGeneration++;
        }
    }

    /// <summary>Op 0x238 (set-anim-clock): set the GLOBAL animation duration (game ticks) and bump the clock
    /// generation so the host resets its wall-clock elapsed. Non-blocking (the render loop advances it).</summary>
    public void SetAnimClock(long durationTicks)
    {
        lock (_lock) { AnimClockDurationTicks = durationTicks; AnimClockGeneration++; }
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
