using System.Linq;

namespace Age.Engine.Model;

/// <summary>The sampled native one-shot channels carried to the compositor: op 0x21e scale, op 0x21f
/// axis-angle rotation, and op 0x220 translation. Z is retained through full 4x4 composition.</summary>
public readonly record struct TransformState(double ScaleX, double ScaleY, double ScaleZ,
                                             double TranslateX, double TranslateY, double TranslateZ,
                                             double AnchorX, double AnchorY, double AnchorZ,
                                             double RotationAxisX = 0, double RotationAxisY = 0,
                                             double RotationAxisZ = 0, double RotationAngleDegrees = 0);

public readonly record struct RotationCycleState(bool Enabled, long PeriodMs,
                                                 double AxisX, double AxisY, double AxisZ,
                                                 double AngleDegrees = 0);

/// <summary>Sampled op-0x223 type-0 surface transition. Range A is already present in normal z-order;
/// the compositor draws range B over it with <paramref name="Progress"/> to form the native crossfade.</summary>
public readonly record struct SurfaceTransitionState(long CommandKey, int TargetSlot,
    long RangeAStart, int RangeACount, long RangeBStart, int RangeBCount,
    long DelayMs, long DurationMs, long StartMs, double Progress, bool Forced);

/// <summary>One synchronized sample of op 0x202's native one-shot packed-color channel.</summary>
public readonly record struct ColorTransitionState(long Current, long Target,
    long DelayMs, long DurationMs, long StartMs, double Progress, bool Active);

/// <summary>A renderable view of one visible gfx object — the host composites these in ascending-handle order
/// (= the engine's z-order) each frame. Built by <see cref="GfxState.SnapshotVisibleObjects"/>; the surface
/// resId/colorkey are resolved from the object's live source slot at snapshot time (see docs/engine-re.md,
/// "The full gfx render model").</summary>
/// <summary>The packed-color channel is mode-dependent. Mode 0 uses <paramref name="TintStrength"/> to blend
/// <paramref name="Tint"/> into the texel; mode 1 uses <paramref name="Alpha"/> as opacity and multiplies the
/// texel by <paramref name="Tint"/>. <paramref name="MultiplyTint"/> selects the latter compositor path.</summary>
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY,
                                           TransformState Transform, RotationCycleState Rotation,
                                           int Alpha, long Tint, int TintStrength, BlendKind Blend,
                                           bool MultiplyTint,
                                           SurfaceTransitionState? SurfaceTransition = null,
                                           ColorTransitionState? ColorTransition = null);

/// <summary>Host-agnostic model of the AGE native gfx command-buffer (reversed in
/// docs/engine-re.md, gfx op-contract table). One registry maps an object handle to a GfxObject — the
/// native ctx+0x408 map that op 0x215 queries and the geometry get/set ops share. Each object carries a
/// slot (returned by 0x215) and three 3-vectors: V18 (set 0x217 / get 0x218, anchor), V24 (set 0x219 /
/// get 0x21a, position), V16c (set 0x1ff). The native DirectDraw workers are NOT modelled — only the data
/// the query ops read back, which is all the bytecode geometry math needs.</summary>
public sealed class GfxState
{
    private sealed class SurfaceTransition
    {
        public long CommandKey, RangeAStart, RangeBStart, DelayMs, DurationMs;
        public int TargetSlot, RangeACount, RangeBCount;
        public long StartMs = -1;
        public bool Forced;
    }
    public sealed class GfxObject
    {
        public (long X, long Y, long Z) V18, V24, V16c;
        public long Field64, Field68, Field6c;
        public long Color;
        public bool HasColor;   // true once op 0x202/0x203 set a color/alpha modulation on this object
        public long StaticColorMode; // op 0x203 operand 2 -> obj+0x30; mode 2 is transition alpha/identity
        // Op 0x202 one-shot packed-color channel: current +0x60, target +0x64, delay +0x38,
        // duration +0x4c. It shares obj+0x34's start timestamp with the one-shot matrix channels.
        public long OneShotColorTarget = -1, ColorDelayMs, ColorDurationMs;
        public bool OneShotColorEnabled;

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
        public (double X, double Y, double Z, double Angle) RotationCurrent;
        public (double X, double Y, double Z, double Angle) RotationTarget;
        public long RotationDelayMs, RotationDurationMs;
        public bool RotationChannelEnabled;
        // Shared matrix-channel start timestamp obj+0x34, seeded from frame-time ctx+0xb550.
        public long OneShotStartMs = -1;

        // Op 0x234 is a separate cyclic rotation channel (period obj+0x228, axis obj+0x244..0x24c).
        public long RotationPeriodMs;
        public (long X, long Y, long Z) RotationAxis;
        public bool RotationEnabled;
        public long RotationStartMs = -1;
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

    /// <summary>Op 0x21d: clone the native 0x2d4-byte retained-object record from source to destination.</summary>
    public bool CloneObject(long sourceHandle, long destinationHandle)
    {
        lock (_lock)
        {
            if (!_objects.TryGetValue(sourceHandle, out var s)) return false;
            _objects[destinationHandle] = new GfxObject
            {
                V18 = s.V18, V24 = s.V24, V16c = s.V16c,
                Field64 = s.Field64, Field68 = s.Field68, Field6c = s.Field6c,
                Color = s.Color, HasColor = s.HasColor, StaticColorMode = s.StaticColorMode,
                OneShotColorTarget = s.OneShotColorTarget, ColorDelayMs = s.ColorDelayMs,
                ColorDurationMs = s.ColorDurationMs, OneShotColorEnabled = s.OneShotColorEnabled,
                SrcGridW = s.SrcGridW, SrcGridH = s.SrcGridH, SrcCell = s.SrcCell,
                SrcPeriod = s.SrcPeriod, SrcStart = s.SrcStart, SrcAnim = s.SrcAnim,
                ColorPeriod = s.ColorPeriod, ColorStart = s.ColorStart, ColorTarget = s.ColorTarget,
                ColorAnim = s.ColorAnim, SourceSlot = s.SourceSlot, SrcRect = s.SrcRect, Visible = s.Visible,
                ScaleCurrent = s.ScaleCurrent, ScaleTarget = s.ScaleTarget,
                ScaleDelayMs = s.ScaleDelayMs, ScaleDurationMs = s.ScaleDurationMs, ScaleEnabled = s.ScaleEnabled,
                TranslationCurrent = s.TranslationCurrent, TranslationTarget = s.TranslationTarget,
                TranslationDelayMs = s.TranslationDelayMs, TranslationDurationMs = s.TranslationDurationMs,
                TranslationEnabled = s.TranslationEnabled,
                RotationCurrent = s.RotationCurrent, RotationTarget = s.RotationTarget,
                RotationDelayMs = s.RotationDelayMs, RotationDurationMs = s.RotationDurationMs,
                RotationChannelEnabled = s.RotationChannelEnabled, OneShotStartMs = s.OneShotStartMs,
                RotationPeriodMs = s.RotationPeriodMs, RotationAxis = s.RotationAxis,
                RotationEnabled = s.RotationEnabled, RotationStartMs = s.RotationStartMs,
            };
            CurrentObject = destinationHandle;
            return true;
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
    private readonly Dictionary<int, SurfaceTransition> _surfaceTransitions = new();
    public void SetSurface(int slot, long resId, long colorKey) { lock (_lock) { _surfaces[slot] = (resId, colorKey); } }

    /// <summary>Ops 0x202/0x203: record a packed 0xAARRGGBB color/alpha modulation on the object and mark it
    /// HasColor so the compositor applies alpha+tint (vs the opaque default).</summary>
    public void SetObjectColor(long handle, long packed)
    {
        lock (_lock) { var o = GetOrCreate(handle); o.Color = packed; o.HasColor = true; }
    }

    /// <summary>Resolve 0x202/0x203's native negative sentinels against obj+0x60 (the current static
    /// packed color): negative alpha preserves its alpha byte; negative RGB preserves its RGB bytes.</summary>
    public void SetObjectColorResolved(long handle, long alpha, long rgb)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            long current = o.HasColor ? o.Color : 0xffffffff;
            long resolvedAlpha = alpha < 0 ? (current >> 24) & 0xff : System.Math.Min(alpha, 0xff);
            long resolvedRgb = rgb < 0 ? current & 0xffffff : rgb & 0xffffff;
            o.Color = PackColor(resolvedAlpha, resolvedRgb);
            o.HasColor = true;
        }
    }

    /// <summary>Op 0x202: arm the delayed one-shot packed-color interpolation without replacing the
    /// current static color. Negative alpha/RGB operands resolve from current obj+0x60 independently.</summary>
    public void SetAnimatedObjectColorResolved(long handle, long delayMs, long durationMs, long alpha, long rgb)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            long current = o.HasColor ? o.Color : 0xffffffff;
            long resolvedAlpha = alpha < 0 ? (current >> 24) & 0xff : System.Math.Min(alpha, 0xff);
            long resolvedRgb = rgb < 0 ? current & 0xffffff : rgb & 0xffffff;
            if (!o.HasColor) { o.Color = current; o.HasColor = true; }
            o.OneShotColorTarget = PackColor(resolvedAlpha, resolvedRgb);
            o.ColorDelayMs = delayMs;
            o.ColorDurationMs = durationMs;
            o.OneShotColorEnabled = true;
            o.OneShotStartMs = -1;
        }
    }

    public void SetStaticObjectColorResolved(long handle, long mode, long alpha, long rgb)
    {
        SetObjectColorResolved(handle, alpha, rgb);
        lock (_lock) GetOrCreate(handle).StaticColorMode = mode;
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

    /// <summary>Op 0x223: queue a type-0 timed alpha transition into a target surface slot.</summary>
    public void QueueSurfaceAlphaTransition(long commandKey, int targetSlot,
        long rangeAStart, int rangeACount, long rangeBStart, int rangeBCount,
        long delayMs, long durationMs)
    {
        lock (_lock)
        {
            _surfaceTransitions[targetSlot] = new SurfaceTransition
            {
                CommandKey = commandKey, TargetSlot = targetSlot,
                RangeAStart = rangeAStart, RangeACount = System.Math.Max(0, rangeACount),
                RangeBStart = rangeBStart, RangeBCount = System.Math.Max(0, rangeBCount),
                DelayMs = System.Math.Max(0, delayMs), DurationMs = System.Math.Max(0, durationMs),
            };
        }
    }

    /// <summary>Start every pending foreground transition at the native present boundary.</summary>
    public int StartForegroundTransitions(long nowMs)
    {
        lock (_lock)
        {
            int started = 0;
            foreach (var t in _surfaceTransitions.Values)
                if (t.StartMs < 0) { t.StartMs = nowMs; started++; }
            return started;
        }
    }

    public bool HasActiveForegroundTransitions(long nowMs)
    {
        lock (_lock)
            return _surfaceTransitions.Values.Any(t => TransitionProgress(t, nowMs) < 1.0);
    }

    /// <summary>Click completion affects only type-0 foreground transitions, never ambient object channels.</summary>
    public int CompleteForegroundTransitions(long nowMs)
    {
        lock (_lock)
        {
            int completed = 0;
            foreach (var t in _surfaceTransitions.Values)
                if (!t.Forced && TransitionProgress(t, nowMs) < 1.0) { t.Forced = true; completed++; }
            return completed;
        }
    }

    public IReadOnlyList<SurfaceTransitionState> SnapshotForegroundTransitions(long nowMs)
    {
        lock (_lock)
            return _surfaceTransitions.Values.Select(t => SampleTransition(t, nowMs)).ToList();
    }

    private static double TransitionProgress(SurfaceTransition t, long nowMs)
    {
        if (t.Forced) return 1.0;
        if (t.StartMs < 0) return 0.0;
        long elapsed = nowMs - t.StartMs - t.DelayMs;
        if (elapsed <= 0) return 0.0;
        if (t.DurationMs <= 0) return 1.0;
        return System.Math.Clamp(elapsed / (double)t.DurationMs, 0.0, 1.0);
    }

    private static SurfaceTransitionState SampleTransition(SurfaceTransition t, long nowMs)
        => new(t.CommandKey, t.TargetSlot, t.RangeAStart, t.RangeACount, t.RangeBStart, t.RangeBCount,
               t.DelayMs, t.DurationMs, t.StartMs, TransitionProgress(t, nowMs), t.Forced);

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
            o.ScaleEnabled = durationMs > 0; o.OneShotStartMs = -1;
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
            o.TranslationEnabled = durationMs > 0; o.OneShotStartMs = -1;
        }
    }

    /// <summary>Op 0x21f: delayed one-shot axis-angle rotation target, sharing obj+0x34's start timestamp.</summary>
    public void SetRotationChannel(long handle, long delayMs, long durationMs,
                                   (long X, long Y, long Z) axis, long angleDegrees)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.RotationDelayMs = delayMs; o.RotationDurationMs = durationMs;
            o.RotationTarget = (axis.X, axis.Y, axis.Z, angleDegrees);
            o.RotationChannelEnabled = durationMs > 0; o.OneShotStartMs = -1;
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
            o.RotationStartMs = -1;
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

                // ---- packed color: mode 0 treats alpha as tint/fill strength; mode 1 treats it as opacity and
                // RGB as multiplicative source modulation. 0x232 ping-pongs the mode-0 strength/tint. ----
                int alpha = 255; long tint = 0xFFFFFF; int strength = 0; var blend = BlendKind.Opaque;
                bool multiplyTint = false;
                long sampledColor = o.Color;
                ColorTransitionState? colorTransition = null;
                if (o.OneShotColorEnabled)
                {
                    if (o.OneShotStartMs < 0) o.OneShotStartMs = nowMs;
                    (sampledColor, colorTransition) = SampleOneShotColor(o, nowMs);
                }
                if (o.HasColor)
                {
                    var (a, r, g, b) = BlendMath.UnpackArgb(sampledColor);
                    tint = ((long)r << 16) | ((long)g << 8) | (long)b;
                    if (o.StaticColorMode == 1)
                    {
                        // Native mode 1 enables SRCALPHA/INVSRCALPHA and passes packed ARGB as D3D
                        // modulation. Its high byte is opacity, not mode-0 tint/fill strength.
                        alpha = a; strength = 0; blend = BlendKind.Alpha; multiplyTint = true;
                    }
                    else if (o.StaticColorMode == 2)
                    {
                        // Native transition-source mode: 0xffffffff is opaque identity modulation,
                        // not a request to replace every texel with white.
                        alpha = a; strength = 0; blend = BlendKind.Alpha;
                    }
                    else
                    {
                        strength = a; blend = BlendKind.Alpha;
                    }
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
                if ((o.OneShotColorEnabled || o.ScaleEnabled || o.RotationChannelEnabled || o.TranslationEnabled)
                    && o.OneShotStartMs < 0)
                    o.OneShotStartMs = nowMs;
                var scale = SampleMatrixChannel(ref o.ScaleCurrent, o.ScaleTarget, o.ScaleDelayMs,
                                                o.ScaleDurationMs, o.OneShotStartMs, ref o.ScaleEnabled, nowMs);
                var rotation = SampleRotationChannel(ref o.RotationCurrent, o.RotationTarget,
                                                     o.RotationDelayMs, o.RotationDurationMs,
                                                     o.OneShotStartMs, ref o.RotationChannelEnabled, nowMs);
                var translation = SampleMatrixChannel(ref o.TranslationCurrent, o.TranslationTarget,
                                                      o.TranslationDelayMs, o.TranslationDurationMs,
                                                      o.OneShotStartMs, ref o.TranslationEnabled, nowMs);
                if (!o.OneShotColorEnabled && !o.ScaleEnabled && !o.RotationChannelEnabled && !o.TranslationEnabled)
                    o.OneShotStartMs = -1;

                double cycleAngle = 0;
                if (o.RotationEnabled && o.RotationPeriodMs > 0)
                {
                    if (o.RotationStartMs < 0) o.RotationStartMs = nowMs;
                    long elapsed = System.Math.Max(0, nowMs - o.RotationStartMs);
                    cycleAngle = ((elapsed % o.RotationPeriodMs) * 360) / o.RotationPeriodMs;
                }

                SurfaceTransitionState? transition = _surfaceTransitions.TryGetValue(o.SourceSlot, out var st)
                    ? SampleTransition(st, nowMs) : null;
                list.Add(new RenderObject(kv.Key, resId, ck, srcX, srcY, w, h,
                                          (int)o.V24.X, (int)o.V24.Y,
                                          new TransformState(scale.X, scale.Y, scale.Z,
                                                             translation.X, translation.Y, translation.Z,
                                                             o.V18.X, o.V18.Y, o.V18.Z,
                                                             rotation.X, rotation.Y, rotation.Z, rotation.Angle),
                                          new RotationCycleState(o.RotationEnabled, o.RotationPeriodMs,
                                                                 o.RotationAxis.X, o.RotationAxis.Y,
                                                                 o.RotationAxis.Z, cycleAngle),
                                          alpha, tint, strength, blend, multiplyTint, transition, colorTransition));
            }
            return list;
        }
    }

    private static (long Packed, ColorTransitionState State) SampleOneShotColor(GfxObject o, long nowMs)
    {
        long current = o.Color & 0xffffffff;
        long target = o.OneShotColorTarget & 0xffffffff;
        long start = o.OneShotStartMs;
        long elapsed = nowMs - start - o.ColorDelayMs;
        if (o.ColorDurationMs <= 0 || elapsed >= o.ColorDurationMs)
        {
            long delay = o.ColorDelayMs, duration = o.ColorDurationMs;
            o.Color = target;
            o.ColorDelayMs = 0;
            o.ColorDurationMs = 0;
            o.OneShotColorTarget = -1;
            o.OneShotColorEnabled = false;
            return (target, new ColorTransitionState(current, target, delay, duration, start, 1.0, false));
        }
        if (elapsed <= 0)
            return (current, new ColorTransitionState(current, target, o.ColorDelayMs,
                o.ColorDurationMs, start, 0.0, true));

        long packed = 0;
        long remaining = o.ColorDurationMs - elapsed;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            long c = (current >> shift) & 0xff;
            long t = (target >> shift) & 0xff;
            packed |= ((c * remaining + t * elapsed) / o.ColorDurationMs & 0xff) << shift;
        }
        return (packed, new ColorTransitionState(current, target, o.ColorDelayMs,
            o.ColorDurationMs, start, elapsed / (double)o.ColorDurationMs, true));
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

    private static (double X, double Y, double Z, double Angle) SampleRotationChannel(
        ref (double X, double Y, double Z, double Angle) current,
        (double X, double Y, double Z, double Angle) target,
        long delayMs, long durationMs, long startMs, ref bool enabled, long nowMs)
    {
        if (!enabled || durationMs <= 0 || startMs < 0) return current;
        long elapsed = nowMs - startMs - delayMs;
        if (elapsed <= 0) return current;
        if (elapsed >= durationMs) { current = target; enabled = false; return current; }
        double t = (double)elapsed / durationMs;
        return (current.X + (target.X - current.X) * t,
                current.Y + (target.Y - current.Y) * t,
                current.Z + (target.Z - current.Z) * t,
                current.Angle + (target.Angle - current.Angle) * t);
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
