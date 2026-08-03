using System.Linq;
using Age.Engine.Hosting;

namespace Age.Engine.Model;


/// <summary>Host-agnostic model of the AGE native gfx command-buffer (reversed in
/// docs/engine-re.md, gfx op-contract table). One registry maps an object handle to a GfxObject — the
/// native retained-gfx owner+0x408 map (EngineCtx+0x46a1c) that op 0x215 queries and the geometry get/set ops share. Each object carries a
/// slot (returned by 0x215) and three 3-vectors: V18 (set 0x217 / get 0x218, anchor), V24 (set 0x219 /
/// get 0x21a, position), V16c (set 0x1ff). The native DirectDraw workers are NOT modelled — only the data
/// the query ops read back, which is all the bytecode geometry math needs.</summary>
public sealed partial class GfxState
{
    private sealed class SurfaceTransition
    {
        public long CommandKey, RangeAStart, RangeBStart, DelayMs, DurationMs;
        public int TargetSlot, RangeACount, RangeBCount;
        public long StartMs = -1;
        public bool Forced;
    }
    private sealed class MovieMaskTransition
    {
        public required MovieMaskTransitionRequest Request;
        public bool Completed;
    }


    // ---- Separate global animation service clock (op 0x238; ctx+0x51b7c total / +0x51b78 elapsed).
    // Retained for its opcode family; 0x21e scale and 0x220 translation use frame-time directly instead. ----
    public long AnimClockDurationTicks { get; private set; }
    public long AnimClockGeneration { get; private set; }
    public long AnimationServiceFlags { get; private set; }
    public uint PreviousFrameTimeMilliseconds { get; private set; }
    public uint CurrentFrameTimeMilliseconds { get; private set; }
    private long _previousFrameTimeMs;
    private long _currentFrameTimeMs;
    private long _mutationGeneration;
    private long _publishedMutationGeneration;

    private void MarkRetainedMutation() => _mutationGeneration++;


    /// <summary>Native scene_context_init_reset ownership boundary used by opcode 0x9: discard
    /// retained objects, command/query state, surfaces, transitions, render-target selection, and the
    /// scene animation clock while leaving VM globals and decoded host assets outside this model.</summary>
    public void ResetSceneContext()
    {
        lock (_lock)
        {
            _objects.Clear();
            _orderedObjectHandles.Clear();
            _fieldTable.Clear();
            _surfaces.Clear();
            _createdSurfaces.Clear();
            _reloadableSurfaces.Clear();
            _movieStopTimesMs.Clear();
            _surfaceTransitions.Clear();
            _movieMaskTransitions.Clear();
            CurrentObject = 0;
            CurrentRenderTargetSlot = -1;
            _rangeTransformFirst = 0;
            _rangeTransformCount = 0;
            _rangeTransform = new GfxObject();
            AnimClockDurationTicks = 0;
            AnimClockGeneration++;
            AnimationServiceFlags = 0;
            PreviousFrameTimeMilliseconds = 0;
            CurrentFrameTimeMilliseconds = 0;
            _previousFrameTimeMs = 0;
            _currentFrameTimeMs = 0;
            MarkRetainedMutation();
        }
    }

    public GfxPersistenceSnapshot CapturePersistenceSnapshot()
    {
        lock (_lock)
        {
            var surfaces = _surfaces.Keys
                .Concat(_reloadableSurfaces)
                .Distinct()
                .Select(slot =>
                {
                    bool active = _surfaces.TryGetValue(slot, out var value);
                    return new GfxSurfacePersistenceState(
                        slot, active ? value.ResId : -1, active ? value.ColorKey : 0,
                        active && _createdSurfaces.Contains(slot),
                        _reloadableSurfaces.Contains(slot));
                })
                .OrderBy(item => item.Slot)
                .ToArray();
            var objects = _orderedObjectHandles
                .Select(handle => (handle, CloneState(_objects[handle])))
                .ToArray();
            return new GfxPersistenceSnapshot(
                surfaces, objects, _rangeTransformFirst, _rangeTransformCount,
                CloneState(_rangeTransform));
        }
    }

    public void RestorePersistenceSnapshot(GfxPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            _surfaces.Clear();
            _createdSurfaces.Clear();
            _reloadableSurfaces.Clear();
            foreach (GfxSurfacePersistenceState surface in snapshot.Surfaces)
            {
                if (surface.ResourceId >= 0 || surface.Created)
                    _surfaces[surface.Slot] = (surface.ResourceId, surface.ColorKey);
                if (surface.Created) _createdSurfaces.Add(surface.Slot);
                if (surface.ReloadOnRestore) _reloadableSurfaces.Add(surface.Slot);
            }
            _objects.Clear();
            _orderedObjectHandles.Clear();
            foreach (var (handle, state) in snapshot.Objects)
            {
                _objects[handle] = CloneState(state);
                _orderedObjectHandles.Add(handle);
            }
            _orderedObjectHandles.Sort();
            _rangeTransformFirst = snapshot.RangeFirst;
            _rangeTransformCount = snapshot.RangeCount;
            _rangeTransform = CloneState(snapshot.RangeTransform);
            MarkRetainedMutation();
        }
    }


    private readonly object _lock = new();

    private readonly Dictionary<int, SurfaceTransition> _surfaceTransitions = new();
    private readonly Dictionary<int, MovieMaskTransition> _movieMaskTransitions = new();

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
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.StaticColorMode = mode;
            o.PostBindStaticColorBlend = mode == 0 && o.Visible;
        }
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
            MarkRetainedMutation();
        }
    }

    public void QueueMovieMaskTransition(MovieMaskTransitionRequest request)
    {
        lock (_lock)
        {
            _movieMaskTransitions[request.SurfaceSlot] = new MovieMaskTransition
            {
                Request = request with
                {
                    SourceRangeCount = System.Math.Max(0, request.SourceRangeCount),
                    Width = System.Math.Max(0, request.Width),
                    Height = System.Math.Max(0, request.Height),
                    StartDelayMs = System.Math.Max(0, request.StartDelayMs),
                    DurationMs = System.Math.Max(0, request.DurationMs),
                },
            };
            MarkRetainedMutation();
        }
    }

    public bool CompleteMovieMaskTransition(int surfaceSlot)
    {
        lock (_lock)
        {
            if (!_movieMaskTransitions.TryGetValue(surfaceSlot, out var transition)
                || transition.Completed)
                return false;
            transition.Completed = true;
            MarkRetainedMutation();
            return true;
        }
    }

    public IReadOnlyList<MovieMaskTransitionState> SnapshotMovieMaskTransitions()
    {
        lock (_lock)
            return _movieMaskTransitions.Values
                .Select(t => new MovieMaskTransitionState(t.Request, t.Completed)).ToList();
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
            return _surfaceTransitions.Values.Any(t => TransitionProgress(t, nowMs) < 1.0)
                   || _movieMaskTransitions.Values.Any(t => !t.Completed);
    }

    /// <summary>Native op 0x21c keeps presenting until both queued surface commands and finite one-shot
    /// object channels have completed. Ambient cyclic/spritesheet/color pulses are deliberately excluded.</summary>
    public bool HasActiveTimedPresentation(long nowMs)
    {
        lock (_lock)
            return _surfaceTransitions.Values.Any(t => TransitionProgress(t, nowMs) < 1.0) ||
                   _movieMaskTransitions.Values.Any(t => !t.Completed) ||
                   _rangeTransform.ScaleEnabled || _rangeTransform.RotationChannelEnabled ||
                   _rangeTransform.TranslationEnabled ||
                   _objects.Values.Any(o => o.Visible &&
                                            (o.OneShotAnimationControlFlags & 1) == 0 &&
                                            (o.OneShotColorEnabled || o.ScaleEnabled ||
                                             o.RotationChannelEnabled || o.TranslationEnabled));
    }

    /// <summary>Observe-only state for a runtime stall capture. It identifies the exact finite channels
    /// which can keep the host's op-0x21c presentation wait active.</summary>
    public GfxDiagnosticSnapshot CaptureDiagnosticSnapshot(long nowMs)
    {
        lock (_lock)
        {
            BlockingGfxObjectDiagnostic? range = HasBlockingChannels(_rangeTransform)
                ? DescribeBlockingObject(-1, _rangeTransform)
                : null;
            var objects = _objects
                .Where(pair => pair.Value.Visible
                    && (pair.Value.OneShotAnimationControlFlags & 1) == 0
                    && HasBlockingChannels(pair.Value))
                .OrderBy(pair => pair.Key)
                .Select(pair => DescribeBlockingObject(pair.Key, pair.Value))
                .ToArray();
            return new GfxDiagnosticSnapshot(
                nowMs,
                _surfaceTransitions.Values.Any(t => TransitionProgress(t, nowMs) < 1.0)
                    || _movieMaskTransitions.Values.Any(t => !t.Completed)
                    || range != null || objects.Length != 0,
                _objects.Count,
                _objects.Values.Count(o => o.Visible),
                _surfaceTransitions.Values.Count(t => TransitionProgress(t, nowMs) < 1.0)
                    + _movieMaskTransitions.Values.Count(t => !t.Completed),
                AnimationServiceFlags,
                AnimClockDurationTicks,
                AnimClockGeneration,
                PreviousFrameTimeMilliseconds,
                CurrentFrameTimeMilliseconds,
                _rangeTransformFirst,
                _rangeTransformCount,
                range,
                objects);
        }
    }

    private static bool HasBlockingChannels(GfxObject o)
        => o.OneShotColorEnabled || o.ScaleEnabled || o.RotationChannelEnabled || o.TranslationEnabled;

    private static BlockingGfxObjectDiagnostic DescribeBlockingObject(long handle, GfxObject o)
        => new(handle, o.SourceSlot, o.OneShotStartMs, o.OneShotAnimationControlFlags,
            o.OneShotColorEnabled, o.ColorDelayMs, o.ColorDurationMs,
            o.ScaleEnabled, o.ScaleDelayMs, o.ScaleDurationMs,
            o.RotationChannelEnabled, o.RotationDelayMs, o.RotationDurationMs,
            o.TranslationEnabled, o.TranslationDelayMs, o.TranslationDurationMs);

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
        CommitOneShotChannels(_rangeTransform);
        foreach (var o in _objects.Values)
        {
            if ((o.OneShotAnimationControlFlags & 1) != 0) continue;
            CommitOneShotChannels(o);
        }
    }

    private static void CommitOneShotChannels(GfxObject o)
    {
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

    /// <summary>Whether sampling the retained scene at a later frame can change its pixels without another
    /// VM mutation. Includes finite presentation work plus the ambient channels that may remain active while
    /// the interpreter is parked at an input wait. Static waits themselves are deliberately not animation.</summary>
    public bool HasActiveVisualPresentation(long nowMs)
    {
        lock (_lock)
            return _surfaceTransitions.Values.Any(t => TransitionProgress(t, nowMs) < 1.0) ||
                   _movieMaskTransitions.Values.Any(t => !t.Completed) ||
                   _rangeTransform.ScaleEnabled || _rangeTransform.RotationChannelEnabled ||
                   _rangeTransform.TranslationEnabled ||
                   _objects.Values.Any(o => o.Visible &&
                       (o.OneShotColorEnabled || o.ScaleEnabled || o.RotationChannelEnabled ||
                        o.TranslationEnabled ||
                        (o.SrcAnim && o.SrcPeriod > 0) ||
                        (o.ColorAnim && o.ColorPeriod > 0) ||
                        (o.ScaleCycleEnabled && o.ScaleCyclePeriodMs > 0) ||
                        (o.RotationEnabled && o.RotationPeriodMs > 0)));
    }

    /// <summary>Consume the native run-state-0x400 EffectSkipOnClick action and, when permitted, force its
    /// finite retained presentation to the endpoint. Bit 0 of op 0x24e rejects the action entirely. Bit 1
    /// still allows the service wait to end but suppresses the shared force-complete request. Movie masks,
    /// ambient cycles, and op-0x242-detached channels continue asynchronously after the service resumes.</summary>
    public bool TryCompleteClickSkippableTimedPresentation(long nowMs, out int completed)
    {
        lock (_lock)
        {
            completed = 0;
            if ((AnimationServiceFlags & 1) != 0) return false;

            if ((AnimationServiceFlags & 2) == 0)
            {
                foreach (var t in _surfaceTransitions.Values)
                {
                    if (t.Forced || TransitionProgress(t, nowMs) >= 1.0) continue;
                    t.Forced = true;
                    completed++;
                }

                completed += CountOneShotChannels(_rangeTransform);
                foreach (var o in _objects.Values)
                {
                    if ((o.OneShotAnimationControlFlags & 1) != 0) continue;
                    completed += CountOneShotChannels(o);
                }
                ForceCompleteOneShotChannels();

                if (AnimClockDurationTicks != 0) completed++;
                AnimClockDurationTicks = 0;
                AnimClockGeneration++;
            }

            if (completed > 0) MarkRetainedMutation();
            return true;
        }
    }

    private static int CountOneShotChannels(GfxObject o)
        => (o.OneShotColorEnabled ? 1 : 0)
           + (o.ScaleEnabled ? 1 : 0)
           + (o.RotationChannelEnabled ? 1 : 0)
           + (o.TranslationEnabled ? 1 : 0);

    /// <summary>Force only queued type-0 foreground transitions. PresentFrame uses this to publish a
    /// command endpoint immediately; interactive run-state-0x400 skipping uses the broader method above.</summary>
    public int CompleteForegroundTransitions(long nowMs)
    {
        lock (_lock)
        {
            int completed = 0;
            foreach (var t in _surfaceTransitions.Values)
                if (!t.Forced && TransitionProgress(t, nowMs) < 1.0) { t.Forced = true; completed++; }
            if (completed > 0) MarkRetainedMutation();
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

    /// <summary>Op 0x1fe: immediately replace the object's current axis-angle rotation matrix at
    /// obj+0xec. Axis components and angle are retained as native floating-point values.</summary>
    public void SetCurrentRotation(long handle, (long X, long Y, long Z) axis, long angleDegrees)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.RotationCurrent = (axis.X, axis.Y, axis.Z, angleDegrees);
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

    /// <summary>Op 0x233: cyclic identity-to-target scale, sampled with a triangular ping-pong phase.</summary>
    public void SetScaleCycle(long handle, long periodMs, (long X, long Y, long Z) percent)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.ScaleCyclePeriodMs = periodMs;
            o.ScaleCycleTarget = (percent.X / 100.0, percent.Y / 100.0, percent.Z / 100.0);
            o.ScaleCycleEnabled = periodMs > 0;
            o.ScaleCycleStartMs = -1;
        }
    }

    /// <summary>Op 0x230: stop every cyclic object channel without changing its base/current state,
    /// one-shot channels, or retained cyclic targets. Native clears flag bit 2 and the five start/period
    /// pairs at obj+0x20c..0x230, including one secondary matrix channel not otherwise modeled here.</summary>
    public void ResetCyclicAnimationChannels(long handle)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.ColorStart = -1;
            o.ScaleCycleStartMs = -1;
            o.RotationStartMs = -1;
            o.SrcStart = -1;
            o.ColorPeriod = 0;
            o.ScaleCyclePeriodMs = 0;
            o.RotationPeriodMs = 0;
            o.SrcPeriod = 0;
            o.ColorAnim = false;
            o.ScaleCycleEnabled = false;
            o.RotationEnabled = false;
            o.SrcAnim = false;

            // Preserve drop-in numbered-save compatibility for the unmodeled secondary cyclic matrix
            // as well as the modeled channels. The ten native timing dwords form one contiguous range.
            if (o.NativePersistenceRecord is { Length: >= 0x234 } raw)
            {
                raw[0] &= 0xfb;
                System.Array.Clear(raw, 0x20c, 0x28);
            }
        }
    }

    /// <summary>Op 0x238: set its separate global animation-service duration and reset marker.</summary>
    public void SetAnimClock(long durationTicks)
    {
        lock (_lock)
        {
            AnimClockDurationTicks = durationTicks;
            AnimClockGeneration++;
            MarkRetainedMutation();
        }
    }

    public void SetAnimationServiceFlags(long flags)
    {
        lock (_lock) AnimationServiceFlags = unchecked((uint)flags);
    }

    public void SampleFrameTime(long nowMilliseconds)
    {
        lock (_lock)
        {
            _previousFrameTimeMs = _currentFrameTimeMs;
            _currentFrameTimeMs = nowMilliseconds;
            PreviousFrameTimeMilliseconds = CurrentFrameTimeMilliseconds;
            CurrentFrameTimeMilliseconds = unchecked((uint)nowMilliseconds);
        }
    }

    /// <summary>Op 0x243: force ordinary finite channels to their endpoints and reset the separate
    /// global animation-service clock. Op-0x242-detached objects ignore the completion request.</summary>
    public void ResetAnimClock()
    {
        lock (_lock)
        {
            if ((AnimationServiceFlags & 2) != 0) return;
            ForceCompleteOneShotChannels();
            AnimClockDurationTicks = 0;
            AnimClockGeneration++;
            MarkRetainedMutation();
        }
    }

    /// <summary>Sample the shared native frame clock and report why the retained scene needs publishing.
    /// Continuous channels remain frame-driven; op-0x231 spritesheets become dirty only when the shared
    /// previous/current samples select different cells. Retained VM writes are published exactly once.</summary>
    public GfxPresentationReason ConsumePresentationReasons(long nowMs)
    {
        lock (_lock)
        {
            _previousFrameTimeMs = _currentFrameTimeMs;
            _currentFrameTimeMs = nowMs;
            PreviousFrameTimeMilliseconds = unchecked((uint)_previousFrameTimeMs);
            CurrentFrameTimeMilliseconds = unchecked((uint)_currentFrameTimeMs);

            GfxPresentationReason reasons = GfxPresentationReason.None;
            if (_publishedMutationGeneration != _mutationGeneration)
            {
                _publishedMutationGeneration = _mutationGeneration;
                reasons |= GfxPresentationReason.RetainedMutation;
            }

            if (_surfaceTransitions.Values.Any(t => TransitionProgress(t, nowMs) < 1.0) ||
                _rangeTransform.ScaleEnabled || _rangeTransform.RotationChannelEnabled ||
                _rangeTransform.TranslationEnabled ||
                _objects.Values.Any(o => o.Visible &&
                    (o.OneShotColorEnabled || o.ScaleEnabled || o.RotationChannelEnabled ||
                     o.TranslationEnabled ||
                     (o.ColorAnim && o.ColorPeriod > 0) ||
                     (o.ScaleCycleEnabled && o.ScaleCyclePeriodMs > 0) ||
                     (o.RotationEnabled && o.RotationPeriodMs > 0))))
                reasons |= GfxPresentationReason.ContinuousChannel;

            foreach (var o in _objects.Values)
            {
                if (!o.Visible || !o.SrcAnim || o.SrcPeriod <= 0 || o.SrcFrameCount < 1) continue;
                if (o.SrcStart < 0)
                {
                    // Prototype clones made before first publication all enter here in the same shared sample,
                    // reproducing FIELD's native phase lock.
                    o.SrcStart = nowMs;
                    continue;
                }
                if (SourceCellAt(o, _previousFrameTimeMs) != SourceCellAt(o, _currentFrameTimeMs))
                {
                    reasons |= GfxPresentationReason.DiscreteSourceCell;
                    break;
                }
            }
            return reasons;
        }
    }

    private static long SourceCellAt(GfxObject o, long nowMs)
    {
        long elapsed = System.Math.Max(0, nowMs - o.SrcStart);
        return elapsed / o.SrcPeriod % o.SrcFrameCount;
    }

    /// <summary>Back-compat: snapshot with no animation clock (nowMs = 0) — deterministic, for headless
    /// callers and existing tests.</summary>
    public IReadOnlyList<RenderObject> SnapshotVisibleObjects() => SnapshotVisibleObjects(0);

    /// <summary>Visible objects in ascending-handle order (= z-order), each with its source surface resolved
    /// and its active channels interpolated at <paramref name="nowMs"/>. Position is the base V24 (a direct
    /// transform (op 0x22f); scale and translation are independent one-shot matrix channels. Ops
    /// 0x229-0x22e contribute a second sampled matrix only to their selected handle range. The
    /// src-rect channel (0x239/0x231) selects the spritesheet cell; the color channel (0x232) ping-pongs the
    /// alpha/tint. Channel Start fields seed to nowMs on first sight.</summary>
    public IReadOnlyList<RenderObject> SnapshotVisibleObjects(long nowMs)
    {
        var list = new List<RenderObject>();
        SnapshotVisibleObjects(nowMs, list);
        return list;
    }

    /// <summary>Fill a caller-owned snapshot buffer. The Godot compositor reuses one list so its backing
    /// array survives across frames; callers that need an independently retained snapshot should use the
    /// returning overload.</summary>
    public void SnapshotVisibleObjects(long nowMs, List<RenderObject> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        lock (_lock)
        {
            list.Clear();
            Affine2D? rangeAffine = null;
            bool rangeTimeVarying = false;
            if (_rangeTransformCount > 0)
            {
                var r = _rangeTransform;
                bool hadRangeOneShot = r.ScaleEnabled || r.RotationChannelEnabled || r.TranslationEnabled;
                if (hadRangeOneShot && r.OneShotStartMs < 0) r.OneShotStartMs = nowMs;
                var rangeScale = SampleMatrixChannel(ref r.ScaleCurrent, r.ScaleTarget, r.ScaleDelayMs,
                    r.ScaleDurationMs, r.OneShotStartMs, ref r.ScaleEnabled, nowMs);
                var rangeRotation = SampleRotationChannel(ref r.RotationCurrent, r.RotationTarget,
                    r.RotationDelayMs, r.RotationDurationMs, r.OneShotStartMs,
                    ref r.RotationChannelEnabled, nowMs);
                var rangeTranslation = SampleMatrixChannel(ref r.TranslationCurrent, r.TranslationTarget,
                    r.TranslationDelayMs, r.TranslationDurationMs, r.OneShotStartMs,
                    ref r.TranslationEnabled, nowMs);
                if (!r.ScaleEnabled && !r.RotationChannelEnabled && !r.TranslationEnabled)
                    r.OneShotStartMs = -1;
                rangeTimeVarying = r.ScaleEnabled || r.RotationChannelEnabled || r.TranslationEnabled;
                rangeAffine = Transform2DMath.Build(new TransformState(
                    rangeScale.X, rangeScale.Y, rangeScale.Z,
                    rangeTranslation.X, rangeTranslation.Y, rangeTranslation.Z,
                    r.V18.X, r.V18.Y, r.V18.Z,
                    rangeRotation.X, rangeRotation.Y, rangeRotation.Z, rangeRotation.Angle));
            }
            foreach (long handle in _orderedObjectHandles)
            {
                var o = _objects[handle];
                if (!o.Visible) continue;
                bool hadOneShot = o.OneShotColorEnabled || o.ScaleEnabled ||
                                  o.RotationChannelEnabled || o.TranslationEnabled;
                // Created/mutable surfaces have no file resource id but remain a distinct surface class.
                // Zero is an active "key black" value, so created and absent slots both default to -1.
                var (resId, ck) = _surfaces.TryGetValue(o.SourceSlot, out var s) ? s : (0L, -1L);
                bool createdSurface = _createdSurfaces.Contains(o.SourceSlot);

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
                    else if (o.StaticColorMode == 0 && o.PostBindStaticColorBlend)
                    {
                        // FIELD suppresses a still-bound idle unit while its clone moves; CALLBACK_SETTING
                        // rebuilds the erased ADV backing and writes the configured opacity after binding it.
                        // Pre-bind static alpha-zero CG initialization remains the opaque mode-0 path.
                        alpha = a; strength = 0; blend = BlendKind.Alpha; multiplyTint = true;
                    }
                    else if (o.StaticColorMode == 2)
                    {
                        // Native transition-source mode: 0xffffffff is opaque identity modulation,
                        // not a request to replace every texel with white.
                        alpha = a; strength = 0; blend = BlendKind.Alpha;
                    }
                    else if (createdSurface)
                    {
                        // Native created/render-target surfaces use packed alpha as object opacity.
                        // SYSTEM4/BUNKI relies on this for translucent panels; FIELD uses the same surface
                        // at alpha 0x40 beneath the minimap so the paper chrome remains visible.
                        alpha = a; strength = 0; blend = BlendKind.Alpha; multiplyTint = true;
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
                double cycleScaleX = 1, cycleScaleY = 1, cycleScaleZ = 1;
                if (o.ScaleCycleEnabled && o.ScaleCyclePeriodMs > 0)
                {
                    if (o.ScaleCycleStartMs < 0) o.ScaleCycleStartMs = nowMs;
                    double weight = ScaleCycleWeight(nowMs, o.ScaleCycleStartMs, o.ScaleCyclePeriodMs);
                    cycleScaleX += (o.ScaleCycleTarget.X - 1) * weight;
                    cycleScaleY += (o.ScaleCycleTarget.Y - 1) * weight;
                    cycleScaleZ += (o.ScaleCycleTarget.Z - 1) * weight;
                }
                if (o.RotationEnabled && o.RotationPeriodMs > 0)
                {
                    if (o.RotationStartMs < 0) o.RotationStartMs = nowMs;
                    long elapsed = System.Math.Max(0, nowMs - o.RotationStartMs);
                    cycleAngle = ((elapsed % o.RotationPeriodMs) * 360) / o.RotationPeriodMs;
                }

                SurfaceTransitionState? transition = _surfaceTransitions.TryGetValue(o.SourceSlot, out var st)
                    ? SampleTransition(st, nowMs) : null;
                Affine2D? objectRangeTransform = rangeAffine is { } ra &&
                    handle >= _rangeTransformFirst && handle - _rangeTransformFirst < _rangeTransformCount
                    ? ra : null;
                bool timeVarying =
                    o.OneShotColorEnabled || o.ScaleEnabled || o.RotationChannelEnabled ||
                    o.TranslationEnabled ||
                    (o.SrcAnim && o.SrcPeriod > 0) ||
                    (o.ColorAnim && o.ColorPeriod > 0) ||
                    (o.ScaleCycleEnabled && o.ScaleCyclePeriodMs > 0) ||
                    (o.RotationEnabled && o.RotationPeriodMs > 0) ||
                    transition is { Progress: < 1.0 } ||
                    (objectRangeTransform != null && rangeTimeVarying);
                list.Add(new RenderObject(handle, resId, ck, srcX, srcY, w, h,
                                          (int)o.V24.X, (int)o.V24.Y,
                                          new TransformState(scale.X, scale.Y, scale.Z,
                                                             translation.X, translation.Y, translation.Z,
                                                             o.V18.X, o.V18.Y, o.V18.Z,
                                                             rotation.X, rotation.Y, rotation.Z, rotation.Angle),
                                          new RotationCycleState(o.RotationEnabled, o.RotationPeriodMs,
                                                                 o.RotationAxis.X, o.RotationAxis.Y,
                                                                 o.RotationAxis.Z, cycleAngle),
                                          alpha, tint, strength, blend, multiplyTint, transition,
                                          colorTransition, objectRangeTransform, timeVarying,
                                          new ScaleCycleState(o.ScaleCycleEnabled, o.ScaleCyclePeriodMs,
                                                              cycleScaleX, cycleScaleY, cycleScaleZ)));
            }
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

    private static double ScaleCycleWeight(long now, long start, long period)
    {
        if (period <= 0) return 0;
        long elapsed = System.Math.Max(0, now - start);
        long position = elapsed % period;
        return 2.0 * System.Math.Min(position, period - position) / period;
    }

    /// <summary>Pack (alpha, rgb) → 0xAARRGGBB, matching op 0x202/0x203's handler bit-manipulation for the
    /// common (non-negative-sentinel) case. The alpha&lt;0 / color&lt;0 native-fetch path is deferred.</summary>
    public static long PackColor(long alpha, long color)
        => ((alpha & 0xff) << 24) | (color & 0xffffff);
}
