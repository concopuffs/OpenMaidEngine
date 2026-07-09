using System.Linq;

namespace Age.Engine.Model;

/// <summary>Per-object animation channel snapshot for the compositor (cluster 0x21c-0x243). Enabled = the
/// object has an active anim channel; (TX,TY,TZ) = the transform target it animates toward; Normalized = the
/// 0x21e ~percent variant; DurationTicks = the object's own duration (anim-start op2). The GLOBAL clock timebase
/// (duration + generation) is read separately off <see cref="GfxState.AnimClockDurationTicks"/>. Generation bumps
/// on each anim-start — the compositor re-triggers its wall-clock tween when it changes.</summary>
public readonly record struct AnimState(bool Enabled, bool Normalized, long TX, long TY, long TZ,
                                        long DurationTicks, long Generation);

/// <summary>A renderable view of one visible gfx object — the host composites these in ascending-handle order
/// (= the engine's z-order) each frame. Built by <see cref="GfxState.SnapshotVisibleObjects"/>; the surface
/// resId/colorkey are resolved from the object's live source slot at snapshot time (see docs/engine-re.md,
/// "The full gfx render model").</summary>
/// <summary><paramref name="Alpha"/> is the object's OPACITY (0-255). <paramref name="TintStrength"/> is how
/// strongly <paramref name="Tint"/> (RGB) is blended into the texel (0=keep texel, 255=full tint) — this is the
/// op 0x202/0x203/0x232 "alpha" byte, which is a tint strength, NOT opacity (conflating them made opaque CGs
/// vanish — the grey-background bug).</summary>
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY,
                                           AnimState Anim, int Alpha, long Tint, int TintStrength, BlendKind Blend);

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
        public bool HasColor;   // true once op 0x202/0x203 set a color/alpha modulation on this object

        // ---- src-rect / spritesheet-cell channel (ops 0x239 static cell, 0x231 animate). Interpolator
        // SRC-RECT SCROLL channel: period obj+0x230, start obj+0x21c, grid obj+0x238/0x23c. ----
        public long SrcGridW = 1, SrcGridH = 1, SrcCell, SrcPeriod, SrcStart = -1;
        public bool SrcAnim;
        // ---- animated color/glow channel (op 0x232). Interpolator COLOR channel: period obj+0x220,
        // start obj+0x20c, target obj+0x240 — PING-PONG (distinct from static 0x202/0x203). ----
        public long ColorPeriod, ColorStart = -1, ColorTarget;
        public bool ColorAnim;
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
        // Locked: called from the VM thread (directly by 0x217/0x219/0x1ff/0x212/0x213 and inside BindDraw/anim
        // ops) while the main-thread compositor enumerates _objects in SnapshotVisibleObjects. _lock is re-entrant
        // (Monitor) so the callers that already hold it are fine.
        lock (_lock)
        {
            if (!_objects.TryGetValue(handle, out var o)) { o = new GfxObject(); _objects[handle] = o; }
            CurrentObject = handle;
            return o;
        }
    }

    /// <summary>Op 0x1a2 (gfx-cmd-register, native FUN_0042d360 -> FUN_0042cf70 hash insert): add the handle to
    /// the op-0x215 query registry. Native inserts map[handle]=handle; QuerySlot returns that value (handle) or
    /// -1. Only this op populates the query registry — geometry/draw ops do not.</summary>
    public void Register(long handle) { lock (_lock) { _registry.Add(handle); } }

    public GfxObject? TryGet(long handle) => _objects.TryGetValue(handle, out var o) ? o : null;

    /// <summary>Op 0x215 (query-gfx-object): native returns std::map::find(handle) — the registered value (=handle),
    /// or 0xffffffff (=-1) when the handle was never 0x1a2-registered. NOT a fabricated slot allocator.</summary>
    public int QuerySlot(long handle) => _registry.Contains(handle) ? (int)handle : -1;
    public long QueryField(long idx) => _fieldTable.TryGetValue(idx, out var v) ? v : 0;

    public void Release(long handle)
    {
        lock (_lock)   // re-entrant: EraseRange already holds _lock; op 0x1fa calls this directly
        {
            _objects.Remove(handle);
            _registry.Remove(handle);        // op 0x1fa/0x1f7 also tear down the query registration
        }
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

    /// <summary>Ops 0x202/0x203: record a packed 0xAARRGGBB color/alpha modulation on the object and mark it
    /// HasColor so the compositor applies alpha+tint (vs the opaque default).</summary>
    public void SetObjectColor(long handle, long packed)
    {
        lock (_lock) { var o = GetOrCreate(handle); o.Color = packed; o.HasColor = true; }
    }

    /// <summary>Ops 0x239 (static cell, period=0) / 0x231 (animate, period&gt;0): set the spritesheet grid +
    /// the visible cell. When animated, the interpolator ping-pongs the cell across the grid over the period.</summary>
    public void SetSrcRect(long handle, long gridW, long gridH, long cell, long period)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.SrcGridW = gridW < 1 ? 1 : gridW; o.SrcGridH = gridH < 1 ? 1 : gridH;
            o.SrcCell = cell; o.SrcPeriod = period; o.SrcStart = -1; o.SrcAnim = true;
        }
    }

    /// <summary>Op 0x232 (anim-color): ping-pong the object's color/alpha toward <paramref name="target"/>
    /// (packed 0xAARRGGBB) over <paramref name="period"/> ms — the pulsing glow. Distinct from static 0x202/0x203.</summary>
    public void SetColorAnim(long handle, long period, long target)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.ColorPeriod = period; o.ColorTarget = target; o.ColorStart = -1; o.ColorAnim = true;
        }
    }
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

    /// <summary>Back-compat: snapshot with no animation clock (nowMs = 0) — deterministic, for headless
    /// callers and existing tests.</summary>
    public IReadOnlyList<RenderObject> SnapshotVisibleObjects() => SnapshotVisibleObjects(0);

    /// <summary>Visible objects in ascending-handle order (= z-order), each with its source surface resolved
    /// and its active anim channels interpolated at <paramref name="nowMs"/> (the port of
    /// gfx_object_anim_interpolate). Position is the base V24 (a direct transform, ops 0x22f/0x229). The
    /// src-rect channel (0x239/0x231) selects the spritesheet cell; the color channel (0x232) ping-pongs the
    /// alpha/tint. Channel Start fields seed to nowMs on first sight.</summary>
    public IReadOnlyList<RenderObject> SnapshotVisibleObjects(long nowMs)
    {
        lock (_lock)
        {
            var list = new List<RenderObject>();
            foreach (var kv in _objects.OrderBy(k => k.Key))
            {
                var o = kv.Value;
                if (!o.Visible) continue;
                var (resId, ck) = _surfaces.TryGetValue(o.SourceSlot, out var s) ? s : (0L, 0L);

                // ---- color: object stays OPAQUE; the op-0x202/0x203 alpha is a TINT STRENGTH (0=keep texel,
                // 255=full tint), NOT opacity. 0x232 ping-pongs the strength (+tint) toward the target (glow). ----
                int alpha = 255; long tint = 0xFFFFFF; int strength = 0; var blend = BlendKind.Opaque;
                if (o.HasColor)
                {
                    var (a, r, g, b) = BlendMath.UnpackArgb(o.Color);
                    strength = a; tint = ((long)r << 16) | ((long)g << 8) | (long)b; blend = BlendKind.Alpha;
                }
                if (o.ColorAnim)
                {
                    if (o.ColorStart < 0) o.ColorStart = nowMs;
                    double t = PingPongWeight(nowMs, o.ColorStart, o.ColorPeriod);
                    var (ta, tr, tg, tb) = BlendMath.UnpackArgb(o.ColorTarget);
                    var (br, bg, bb) = ((int)((tint >> 16) & 0xff), (int)((tint >> 8) & 0xff), (int)(tint & 0xff));
                    strength = (int)(strength + (ta - strength) * t);
                    tint = ((long)(br + (tr - br) * t) << 16) | ((long)(bg + (tg - bg) * t) << 8) | (long)(bb + (tb - bb) * t);
                    blend = BlendKind.Alpha;
                }

                // ---- src-rect: pick the spritesheet cell (static or ping-ponged across the grid) ----
                int srcX = o.SrcRect.X, srcY = o.SrcRect.Y, w = o.SrcRect.W, h = o.SrcRect.H;
                if (o.SrcAnim && o.SrcGridW >= 1)
                {
                    int cellW = (int)(o.SrcRect.W / o.SrcGridW);
                    int cellH = o.SrcGridH >= 1 ? (int)(o.SrcRect.H / o.SrcGridH) : o.SrcRect.H;
                    long cell = o.SrcCell;
                    if (o.SrcPeriod > 0)
                    {
                        if (o.SrcStart < 0) o.SrcStart = nowMs;
                        double t = PingPongWeight(nowMs, o.SrcStart, o.SrcPeriod);
                        cell = (long)System.Math.Round(t * (o.SrcGridW - 1));
                    }
                    long cols = o.SrcGridW;
                    srcX = o.SrcRect.X + (int)(cell % cols) * cellW;
                    srcY = o.SrcRect.Y + (int)(cell / cols) * cellH;
                    w = cellW; h = cellH;
                }

                list.Add(new RenderObject(kv.Key, resId, ck, srcX, srcY, w, h,
                                          (int)o.V24.X, (int)o.V24.Y,
                                          new AnimState(o.AnimEnabled, o.AnimNormalized,
                                                        o.AnimTarget.X, o.AnimTarget.Y, o.AnimTarget.Z,
                                                        o.AnimDurationTicks, o.AnimGeneration),
                                          alpha, tint, strength, blend));
            }
            return list;
        }
    }

    /// <summary>Ping-pong interpolation weight in [0,1] toward the target: 0 at cycle start, 1 at half-period.</summary>
    private static double PingPongWeight(long now, long start, long period)
    {
        if (period <= 0) return 0;
        long half = period / 2; if (half <= 0) return 0;
        return (double)BlendMath.PingPong(now, start, period) / half;
    }

    /// <summary>Pack (alpha, rgb) → 0xAARRGGBB, matching op 0x202/0x203's handler bit-manipulation for the
    /// common (non-negative-sentinel) case. The alpha&lt;0 / color&lt;0 native-fetch path is deferred.</summary>
    public static long PackColor(long alpha, long color)
        => ((alpha & 0xff) << 24) | (color & 0xffffff);
}
