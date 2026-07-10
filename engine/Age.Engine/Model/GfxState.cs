using System.Linq;

namespace Age.Engine.Model;

/// <summary>The sampled native matrix channels carried to the compositor. Op 0x21e owns scale; op 0x220 owns
/// translation. Z is retained for model fidelity even though the current 2D compositor uses X/Y only.</summary>
public readonly record struct TransformState(double ScaleX, double ScaleY, double ScaleZ,
                                             double TranslateX, double TranslateY, double TranslateZ,
                                             double AnchorX, double AnchorY, double AnchorZ);

public readonly record struct RotationCycleState(bool Enabled, long PeriodMs, long AxisX, long AxisY, long AxisZ);

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
                                           TransformState Transform, RotationCycleState Rotation,
                                           int Alpha, long Tint, int TintStrength, BlendKind Blend);

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

        // ---- independent one-shot matrix channels (gfx_object_apply_transform_channels@0x472f00). ----
        // Scale: current obj+0x6c, target obj+0xac, delay obj+0x3c, duration obj+0x50.
        public (double X, double Y, double Z) ScaleCurrent = (1, 1, 1), ScaleTarget = (1, 1, 1);
        public long ScaleDelayMs, ScaleDurationMs;
        public bool ScaleEnabled;
        // Translation: current obj+0x16c, target obj+0x1ac, delay obj+0x44, duration obj+0x58.
        public (double X, double Y, double Z) TranslationCurrent, TranslationTarget;
        public long TranslationDelayMs, TranslationDurationMs;
        public bool TranslationEnabled;
        // Shared matrix-channel start timestamp obj+0x34, seeded from frame-time ctx+0xb550.
        public long MatrixStartMs = -1;

        // Op 0x234 is a separate cyclic rotation channel (period obj+0x228, axis obj+0x244..0x24c).
        public long RotationPeriodMs;
        public (long X, long Y, long Z) RotationAxis;
        public bool RotationEnabled;
    }

    // ---- geometry/draw object store (V18/V24/draw bind, the compositor's input) ----
    // Populated lazily by the geometry SET ops and draw-texture. Op 0x215 queries this same native map and
    // returns the object's live source slot (obj+4), or -1 when the handle has not been drawn/bound yet.
    private readonly Dictionary<long, GfxObject> _objects = new();

    // ---- opcode 0x1a2's operand-descriptor registry. Native op 0x1a2 hashes the lvalue descriptor string;
    // it is separate from the retained-object map queried by op 0x215. We retain membership for diagnostics
    // and teardown parity, but it does not make an undrawn gfx object queryable as a surface slot. ----
    private readonly HashSet<long> _operandRegistry = new();

    private readonly Dictionary<long, long> _fieldTable = new();   // ctx+0x46d14 (0x216); no family writer -> default 0
    public long CurrentObject { get; private set; }

    // ---- Separate global animation service clock (op 0x238; ctx+0x51b7c total / +0x51b78 elapsed).
    // Retained for its opcode family; 0x21e scale and 0x220 translation use frame-time directly instead. ----
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

    /// <summary>Op 0x1a2: retain the operand's current value in the separate descriptor registry. This does
    /// not populate the retained-object map used by op 0x215.</summary>
    public void Register(long handle) { lock (_lock) { _operandRegistry.Add(handle); } }

    public GfxObject? TryGet(long handle) => _objects.TryGetValue(handle, out var o) ? o : null;

    /// <summary>Op 0x215: look up <paramref name="handle"/> in the retained gfx-object map and return obj+4,
    /// the live source-surface slot written by draw-texture, or -1 when absent/unbound.</summary>
    public int QuerySlot(long handle)
    {
        lock (_lock)
            return _objects.TryGetValue(handle, out var o) ? o.SourceSlot : -1;
    }
    public bool IsRegistered(long handle) { lock (_lock) { return _operandRegistry.Contains(handle); } }
    public long QueryField(long idx) => _fieldTable.TryGetValue(idx, out var v) ? v : 0;

    public void Release(long handle)
    {
        lock (_lock)   // re-entrant: EraseRange already holds _lock
        {
            _objects.Remove(handle);
            _operandRegistry.Remove(handle);
        }
    }

    /// <summary>Op 0x1f7 semantics (native gfx_object_erase_range @0x47d8b0): erase handles in
    /// [handle, handle+count) when count>1, else just <paramref name="handle"/>. It is a teardown/erase,
    /// NOT a create — objects are created lazily by the geometry SET ops (gfx_object_get_or_create).</summary>
    public void EraseRange(long handle, long count)
    {
        // Retained-object cleanup (native gfx_object_erase): removes the object from the map, so it stops
        // compositing next frame. Faithful to the engine (the render loop iterates the retained-object map).
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

    /// <summary>Op 0x21e: normalized scale target (100 = identity), with independent delay/duration.</summary>
    public void SetScaleChannel(long handle, long delayMs, long durationMs, (long X, long Y, long Z) percent)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.ScaleDelayMs = delayMs; o.ScaleDurationMs = durationMs;
            o.ScaleTarget = (percent.X / 100.0, percent.Y / 100.0, percent.Z / 100.0);
            o.ScaleEnabled = durationMs > 0; o.MatrixStartMs = -1;
        }
    }

    /// <summary>Op 0x220: absolute translation target, with independent delay/duration.</summary>
    public void SetTranslationChannel(long handle, long delayMs, long durationMs, (long X, long Y, long Z) target)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.TranslationDelayMs = delayMs; o.TranslationDurationMs = durationMs;
            o.TranslationTarget = target;
            o.TranslationEnabled = durationMs > 0; o.MatrixStartMs = -1;
        }
    }

    /// <summary>Op 0x234: retain the cyclic rotation period and axis separately. Native interpolation uses
    /// frame-time ctx+0xb550 and rotates through 360 degrees per period; affine rendering is deferred.</summary>
    public void SetRotationCycle(long handle, long periodMs, (long X, long Y, long Z) axis)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.RotationPeriodMs = periodMs; o.RotationAxis = axis; o.RotationEnabled = periodMs > 0;
        }
    }

    /// <summary>Op 0x238: set its separate global animation-service duration and reset marker.</summary>
    public void SetAnimClock(long durationTicks)
    {
        lock (_lock) { AnimClockDurationTicks = durationTicks; AnimClockGeneration++; }
    }

    /// <summary>Op 0x243: reset the separate global animation-service clock.</summary>
    public void ResetAnimClock()
    {
        lock (_lock) { AnimClockDurationTicks = 0; AnimClockGeneration++; }
    }

    /// <summary>Back-compat: snapshot with no animation clock (nowMs = 0) — deterministic, for headless
    /// callers and existing tests.</summary>
    public IReadOnlyList<RenderObject> SnapshotVisibleObjects() => SnapshotVisibleObjects(0);

    /// <summary>Visible objects in ascending-handle order (= z-order), each with its source surface resolved
    /// and its active channels interpolated at <paramref name="nowMs"/>. Position is the base V24 (a direct
    /// transform, ops 0x22f/0x229); scale and translation are independent one-shot matrix channels. The
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

                // One-shot matrix channels: hold current through delay, then linearly sample current -> target.
                if ((o.ScaleEnabled || o.TranslationEnabled) && o.MatrixStartMs < 0) o.MatrixStartMs = nowMs;
                var scale = SampleMatrixChannel(ref o.ScaleCurrent, o.ScaleTarget, o.ScaleDelayMs,
                                                o.ScaleDurationMs, o.MatrixStartMs, ref o.ScaleEnabled, nowMs);
                var translation = SampleMatrixChannel(ref o.TranslationCurrent, o.TranslationTarget,
                                                      o.TranslationDelayMs, o.TranslationDurationMs,
                                                      o.MatrixStartMs, ref o.TranslationEnabled, nowMs);
                if (!o.ScaleEnabled && !o.TranslationEnabled) o.MatrixStartMs = -1;

                list.Add(new RenderObject(kv.Key, resId, ck, srcX, srcY, w, h,
                                          (int)o.V24.X, (int)o.V24.Y,
                                          new TransformState(scale.X, scale.Y, scale.Z,
                                                             translation.X, translation.Y, translation.Z,
                                                             o.V18.X, o.V18.Y, o.V18.Z),
                                          new RotationCycleState(o.RotationEnabled, o.RotationPeriodMs,
                                                                 o.RotationAxis.X, o.RotationAxis.Y,
                                                                 o.RotationAxis.Z),
                                          alpha, tint, strength, blend));
            }
            return list;
        }
    }

    private static (double X, double Y, double Z) SampleMatrixChannel(
        ref (double X, double Y, double Z) current,
        (double X, double Y, double Z) target,
        long delayMs, long durationMs, long startMs, ref bool enabled, long nowMs)
    {
        if (!enabled || durationMs <= 0 || startMs < 0) return current;
        long elapsed = nowMs - startMs - delayMs;
        if (elapsed <= 0) return current;
        if (elapsed >= durationMs)
        {
            current = target;
            enabled = false;
            return current;
        }
        double t = (double)elapsed / durationMs;
        return (current.X + (target.X - current.X) * t,
                current.Y + (target.Y - current.Y) * t,
                current.Z + (target.Z - current.Z) * t);
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
