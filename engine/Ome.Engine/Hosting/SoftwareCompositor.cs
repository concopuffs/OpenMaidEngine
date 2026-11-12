using Ome.Engine.Diagnostics;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Hosting;

/// <summary>
/// The software compositor (engine layering spec, milestone 4): composes the VM's visible retained objects, in
/// ascending-handle order (the engine's z-order), into one canvas-sized RGBA buffer that the front end uploads. Each
/// object blits its live surface's rectangle at its position; decoded catalog pixels are cached by identity, since
/// this runs every frame. Native scale and translation channels are sampled by <see cref="GfxState"/> and applied
/// here; an object's opacity comes only from its blend and colour.
/// </summary>
/// <remarks>The front end calls <see cref="Compose"/> on its thread, once per frame that the GPU path does not draw:
/// a screen transition, a diagnostic log, or a run without the GPU renderer. Normal play draws on the GPU.</remarks>
public sealed class SoftwareCompositor
{
    private readonly EngineHost _host;
    private readonly GfxState _gfx;
    private readonly FrameClock _clock;
    private readonly int _width;
    private readonly int _height;
    // One managed composition target for the entire frame. Layer helpers mutate it in place; only the completed
    // frame crosses to the front end, which avoids a full read-back per layer.
    private readonly byte[] _pixels;
    private readonly List<RenderObject> _visibleSnapshot = new(1024);
    private readonly BlurRangeSources _blurSources = new();
    private sealed record CachedPixels(int Width, int Height, byte[] Rgba);
    private readonly Dictionary<(int AssetId, long Key), CachedPixels> _pixelCache = new();
    private readonly string? _gfxLogPath;       // --gfx-log <file>: log per-object compositor draw/skip CHANGES
    private System.IO.StreamWriter? _gfxLog;
    private readonly Dictionary<long, string> _lastGfxDecision = new();
    private int _gfxLogFrame;
    private int _page;

    public SoftwareCompositor(EngineHost host, GfxState gfx, FrameClock clock, Sys4LogicalCanvas canvas,
                              string? gfxLogPath = null)
    {
        _host = host;
        _gfx = gfx;
        _clock = clock;
        _width = canvas.Width;
        _height = canvas.Height;
        _pixels = new byte[canvas.RgbaByteCount];
        _gfxLogPath = gfxLogPath;
    }

    /// <summary>The composed frame: <c>Width * Height</c> RGBA8 pixels, rows top down. Valid after
    /// <see cref="Compose"/> until the next call.</summary>
    public byte[] Pixels => _pixels;
    public int Width => _width;
    public int Height => _height;

    /// <summary><c>--perf-log</c>'s per-frame counters.</summary>
    public PerformanceFrameLog? Perf { get; init; }

    /// <summary><c>--timeline-log</c>: an <c>objects</c> event for each frame whose draw decisions changed.</summary>
    public TimelineLog? Timeline { get; init; }

    /// <summary>Drops the prepared pixels of every catalog image, so the compositor takes each one from the host
    /// again: after a language switch, its language assets apply (localization spec §12).</summary>
    public void ForgetPreparedPixels() => _pixelCache.Clear();

    /// <summary>A retained object's live source slot, read under the gfx lock (the VM thread changes the object).
    /// </summary>
    private int? SourceSlotOf(long handle) => _gfx.TryGetSourceSlot(handle, out int slot) ? slot : null;

    /// <summary>
    /// Composes one frame into <see cref="Pixels"/>. <paramref name="sampledVisible"/> is the visible objects the GPU
    /// path already sampled this frame, with its <paramref name="preserveExistingPixels"/>; null samples them here.
    /// <paramref name="page"/> labels <c>--gfx-log</c>'s frames. The caller ends the frame's performance record
    /// (<see cref="PerformanceFrameLog.EndRecomposite"/>) once it has uploaded the pixels.
    /// </summary>
    public void Compose(IReadOnlyList<RenderObject>? sampledVisible, bool preserveExistingPixels, int page)
    {
        _page = page;
        long phase = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocationPhase = Perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        bool hasScreenTransition = _host.TrySnapshotScreenTransition(out var transition);
        Perf?.RecordSnapshotAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        Perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);
        if (!hasScreenTransition && sampledVisible == null)
        {
            phase = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
            allocationPhase = Perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            preserveExistingPixels = _host.SnapshotBackbufferObjects(
                _gfx, _clock.NowMs, _visibleSnapshot).PreserveExistingPixels;
            Perf?.RecordSnapshotAllocation(
                PerformanceFrameLog.AllocatedBytes() - allocationPhase);
            Perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);
        }
        if (hasScreenTransition) preserveExistingPixels = false;
        Perf?.BeginRecomposite(hasScreenTransition);

        phase = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        if (!preserveExistingPixels)
        {
            System.Array.Clear(_pixels);
            long background = _gfx.DisplayBackgroundColor;
            if (background != 0)
                FillQuad(0, 0, _width, _height, background, 1f);
        }
        Perf?.RecordClear(PerformanceFrameLog.Timestamp() - phase);
        Dictionary<long, string>? decisions = _gfxLogPath != null || Timeline != null ? new() : null;
        if (hasScreenTransition)
        {
            allocationPhase = Perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            // Native mode 4 keeps the captured source opaque and alpha-composites the complete target
            // surface over it. The target is one flattened surface: its objects drawn opaquely over the
            // surface's clear (black, or the overlay colour for the white fade). Fading each object on its own
            // instead dims the whole frame mid-transition and lets the target's layers show through each other.
            CompositeVisibleObjects(transition.Source, 1f, decisions);
            CompositeFlattenedTransitionTarget(transition, decisions);
            Perf?.RecordCompositeAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        }
        else
        {
            allocationPhase = Perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            CompositeVisibleObjects(sampledVisible ?? _visibleSnapshot, 1f, decisions);
            Perf?.RecordCompositeAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        }
        if (decisions != null) LogGfxDecisionChanges(decisions);
    }

    private byte[] _transitionSourcePixels = [];

    /// <summary>Blend the transition's flattened target surface over the source already in the screen
    /// buffer at the transition's progress (native screen_transition_tick mode 4).</summary>
    private void CompositeFlattenedTransitionTarget(
        LegacyScreenTransitionSnapshot transition,
        System.Collections.Generic.Dictionary<long, string>? decisions)
    {
        int alpha = Ome.Engine.Model.LegacyScreenTransitionComposite.Alpha(transition.Progress);
        if (alpha == 0) return;
        if (_transitionSourcePixels.Length != _pixels.Length)
            _transitionSourcePixels = new byte[_pixels.Length];
        System.Buffer.BlockCopy(_pixels, 0, _transitionSourcePixels, 0, _pixels.Length);
        System.Array.Clear(_pixels);
        FillQuad(0, 0, _width, _height, transition.OverlayRgb, 1f);
        CompositeVisibleObjects(transition.Target, 1f, decisions);
        if (alpha < 256)
            Ome.Engine.Model.LegacyScreenTransitionComposite.BlendTargetOverSource(
                _pixels, _transitionSourcePixels, alpha);
    }

    private void CompositeVisibleObjects(
        IReadOnlyList<RenderObject> visible,
        float globalOpacity,
        System.Collections.Generic.Dictionary<long, string>? decisions)
    {
        _blurSources.Collect(visible);
        int z = 0;
        foreach (var v in visible)   // interpolate at the retained-presentation clock
        {
            Perf?.RecordObject(v.TimeVarying);
            if (v.RadialBlurTransition == null && v.DirectionalBlurTransition == null &&
                _blurSources.Contains(v.Handle))
            {
                Perf?.RecordSkippedLayer();
                if (decisions != null) decisions[v.Handle] = "SKIP(blur source range)";
                continue;
            }
            var t = v.Transform;
            var affine = Ome.Engine.Model.Transform2DMath.Build(t, v.Rotation, v.ScaleCycle, v.TranslationCycle);
            var localToDest = affine.FromLocalOrigin(v.DstX, v.DstY);
            if (v.RangeTransform is { } rangeTransform)
                localToDest = localToDest.Then(rangeTransform);
            var projected = localToDest.Apply(0, 0);
            int dstX = (int)System.Math.Round(projected.X);
            int dstY = (int)System.Math.Round(projected.Y);
            float opacity = v.Alpha / 255f * globalOpacity;  // transform Z is never opacity
            float strength = v.TintStrength / 255f;          // tint-blend / fill strength
            int? rawSlot = SourceSlotOf(v.Handle);
            long resolveStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var surfaceTexture = rawSlot != null
                ? _host.ResolveSurfaceTexture(rawSlot.Value, v.SurfaceResId)
                : null;
            Perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _host.IsMovieSurfaceBound(rawSlot.Value);
            // These strings exist only for --gfx-log/timeline diagnostics. DEBUGMAP visits roughly one
            // thousand retained objects per composition, so formatting them unconditionally creates
            // several megabytes of short-lived garbage even in an ordinary run.
            string? outcome = null;
            if (v.DirectionalBlurTransition is { } directionalBlur)
            {
                Perf?.RecordTransitionLayer();
                int layers = DrawDirectionalBlurRange(visible, directionalBlur);
                if (decisions != null)
                    outcome = $"DIRECTIONAL-BLUR key=0x{directionalBlur.CommandKey:x} " +
                              $"angle={directionalBlur.AngleDegrees:0.0} " +
                              $"length={directionalBlur.Length:0.0} " +
                              $"progress={directionalBlur.Progress:0.000} layers={layers}";
            }
            else if (v.RadialBlurTransition is { } radialBlur)
            {
                Perf?.RecordTransitionLayer();
                int layers = DrawRadialBlurRange(visible, radialBlur);
                if (decisions != null)
                    outcome = $"RADIAL-BLUR key=0x{radialBlur.CommandKey:x} " +
                              $"center=({radialBlur.CenterX:0.0},{radialBlur.CenterY:0.0}) " +
                              $"length={radialBlur.Length:0.0} progress={radialBlur.Progress:0.000} layers={layers}";
            }
            else if (v.PatternedSurfaceTransition is { } patterned)
            {
                Perf?.RecordTransitionLayer();
                int layers = DrawTransitionRange(visible, CrossfadeApproximation(patterned));
                if (decisions != null)
                    outcome = $"PATTERN-TRANSITION slot={patterned.TargetSlot} " +
                              $"key=0x{patterned.CommandKey:x} mode={(int)patterned.Mode} " +
                              $"thickness={patterned.PatternThickness} progress={patterned.Progress:0.000} " +
                              $"forced={patterned.Forced} layers={layers}";
            }
            else if (v.SurfaceTransition is { } transition)
            {
                Perf?.RecordTransitionLayer();
                int layers = DrawTransitionRange(visible, transition);
                if (decisions != null)
                    outcome = $"TRANSITION slot={transition.TargetSlot} key=0x{transition.CommandKey:x} " +
                              $"progress={transition.Progress:0.000} forced={transition.Forced} layers={layers}";
            }
            else if (v.SurfaceResId == 0 && surfaceTexture == null)
            {
                // A colored object with no bound surface = a fade/flash fill (e.g. fade-to-black). Its presence
                // is the tint STRENGTH (0=absent, 255=solid), scaled by any object opacity. Uncolored surfaceless
                // objects are render targets — still skipped (slice C).
                if (v.Blend != Ome.Engine.Model.BlendKind.Opaque)
                {
                    int baseW = v.W > 0 ? v.W : _width;
                    int baseH = v.H > 0 ? v.H : _height;
                    // One-shot/mode-1 packed color supplies opacity directly. Static mode-0 fills retain
                    // the tint-strength convention used by the existing effect objects.
                    float fillA = v.MultiplyTint ? opacity : opacity * strength;
                    Perf?.RecordFillLayer();
                    FillAffineQuad(baseW, baseH, localToDest, v.Tint, fillA);
                    if (decisions != null)
                        outcome = $"FILL tint=0x{v.Tint:x6} a={fillA:0.00} {baseW}x{baseH}@({dstX},{dstY}) " +
                                  $"base=({v.DstX},{v.DstY}) anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) " +
                                  $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) " +
                                  $"trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) rot={v.Rotation.AngleDegrees:0.0}" +
                                  ColorTimeline(v.ColorTransition);
                }
                else
                {
                    Perf?.RecordSkippedLayer();
                    if (decisions != null) outcome = "SKIP(no-resId, opaque render-target)";
                }
            }
            else
            {
                var texture = surfaceTexture;
                if (texture == null && !movieSurfaceBound)
                {
                    resolveStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = _host.ResolveResIdTexture(v.SurfaceResId);
                    Perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null)
                {
                    Perf?.RecordSkippedLayer();
                    if (decisions != null) outcome = $"SKIP(resId=0x{v.SurfaceResId:x} UNRESOLVED)";
                }
                else
                {
                    BlitLayer(texture.Value.Image, texture.Value.AssetId, v.ColorKey, v.Tint, strength, v.SrcX, v.SrcY, v.W, v.H,
                              localToDest, opacity, v.MultiplyTint, texture.Value.IsDynamic, v.Blend);
                    if (decisions != null)
                        outcome = $"slot={rawSlot} DRAWN resId=0x{v.SurfaceResId:x} {texture.Value.Name} " +
                                  $"src=({v.SrcX},{v.SrcY} {v.W}x{v.H}) base=({v.DstX},{v.DstY}) " +
                                  $"anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) dst=({dstX},{dstY}) " +
                                  $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) " +
                                  $"rot=({t.RotationAngleDegrees:0.0}+{v.Rotation.AngleDegrees:0.0}) " +
                                  $"mode={_gfx.TryGet(v.Handle)?.StaticColorMode} op={opacity:0.00} tintStr={strength:0.00}" +
                                  ColorTimeline(v.ColorTransition);
                }
            }
            if (decisions != null) decisions[v.Handle] = $"z{z} {outcome}";
            z++;
        }
    }

    private static string ColorTimeline(Ome.Engine.Model.ColorTransitionState? state)
        => state is { } c
            ? $" color=0x{c.Current:x8}->0x{c.Target:x8} colorProgress={c.Progress:0.000}"
            : "";

    // Native type-0 surface commands first leave range A in normal z-order, then alpha-composite range B
    // into the target surface. SC0000 binds that target to handle+2, above both source handles, so drawing
    // range B here with progress produces old*(1-progress)+new*progress without disturbing ambient channels.
    private int DrawTransitionRange(IReadOnlyList<RenderObject> visible, SurfaceTransitionState transition)
    {
        int drawn = 0;
        long end = transition.RangeBStart + transition.RangeBCount;
        foreach (var source in visible)
        {
            if (source.Handle < transition.RangeBStart || source.Handle >= end ||
                source.SurfaceTransition != null || source.PatternedSurfaceTransition != null)
                continue;
            Perf?.RecordObject(source.TimeVarying);
            var affine = Transform2DMath.Build(source.Transform, source.Rotation, source.ScaleCycle, source.TranslationCycle)
                .FromLocalOrigin(source.DstX, source.DstY);
            if (source.RangeTransform is { } rangeTransform)
                affine = affine.Then(rangeTransform);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            int? rawSlot = SourceSlotOf(source.Handle);
            long resolveStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawSlot != null
                ? _host.ResolveSurfaceTexture(rawSlot.Value, source.SurfaceResId)
                : null;
            Perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _host.IsMovieSurfaceBound(rawSlot.Value);
            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque)
                {
                    Perf?.RecordSkippedLayer();
                    continue;
                }
                int w = source.W > 0 ? source.W : _width;
                int h = source.H > 0 ? source.H : _height;
                Perf?.RecordFillLayer();
                FillAffineQuad(w, h, affine, source.Tint, opacity * source.TintStrength / 255f);
            }
            else
            {
                if (!movieSurfaceBound && texture == null)
                {
                    resolveStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = _host.ResolveResIdTexture(source.SurfaceResId);
                    Perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null)
                {
                    Perf?.RecordSkippedLayer();
                    continue;
                }
                BlitLayer(texture.Value.Image, texture.Value.AssetId, source.ColorKey, source.Tint, source.TintStrength / 255f,
                          source.SrcX, source.SrcY, source.W, source.H, affine, opacity, source.MultiplyTint,
                          texture.Value.IsDynamic, source.Blend);
            }
            drawn++;
        }
        return drawn;
    }

    /// <summary>A patterned transition drawn as a crossfade over the same ranges and timing: neither path has the
    /// pattern masks yet. Shared with the GPU path.</summary>
    public static SurfaceTransitionState CrossfadeApproximation(PatternedSurfaceTransitionState transition)
        => new(transition.CommandKey, transition.TargetSlot,
               transition.RangeAStart, transition.RangeACount,
               transition.RangeBStart, transition.RangeBCount,
               transition.DelayMs, transition.DurationMs, transition.StartMs,
               transition.Progress, transition.Forced);

    private int DrawRadialBlurRange(
        IReadOnlyList<RenderObject> visible, RadialBlurRangeTransitionState effect)
        => DrawBlurRange(visible, null, effect);

    private int DrawDirectionalBlurRange(
        IReadOnlyList<RenderObject> visible, DirectionalBlurRangeTransitionState effect)
        => DrawBlurRange(visible, effect, null);

    private int DrawBlurRange(
        IReadOnlyList<RenderObject> visible,
        DirectionalBlurRangeTransitionState? directional,
        RadialBlurRangeTransitionState? radial)
    {
        double length = directional?.Length ?? radial!.Value.Length;
        int samples = System.Math.Abs(length) < 0.5 ? 1 : 6;
        int drawn = 0;
        foreach (RenderObject source in visible)
        {
            bool contains = directional?.Contains(source.Handle) ?? radial!.Value.Contains(source.Handle);
            if (!contains || source.DirectionalBlurTransition != null || source.RadialBlurTransition != null)
                continue;
            Perf?.RecordObject(source.TimeVarying);
            var baseAffine = Transform2DMath.Build(source.Transform, source.Rotation, source.ScaleCycle, source.TranslationCycle)
                .FromLocalOrigin(source.DstX, source.DstY);
            if (source.RangeTransform is { } rangeTransform)
                baseAffine = baseAffine.Then(rangeTransform);
            int? rawSlot = SourceSlotOf(source.Handle);
            long resolveStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawSlot != null
                ? _host.ResolveSurfaceTexture(rawSlot.Value, source.SurfaceResId)
                : null;
            Perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _host.IsMovieSurfaceBound(rawSlot.Value);
            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque) { Perf?.RecordSkippedLayer(); continue; }
                int width = source.W > 0 ? source.W : _width;
                int height = source.H > 0 ? source.H : _height;
                for (int sample = 0; sample < samples; sample++)
                {
                    var affine = BlurSampleAffine(baseAffine, directional, radial, sample, samples, _width);
                    Perf?.RecordFillLayer();
                    FillAffineQuad(
                        width, height, affine, source.Tint,
                        source.Alpha / 255f * source.TintStrength / 255f / samples);
                    drawn++;
                }
                continue;
            }
            if (!movieSurfaceBound && texture == null)
                texture = _host.ResolveResIdTexture(source.SurfaceResId);
            if (texture == null) { Perf?.RecordSkippedLayer(); continue; }

            for (int sample = 0; sample < samples; sample++)
            {
                var affine = BlurSampleAffine(baseAffine, directional, radial, sample, samples, _width);
                var resolved = texture.Value;
                BlitLayer(
                    resolved.Image, resolved.AssetId, source.ColorKey, source.Tint,
                    source.TintStrength / 255f, source.SrcX, source.SrcY, source.W, source.H,
                    affine, source.Alpha / 255f / samples, source.MultiplyTint,
                    resolved.IsDynamic, BlendKind.Alpha);
                drawn++;
            }
        }
        return drawn;
    }

    // Diagnostic (--gfx-log): print, per rendered frame, only the objects whose compositor outcome CHANGED
    // since last frame (added / gone / drawn↔skip / resId change). Quiet until something actually changes, so
    // the frame where the background drops out — and WHY — stands out. See systematic-debugging of the grey-BG.
    private void LogGfxDecisionChanges(System.Collections.Generic.Dictionary<long, string> curr)
    {
        if (_gfxLogPath != null && _gfxLog == null)
        {
            var dir = System.IO.Path.GetDirectoryName(_gfxLogPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            _gfxLog = new System.IO.StreamWriter(_gfxLogPath!) { AutoFlush = true };
        }
        _gfxLogFrame++;
        var lines = new System.Collections.Generic.List<string>();
        foreach (var kv in curr)
            if (!_lastGfxDecision.TryGetValue(kv.Key, out var prev) || prev != kv.Value)
                lines.Add($"  0x{kv.Key:x}: {kv.Value}" + (_lastGfxDecision.ContainsKey(kv.Key) ? "" : "  [NEW]"));
        foreach (var kv in _lastGfxDecision)
            if (!curr.ContainsKey(kv.Key))
                lines.Add($"  0x{kv.Key:x}: GONE (was {kv.Value})");
        if (lines.Count > 0 && _gfxLog != null)
        {
            _gfxLog.WriteLine($"[frame {_gfxLogFrame} nowMs={_clock.NowMs} page={_page}] {curr.Count} visible, {lines.Count} changes:");
            foreach (var l in lines) _gfxLog.WriteLine(l);
        }
        if (lines.Count > 0)
            Timeline?.Event("objects", new() { ["visible_count"] = curr.Count, ["changes"] = lines.ToArray() });
        _lastGfxDecision.Clear();
        foreach (var kv in curr) _lastGfxDecision[kv.Key] = kv.Value;
    }

    // Blit one object's surface rect. Static source pixels are cached per (assetId, colorKey): on first use, texels
    // matching the surface colorkey are made transparent (native bakes the key at load — engine-re.md §Blend).
    // Mode 0 uses tintStrength to LERP texel RGB toward tint. Mode 1 uses packed RGB modulation and
    // SRCALPHA/ONE additive composition; black source pixels therefore contribute nothing.
    private void BlitLayer(RgbaImage decoded, int assetId, long colorKey, long tint, float tintStrength, int srcX, int srcY, int w, int h,
                           Ome.Engine.Model.Affine2D localToDest, float alpha = 1f, bool multiplyTint = false,
                           bool dynamic = false, BlendKind blend = BlendKind.Alpha)
    {
        // Native gfx_object_blit_d3d9 clips the explicit source rectangle and returns without drawing when
        // right<=left or bottom<=top. FIELD deliberately creates zero-area prototype objects from SO005;
        // expanding those dimensions to the full texture leaks the entire spritesheet onto the map.
        if (w <= 0 || h <= 0) return;
        long sourcePrepStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long sourcePrepAllocated = Perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        var cacheKey = (assetId, colorKey);
        int sourceWidth, sourceHeight;
        byte[] sourcePixels;
        if (dynamic)
        {
            // Decoder samples replace the pixels of one retained surface. Never enter them in the static cache.
            // Clone only when applying a key so the decoder-owned newest-frame buffer remains untouched.
            sourceWidth = decoded.Width;
            sourceHeight = decoded.Height;
            sourcePixels = decoded.Pixels;
            if (Ome.Engine.Model.BlendMath.HasColorKey(colorKey))
            {
                sourcePixels = (byte[])sourcePixels.Clone();
                BakeColorKey(sourcePixels, colorKey);
            }
        }
        else
        {
            if (!_pixelCache.TryGetValue(cacheKey, out var cached))
            {
                byte[] pixels = decoded.Pixels;
                if (Ome.Engine.Model.BlendMath.HasColorKey(colorKey))
                {
                    pixels = (byte[])pixels.Clone();
                    BakeColorKey(pixels, colorKey);
                }
                cached = new CachedPixels(decoded.Width, decoded.Height, pixels);
                _pixelCache[cacheKey] = cached;
            }
            sourceWidth = cached.Width;
            sourceHeight = cached.Height;
            sourcePixels = cached.Rgba;
        }

        int sw = w;
        int sh = h;
        sw = System.Math.Min(sw, sourceWidth - srcX);
        sh = System.Math.Min(sh, sourceHeight - srcY);
        Perf?.RecordSourcePrep(PerformanceFrameLog.Timestamp() - sourcePrepStarted);
        Perf?.RecordSourcePrepAllocation(PerformanceFrameLog.AllocatedBytes() - sourcePrepAllocated);
        if (sw <= 0 || sh <= 0) return;
        long rasterStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        Ome.Engine.Model.SoftwareAffineRasterizer.BlitRgba(
            _pixels, _width, _height, sourcePixels, sourceWidth, sourceHeight,
            srcX, srcY, sw, sh, localToDest, tint, tintStrength, alpha, multiplyTint, blend);
        Perf?.RecordRaster(sw, sh, localToDest, _width, _height, dynamic, blend,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    private void FillAffineQuad(int w, int h, Ome.Engine.Model.Affine2D localToDest, long tint, float alpha)
    {
        long rasterStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        Ome.Engine.Model.SoftwareAffineRasterizer.FillRgba(
            _pixels, _width, _height, w, h, localToDest, tint, alpha);
        Perf?.RecordRaster(w, h, localToDest, _width, _height, false, BlendKind.Alpha,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    // Alpha-blend a solid tint (0xRRGGBB) rectangle over the screen — the surfaceless fade/flash fill.
    private void FillQuad(int dstX, int dstY, int w, int h, long tint, float alpha)
    {
        int ia = (int)(System.Math.Clamp(alpha, 0f, 1f) * 255);
        if (ia == 0) return;
        long rasterStarted = Perf != null ? PerformanceFrameLog.Timestamp() : 0;
        int tr = (int)((tint >> 16) & 0xff), tg = (int)((tint >> 8) & 0xff), tb = (int)(tint & 0xff);
        byte[] dst = _pixels;
        int dw = _width, dh = _height;
        int x0 = System.Math.Max(0, -dstX), x1 = System.Math.Min(w, dw - dstX);
        int y0 = System.Math.Max(0, -dstY), y1 = System.Math.Min(h, dh - dstY);
        if (x1 <= x0 || y1 <= y0) return;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int dxp = dstX + x, dyp = dstY + y;
                int di = (dyp * dw + dxp) * 4;
                dst[di]     = (byte)((tr * ia + dst[di]     * (255 - ia)) / 255);
                dst[di + 1] = (byte)((tg * ia + dst[di + 1] * (255 - ia)) / 255);
                dst[di + 2] = (byte)((tb * ia + dst[di + 2] * (255 - ia)) / 255);
                dst[di + 3] = (byte)System.Math.Min(255, dst[di + 3] + ia);
            }
        Perf?.RecordFillLayer();
        Perf?.RecordRaster(w, h, new Affine2D(1, 0, 0, 1, dstX, dstY),
                            _width, _height, false, BlendKind.Alpha,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    // Make colorkey-matching texels transparent (native colorkey is baked at surface load).
    private static void BakeColorKey(byte[] px, long colorKey)
    {
        for (int i = 0; i < px.Length; i += 4)
            if (Ome.Engine.Model.BlendMath.ColorKeyMatches(px[i], px[i + 1], px[i + 2], colorKey))
                px[i + 3] = 0;
    }


    /// <summary>One of a blur's samples: a translated copy along the angle (directional) or a zoom about the
    /// centre (radial). Shared with the GPU path.</summary>
    public static Affine2D BlurSampleAffine(
        Affine2D source, DirectionalBlurRangeTransitionState? directional,
        RadialBlurRangeTransitionState? radial,
        int sample, int sampleCount, int viewportWidth)
    {
        double fraction = sampleCount == 1 ? 0 : sample / (double)(sampleCount - 1);
        if (directional is { } directionalEffect)
        {
            double radians = directionalEffect.AngleDegrees * System.Math.PI / 180.0;
            double distance = directionalEffect.Length * fraction;
            return source.Then(new Affine2D(
                1, 0, 0, 1,
                System.Math.Cos(radians) * distance,
                System.Math.Sin(radians) * distance));
        }
        RadialBlurRangeTransitionState effect = radial!.Value;
        double scale = 1.0 + effect.Length / System.Math.Max(1, viewportWidth) * fraction;
        var zoom = new Affine2D(scale, 0, 0, scale,
            effect.CenterX * (1 - scale), effect.CenterY * (1 - scale));
        return source.Then(zoom);
    }
}
