using System.Linq;
using Age.Engine.Hosting;

namespace Age.Engine.Model;

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

    private readonly Dictionary<int, SurfaceTransition> _surfaceTransitions = new();
    private readonly Dictionary<int, MovieMaskTransition> _movieMaskTransitions = new();

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
}
