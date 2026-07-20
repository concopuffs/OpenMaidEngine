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
/// <summary>The packed-color channel is mode-dependent. Textured mode 0 ignores packed alpha and
/// multiplicatively modulates by <paramref name="Tint"/>; surfaceless mode 0 uses <paramref name="TintStrength"/>.
/// A mode-0 color which has passed through op 0x202, and mode 1, use <paramref name="Alpha"/> as opacity.
/// <paramref name="MultiplyTint"/> selects the latter compositor path.</summary>
public readonly record struct RenderObject(long Handle, long SurfaceResId, long ColorKey,
                                           int SrcX, int SrcY, int W, int H, int DstX, int DstY,
                                           TransformState Transform, RotationCycleState Rotation,
                                           int Alpha, long Tint, int TintStrength, BlendKind Blend,
                                           bool MultiplyTint,
                                           SurfaceTransitionState? SurfaceTransition = null,
                                           ColorTransitionState? ColorTransition = null);

/// <summary>Host-agnostic model of the AGE native gfx command-buffer (reversed in
/// docs/engine-re.md, gfx op-contract table). One registry maps an object handle to a GfxObject — the
/// native retained-gfx owner+0x408 map (EngineCtx+0x46a1c) that op 0x215 queries and the geometry get/set ops share. Each object carries a
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
        public long Color = 0xffffffff; // native gfx_object_init_default obj+0x60: identity packed ARGB
        public bool HasColor;   // true once op 0x202/0x203 set a color/alpha modulation on this object
        public long StaticColorMode; // op 0x203 operand 2 -> obj+0x30; mode 2 is transition alpha/identity
        // Op 0x202 one-shot packed-color channel: current +0x60, target +0x64, delay +0x38,
        // duration +0x4c. It shares obj+0x34's start timestamp with the one-shot matrix channels.
        public long OneShotColorTarget = -1, ColorDelayMs, ColorDurationMs;
        public bool OneShotColorEnabled;
        // Once op 0x202 arms this object's color channel, its sampled/current ARGB is consumed as D3D
        // opacity + multiplicative modulation even after the target commits. This distinguishes ADV chrome
        // fades from a static mode-0 0x203 such as a CG initialized with 0x00ffffff (opaque identity).
        public bool OneShotColorBlend;

        // ---- src-rect / spritesheet-cell channel (ops 0x239 one-shot cell, 0x231 looping animation).
        // Native obj+0x238 is the TOTAL FRAME COUNT and +0x23c is the COLUMN COUNT. Each frame keeps
        // draw-texture's original source-rect size; those operands are not a grid to divide it by. ----
        public long SrcFrameCount = 1, SrcColumns = 1, SrcCell, SrcPeriod, SrcStart = -1;
        public bool SrcAnim;
        // ---- animated color/glow channel (op 0x232). Interpolator COLOR channel: period obj+0x220,
        // start obj+0x20c, target obj+0x240 — PING-PONG (distinct from static 0x202/0x203). ----
        public long ColorPeriod, ColorStart = -1, ColorTarget;
        public bool ColorAnim;
        // draw-texture bind (gfx_object_bind_draw): the surface to draw + its source rect + the visible flag.
        // Native gfx_object_init_default zeroes obj+4. Querying an object created by a geometry/animation
        // setter therefore returns slot 0 even before draw-texture binds it; only an absent object returns -1.
        public int SourceSlot;
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
        // Op 0x242 writes obj+0x2d0. Bit 0 detaches the finite one-shot group from the blocking-dirty
        // service and protects it from op 0x243's global force-completion request until natural completion.
        public long OneShotAnimationControlFlags;
        // Shared matrix-channel start timestamp obj+0x34, seeded from retained-gfx owner+0xb550
        // (EngineCtx+0x51b64).
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

    private readonly Dictionary<long, long> _fieldTable = new();   // ctx+0x46d14 (0x216); no family writer -> default 0
    public long CurrentObject { get; private set; }
    /// <summary>The D3D render target selected by op 0x20d. -1 denotes the main backbuffer.</summary>
    public int CurrentRenderTargetSlot { get; private set; } = -1;

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
                OneShotColorBlend = s.OneShotColorBlend,
                SrcFrameCount = s.SrcFrameCount, SrcColumns = s.SrcColumns, SrcCell = s.SrcCell,
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
                RotationChannelEnabled = s.RotationChannelEnabled,
                OneShotAnimationControlFlags = s.OneShotAnimationControlFlags,
                OneShotStartMs = s.OneShotStartMs,
                RotationPeriodMs = s.RotationPeriodMs, RotationAxis = s.RotationAxis,
                RotationEnabled = s.RotationEnabled, RotationStartMs = s.RotationStartMs,
            };
            CurrentObject = destinationHandle;
            return true;
        }
    }

    public GfxObject? TryGet(long handle) => _objects.TryGetValue(handle, out var o) ? o : null;

    /// <summary>Op 0x215: look up <paramref name="handle"/> in the retained gfx-object map and return obj+4,
    /// the live source-surface slot written by draw-texture, or -1 when absent/unbound.</summary>
    public int QuerySlot(long handle)
    {
        lock (_lock)
            return _objects.TryGetValue(handle, out var o) ? o.SourceSlot : -1;
    }

    /// <summary>Rebind one retained object from one source surface to another. Used by SC0000's bounded
    /// movie site to reproduce the native warm-engine slot assignment before the following static loaders
    /// reuse the port's cold-bootstrap slot.</summary>
    public void RemapObjectSurface(long handle, int fromSlot, int toSlot)
    {
        lock (_lock)
            if (_objects.TryGetValue(handle, out var obj) && obj.SourceSlot == fromSlot)
                obj.SourceSlot = toSlot;
    }
    public long QueryField(long idx) => _fieldTable.TryGetValue(idx, out var v) ? v : 0;

    public void Release(long handle)
    {
        lock (_lock)   // re-entrant: EraseRange already holds _lock
        {
            _objects.Remove(handle);
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

    /// <summary>Op 0x1f6: clear every retained gfx-object record without releasing surface resources.</summary>
    public void ClearRetainedObjects()
    {
        lock (_lock)
        {
            _objects.Clear();
            CurrentObject = 0;
        }
    }

    private readonly object _lock = new();

    // ---- surfaces (image buffers per slot): ctx+0x52bd4[slot], from create/set-texture ----
    private readonly Dictionary<int, (long ResId, long ColorKey)> _surfaces = new();
    private readonly Dictionary<int, SurfaceTransition> _surfaceTransitions = new();
    public void SetSurface(int slot, long resId, long colorKey) { lock (_lock) { _surfaces[slot] = (resId, colorKey); } }

    /// <summary>Op 0x20d: select a surface as the D3D render target; values at or above 1000 restore the
    /// device backbuffer in the native engine.</summary>
    public void SelectRenderTarget(long slot)
    {
        lock (_lock) CurrentRenderTargetSlot = slot is >= 0 and < 1000 ? (int)slot : -1;
    }

    /// <summary>Op 0x23d: release the transient surface range while retaining system-owned low slots.</summary>
    public void ReleaseSurfaceRange(int firstSlot, int count)
    {
        lock (_lock)
        {
            int end = checked(firstSlot + count);
            for (int slot = firstSlot; slot < end; slot++)
            {
                _surfaces.Remove(slot);
                _surfaceTransitions.Remove(slot);
            }
            if (CurrentRenderTargetSlot >= firstSlot && CurrentRenderTargetSlot < end)
                CurrentRenderTargetSlot = -1;
        }
    }

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
            o.OneShotColorBlend = true;
            o.OneShotStartMs = -1;
        }
    }

    public void SetStaticObjectColorResolved(long handle, long mode, long alpha, long rgb)
    {
        SetObjectColorResolved(handle, alpha, rgb);
        lock (_lock) GetOrCreate(handle).StaticColorMode = mode;
    }

    /// <summary>Ops 0x239 (one-shot cell endpoint, period=0 in this retained model) / 0x231 (looping):
    /// set total frame count, column count, and visible cell. Every frame retains draw-texture's source
    /// rectangle dimensions. For 0x231, <paramref name="period"/> is milliseconds per frame.</summary>
    public void SetSrcRect(long handle, long frameCount, long columns, long cell, long period)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.SrcFrameCount = frameCount < 1 ? 1 : frameCount;
            o.SrcColumns = columns < 1 ? 1 : columns;
            o.SrcCell = cell; o.SrcPeriod = period; o.SrcStart = -1; o.SrcAnim = true;
        }
    }

    /// <summary>Op 0x232 worker contract: ping-pong a temporary copy of static packed ARGB toward
    /// <paramref name="target"/>. The sampled value is consumed by the object's unchanged blend mode.</summary>
    public void SetColorAnim(long handle, long period, long target)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.ColorPeriod = period; o.ColorTarget = target; o.ColorStart = -1; o.ColorAnim = true;
        }
    }

    /// <summary>Op 0x232 handler contract: resolve negative alpha/RGB sentinels from static obj+0x60,
    /// clamp alpha above 255, then arm the cyclic packed-color channel without changing blend mode.</summary>
    public void SetColorAnimResolved(long handle, long period, long alpha, long rgb)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            long current = o.Color & 0xffffffff;
            long resolvedAlpha = alpha < 0 ? (current >> 24) & 0xff : System.Math.Min(alpha, 0xff);
            long resolvedRgb = rgb < 0 ? current & 0xffffff : rgb & 0xffffff;
            o.ColorPeriod = period;
            o.ColorTarget = PackColor(resolvedAlpha, resolvedRgb);
            o.ColorStart = -1;
            o.ColorAnim = true;
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

    /// <summary>Native op 0x21c keeps presenting until both queued surface commands and finite one-shot
    /// object channels have completed. Ambient cyclic/spritesheet/color pulses are deliberately excluded.</summary>
    public bool HasActiveTimedPresentation(long nowMs)
    {
        lock (_lock)
            return _surfaceTransitions.Values.Any(t => TransitionProgress(t, nowMs) < 1.0) ||
                   _objects.Values.Any(o => o.Visible &&
                                            (o.OneShotAnimationControlFlags & 1) == 0 &&
                                            (o.OneShotColorEnabled || o.ScaleEnabled ||
                                             o.RotationChannelEnabled || o.TranslationEnabled));
    }

    /// <summary>Op 0x242: replace the retained object's animation-control word. Native bit 0 makes its
    /// finite one-shot channels nonblocking and immune to op 0x243 forced completion.</summary>
    public void SetOneShotAnimationControl(long handle, long flags)
    {
        lock (_lock) GetOrCreate(handle).OneShotAnimationControlFlags = flags;
    }

    /// <summary>Part of op 0x243: immediately commit every unprotected finite one-shot channel. Objects
    /// marked by op 0x242 bit 0 keep sampling asynchronously.</summary>
    private void ForceCompleteOneShotChannels()
    {
        foreach (var o in _objects.Values)
        {
            if ((o.OneShotAnimationControlFlags & 1) != 0) continue;
            if (o.OneShotColorEnabled)
            {
                o.Color = o.OneShotColorTarget & 0xffffffff;
                o.OneShotColorTarget = -1;
                o.ColorDelayMs = 0;
                o.ColorDurationMs = 0;
                o.OneShotColorEnabled = false;
            }
            if (o.ScaleEnabled)
            {
                o.ScaleCurrent = o.ScaleTarget;
                o.ScaleDelayMs = 0;
                o.ScaleDurationMs = 0;
                o.ScaleEnabled = false;
            }
            if (o.RotationChannelEnabled)
            {
                o.RotationCurrent = o.RotationTarget;
                o.RotationDelayMs = 0;
                o.RotationDurationMs = 0;
                o.RotationChannelEnabled = false;
            }
            if (o.TranslationEnabled)
            {
                o.TranslationCurrent = o.TranslationTarget;
                o.TranslationDelayMs = 0;
                o.TranslationDurationMs = 0;
                o.TranslationEnabled = false;
            }
            o.OneShotStartMs = -1;
        }
    }

    /// <summary>Whether sampling the retained scene at a later frame can change its pixels without another
    /// VM mutation. Includes finite presentation work plus the ambient channels that may remain active while
    /// the interpreter is parked at an input wait. Static waits themselves are deliberately not animation.</summary>
    public bool HasActiveVisualPresentation(long nowMs)
    {
        lock (_lock)
            return _surfaceTransitions.Values.Any(t => TransitionProgress(t, nowMs) < 1.0) ||
                   _objects.Values.Any(o => o.Visible &&
                       (o.OneShotColorEnabled || o.ScaleEnabled || o.RotationChannelEnabled ||
                        o.TranslationEnabled ||
                        (o.SrcAnim && o.SrcPeriod > 0) ||
                        (o.ColorAnim && o.ColorPeriod > 0) ||
                        (o.RotationEnabled && o.RotationPeriodMs > 0)));
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

    /// <summary>Op 0x1fd: immediately replace the object's current scale matrix. Script operands are
    /// integer percentages; native divides them by 100 before matrix4_make_scale at obj+0x6c.</summary>
    public void SetCurrentScale(long handle, (long X, long Y, long Z) percent)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.ScaleCurrent = (percent.X / 100.0, percent.Y / 100.0, percent.Z / 100.0);
        }
    }

    /// <summary>Op 0x1ff: immediately replace the object's current translation matrix at obj+0x16c.</summary>
    public void SetCurrentTranslation(long handle, (long X, long Y, long Z) translation)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.V16c = translation;
            o.TranslationCurrent = translation;
        }
    }

    /// <summary>Op 0x21e: normalized scale target (100 = identity), with independent delay/duration.</summary>
    public void SetScaleChannel(long handle, long delayMs, long durationMs, (long X, long Y, long Z) percent)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            // Native setters get-or-create first, then require object flag bit 0 (draw-bound/visible).
            // Pre-bind calls leave a neutral, queryable placeholder and do not queue latent motion.
            if (!o.Visible) return;
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
            if (!o.Visible) return;
            o.TranslationDelayMs = delayMs; o.TranslationDurationMs = durationMs;
            o.TranslationTarget = target;
            o.TranslationEnabled = durationMs > 0; o.OneShotStartMs = -1;
        }
    }

    /// <summary>Op 0x228: query the translation target decomposed from the native target matrix at
    /// obj+0x17c (translation obj+0x1ac/+0x1b0/+0x1b4). This is independent of draw/base position V24.</summary>
    public bool TryQueryTranslationTarget(long handle, out (double X, double Y, double Z) target)
    {
        lock (_lock)
        {
            if (_objects.TryGetValue(handle, out var o))
            {
                target = o.TranslationTarget;
                return true;
            }
            target = default;
            return false;
        }
    }

    /// <summary>Op 0x21f: delayed one-shot axis-angle rotation target, sharing obj+0x34's start timestamp.</summary>
    public void SetRotationChannel(long handle, long delayMs, long durationMs,
                                   (long X, long Y, long Z) axis, long angleDegrees)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            if (!o.Visible) return;
            o.RotationDelayMs = delayMs; o.RotationDurationMs = durationMs;
            o.RotationTarget = (axis.X, axis.Y, axis.Z, angleDegrees);
            o.RotationChannelEnabled = durationMs > 0; o.OneShotStartMs = -1;
        }
    }

    /// <summary>Op 0x234: retain the cyclic rotation period and axis separately. Native interpolation uses
    /// frame-time retained-gfx owner+0xb550 (EngineCtx+0x51b64) and rotates through 360 degrees per period.</summary>
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

    /// <summary>Op 0x243: force ordinary finite channels to their endpoints and reset the separate
    /// global animation-service clock. Op-0x242-detached objects ignore the completion request.</summary>
    public void ResetAnimClock()
    {
        lock (_lock)
        {
            ForceCompleteOneShotChannels();
            AnimClockDurationTicks = 0;
            AnimClockGeneration++;
        }
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
                bool hadOneShot = o.OneShotColorEnabled || o.ScaleEnabled ||
                                  o.RotationChannelEnabled || o.TranslationEnabled;
                var (resId, ck) = _surfaces.TryGetValue(o.SourceSlot, out var s) ? s : (0L, 0L);

                // ---- packed color: a static mode 0 treats alpha as tint/fill strength. Once op 0x202 has
                // armed the one-shot channel, its current/target ARGB instead supplies opacity and D3D-style
                // multiplicative RGB modulation (including after target commit). Mode 1 uses the same blend.
                // Op 0x232 modifies this temporary packed color before the unchanged mode consumes it. ----
                int alpha = 255; long tint = 0xFFFFFF; int strength = 0; var blend = BlendKind.Opaque;
                bool multiplyTint = false;
                long sampledColor = o.HasColor ? o.Color : 0xffffffff;
                ColorTransitionState? colorTransition = null;
                if (o.OneShotColorEnabled)
                {
                    if (o.OneShotStartMs < 0) o.OneShotStartMs = nowMs;
                    (sampledColor, colorTransition) = SampleOneShotColor(o, nowMs);
                }
                if (o.ColorAnim)
                {
                    if (o.ColorStart < 0) o.ColorStart = nowMs;
                    sampledColor = InterpolatePackedColor(sampledColor, o.ColorTarget,
                        PingPongWeight(nowMs, o.ColorStart, o.ColorPeriod));
                }
                if (o.HasColor || o.ColorAnim)
                {
                    var (a, r, g, b) = BlendMath.UnpackArgb(sampledColor);
                    tint = ((long)r << 16) | ((long)g << 8) | (long)b;
                    if (o.StaticColorMode == 1)
                    {
                        // Native mode 1 enables SRCALPHA/ONE additive blending and passes packed ARGB as
                        // D3D modulation. Black therefore contributes nothing (TITLE's SO022 flames),
                        // while the high byte scales the additive source contribution.
                        alpha = a; strength = 0; blend = BlendKind.Additive; multiplyTint = true;
                    }
                    else if (o.StaticColorMode == 0 && o.OneShotColorBlend)
                    {
                        alpha = a; strength = 0; blend = BlendKind.Alpha; multiplyTint = true;
                    }
                    else if (o.StaticColorMode == 2)
                    {
                        // Native transition-source mode: 0xffffffff is opaque identity modulation,
                        // not a request to replace every texel with white.
                        alpha = a; strength = 0; blend = BlendKind.Alpha;
                    }
                    else if (resId != 0)
                    {
                        // Native mode 0 is the opaque textured path. RGB modulates the source and the
                        // packed alpha byte does not become tint strength; in particular 0xffffffff is
                        // identity after the foreground-transition cleanup write.
                        alpha = 255; strength = 0; blend = BlendKind.Alpha; multiplyTint = true;
                    }
                    else
                    {
                        // A surfaceless mode-0 object is a solid fill, whose high byte remains fill strength.
                        strength = a; blend = BlendKind.Alpha;
                    }
                }
                // ---- src-rect: preserve cell dimensions and offset it row-major through the sheet ----
                int srcX = o.SrcRect.X, srcY = o.SrcRect.Y, w = o.SrcRect.W, h = o.SrcRect.H;
                if (o.SrcAnim && o.SrcFrameCount >= 1)
                {
                    long cell = o.SrcCell;
                    if (o.SrcPeriod > 0)
                    {
                        if (o.SrcStart < 0) o.SrcStart = nowMs;
                        cell = ((nowMs - o.SrcStart) / o.SrcPeriod) % o.SrcFrameCount;
                    }
                    srcX = o.SrcRect.X + (int)(cell % o.SrcColumns) * o.SrcRect.W;
                    srcY = o.SrcRect.Y + (int)(cell / o.SrcColumns) * o.SrcRect.H;
                }

                // One-shot matrix channels: hold current through delay, then linearly sample current -> target.
                if (hadOneShot
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
                {
                    if (hadOneShot) o.OneShotAnimationControlFlags &= ~1L;
                    o.OneShotStartMs = -1;
                }

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

    private static long InterpolatePackedColor(long current, long target, double t)
    {
        t = System.Math.Clamp(t, 0.0, 1.0);
        long packed = 0;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            long c = (current >> shift) & 0xff;
            long v = (target >> shift) & 0xff;
            packed |= ((long)(c + (v - c) * t) & 0xff) << shift;
        }
        return packed;
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
