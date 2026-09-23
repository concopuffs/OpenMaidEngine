namespace Age.Engine.Model;

public sealed partial class GfxState
{
    // ---- Separate global animation service clock (op 0x238; ctx+0x51b7c total / +0x51b78 elapsed).
    // Retained for its opcode family; 0x21e scale and 0x220 translation use frame-time directly instead. ----
    public long AnimClockDurationTicks { get; private set; }
    public long AnimClockGeneration { get; private set; }
    public long AnimationServiceFlags { get; private set; }
    public uint PreviousFrameTimeMilliseconds { get; private set; }
    public uint CurrentFrameTimeMilliseconds { get; private set; }
    private long _previousFrameTimeMs;
    private long _currentFrameTimeMs;

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
            o.EvaluatedRotationCache = RotationCache(o.RotationCurrent);
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
            o.EvaluatedRotationCache = RotationCache(o.RotationCurrent);
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

    /// <summary>Op 0x228: query the current translation decomposed from the native matrix at obj+0x16c.
    /// This is independent of both the delayed target at obj+0x1ac and draw/base position V24.</summary>
    public bool TryQueryTranslationCurrent(long handle, out (double X, double Y, double Z) current)
    {
        lock (_lock)
        {
            if (_objects.TryGetValue(handle, out var o))
            {
                current = o.TranslationCurrent;
                return true;
            }
            current = default;
            return false;
        }
    }

    /// <summary>Op 0x227: query the current axis-angle rotation stored at
    /// obj+0x1ec/+0x1f0/+0x1f4/+0x204, independently of the delayed target.</summary>
    public bool TryQueryRotationCurrent(
        long handle, out (double X, double Y, double Z, double Angle) current)
    {
        lock (_lock)
        {
            if (_objects.TryGetValue(handle, out var o))
            {
                current = o.RotationCurrent;
                return true;
            }
            current = default;
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

    /// <summary>Op 0x235: cyclic identity-to-target translation, sampled with a triangular
    /// ping-pong phase and composed after the sibling cyclic rotation.</summary>
    public void SetTranslationCycle(long handle, long periodMs, (long X, long Y, long Z) target)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.TranslationCyclePeriodMs = periodMs;
            o.TranslationCycleTarget = target;
            o.TranslationCycleEnabled = periodMs > 0;
            o.TranslationCycleStartMs = -1;
        }
    }

    /// <summary>Op 0x230: stop every cyclic object channel without changing its base/current state,
    /// one-shot channels, or retained cyclic targets. Native clears flag bit 2 and the five start/period
    /// pairs at obj+0x20c..0x230.</summary>
    public void ResetCyclicAnimationChannels(long handle)
    {
        lock (_lock)
        {
            var o = GetOrCreate(handle);
            o.ColorStart = -1;
            o.ScaleCycleStartMs = -1;
            o.RotationStartMs = -1;
            o.TranslationCycleStartMs = -1;
            o.SrcStart = -1;
            o.ColorPeriod = 0;
            o.ScaleCyclePeriodMs = 0;
            o.RotationPeriodMs = 0;
            o.TranslationCyclePeriodMs = 0;
            o.SrcPeriod = 0;
            o.ColorAnim = false;
            o.ScaleCycleEnabled = false;
            o.RotationEnabled = false;
            o.TranslationCycleEnabled = false;
            o.SrcAnim = false;

            // Preserve drop-in numbered-save compatibility for all five cyclic channels. The ten native
            // timing dwords form one contiguous range.
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

    private static long SourceCellAt(GfxObject o, long nowMs)
    {
        long elapsed = System.Math.Max(0, nowMs - o.SrcStart);
        return elapsed / o.SrcPeriod % o.SrcFrameCount;
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

    private static (double X, double Y, double Z, double Radians) RotationCache(
        (double X, double Y, double Z, double Angle) rotation)
        => (rotation.X, rotation.Y, rotation.Z, rotation.Angle * Math.PI / 180.0);

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
