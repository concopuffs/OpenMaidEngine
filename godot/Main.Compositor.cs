using System.Collections.Generic;
using System.Linq;
using Godot;
using Age.Engine.Model;

public partial class Main
{
    private TextureRect _screenView = null!;              // shows the composited screen backbuffer
    private Image _screen = null!;                        // SYS4INI-sized immediate-mode canvas
    private ImageTexture _screenTex = null!;
    private GpuRetainedRenderer _gpuRenderer = null!;
    private bool _useGpuBackend = true;
    // One managed composition target for the entire frame. Layer helpers mutate it in place; only the
    // completed frame crosses the Godot Image boundary, avoiding a full GetData/SetData round-trip per layer.
    private byte[] _screenPixels = [];

    private string? _gfxLogPath;                // --gfx-log <file>: log per-object compositor draw/skip CHANGES
    private System.IO.StreamWriter? _gfxLog;
    private readonly System.Collections.Generic.Dictionary<long, string> _lastGfxDecision = new();
    private int _gfxLogFrame;

    // ---- retained per-frame compositor (main thread, from _Process) ----
    // Clear the screen and composite the VM's current VISIBLE gfx objects in ascending-handle order (= the
    // engine's z-order), each blitting its live surface's rect at its position. Decoded AGF pixels are cached
    // by catalog identity (this runs every frame). Native scale/translation matrix channels are sampled independently by
    // GfxState and applied here; object opacity comes only from the actual blend/color path.
    private sealed record CachedPixels(int Width, int Height, byte[] Rgba);
    private readonly System.Collections.Generic.Dictionary<(int AssetId, long Key), CachedPixels> _pixelCache = new();
    private readonly System.Collections.Generic.List<RenderObject> _visibleSnapshot = new(1024);

    private void Recomposite()
    {
        bool gpuSnapshotCaptured = false;
        BackbufferPublicationPolicy publicationPolicy = default;
        if (_useGpuBackend &&
            TryRecompositeGpu(out gpuSnapshotCaptured, out publicationPolicy)) return;
        _gpuRenderer.Visible = false;
        _screenView.Visible = true;
        RecompositeSoftware(
            gpuSnapshotCaptured ? _visibleSnapshot : null,
            publicationPolicy.PreserveExistingPixels);
    }

    private bool TryRecompositeGpu(
        out bool snapshotCaptured,
        out BackbufferPublicationPolicy publicationPolicy)
    {
        snapshotCaptured = false;
        publicationPolicy = default;
        // Preserve the existing high-volume object/timeline diagnostics exactly. They are debugging tools,
        // not performance workloads, and their software decision strings remain the canonical evidence.
        if (_gfxLogPath != null || _timeline != null) return false;

        long phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        if (_host.TrySnapshotScreenTransition(out _)) return false; // P4: whole-screen offscreen targets
        publicationPolicy =
            _host.SnapshotBackbufferObjects(_vm.Gfx, _clock.NowMs, _visibleSnapshot);
        snapshotCaptured = true;
        _perf?.RecordSnapshotAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        _perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);

        // Additive LERP-tint has not appeared in the target workloads and needs a dedicated additive shader
        // variant before leaving the software oracle.
        if (_visibleSnapshot.Any(v =>
            v.Blend == BlendKind.Additive && !v.MultiplyTint && v.TintStrength > 0))
            return false;

        if (_perf != null)
        {
            var presentStep = _trace.LatestStep;
            _perf.RecordPresentationCoordinate(presentStep?.Script ?? "<startup>",
                                               presentStep?.Offset ?? -1, presentStep?.Opcode ?? -1);
        }
        _perf?.BeginRecomposite(screenTransition: false);
        _gpuRenderer.BeginFrame(publicationPolicy.AppendGpuLayers);
        foreach (var v in _visibleSnapshot)
        {
            _perf?.RecordObject(v.TimeVarying);
            var affine = Transform2DMath.Build(v.Transform, v.Rotation, v.ScaleCycle)
                .FromLocalOrigin(v.DstX, v.DstY);
            if (v.RangeTransform is { } rangeTransform) affine = affine.Then(rangeTransform);
            float opacity = v.Alpha / 255f;
            var rawObject = _vm.Gfx.TryGet(v.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, v.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);

            if (v.SurfaceTransition is { } transition)
            {
                _perf?.RecordTransitionLayer();
                DrawTransitionRangeGpu(_visibleSnapshot, transition);
            }
            else if (v.SurfaceResId == 0 && texture == null)
            {
                if (v.Blend != BlendKind.Opaque)
                {
                    int width = v.W > 0 ? v.W : _screenWidth;
                    int height = v.H > 0 ? v.H : _screenHeight;
                    float fillOpacity = v.MultiplyTint
                        ? opacity
                        : opacity * v.TintStrength / 255f;
                    _perf?.RecordFillLayer();
                    if (_gpuRenderer.DrawFill(width, height, affine, v.Tint, fillOpacity))
                        _perf?.RecordGpuLayer(width, height, affine, _screenWidth, _screenHeight,
                                              dynamic: false, BlendKind.Alpha);
                }
                else _perf?.RecordSkippedLayer();
            }
            else
            {
                if (texture == null && !movieSurfaceBound)
                {
                    resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = _host.ResolveResIdTexture(v.SurfaceResId);
                    _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null) _perf?.RecordSkippedLayer();
                else
                {
                    var resolved = texture.Value;
                    bool drawn = _gpuRenderer.DrawTexture(resolved.Image, resolved.AssetId, v.ColorKey,
                        v.SrcX, v.SrcY, v.W, v.H, affine, v.Tint, v.TintStrength,
                        opacity, v.MultiplyTint, resolved.IsDynamic,
                        rawObject?.SourceSlot ?? v.Handle, v.Blend);
                    if (drawn)
                        _perf?.RecordGpuLayer(v.W, v.H, affine, _screenWidth, _screenHeight,
                                              resolved.IsDynamic, v.Blend);
                }
            }
        }

        var stats = _gpuRenderer.EndFrame();
        _perf?.RecordGpu(stats.DrawItems, stats.TextureUploads, stats.TextureUploadTicks);
        _screenView.Visible = false;
        _gpuRenderer.Visible = true;
        _perf?.EndRecomposite();
        return true;
    }

    // Native type-0 retained range transition: range A has already passed through ordinary z-order;
    // republish range B at the transition placeholder with progress-scaled source opacity. This mirrors
    // DrawTransitionRange's software-oracle order without allocating an offscreen CPU surface.
    private int DrawTransitionRangeGpu(IReadOnlyList<RenderObject> visible, SurfaceTransitionState transition)
    {
        int drawn = 0;
        long end = transition.RangeBStart + transition.RangeBCount;
        foreach (var source in visible)
        {
            if (source.Handle < transition.RangeBStart || source.Handle >= end || source.SurfaceTransition != null)
                continue;
            _perf?.RecordObject(source.TimeVarying);
            var affine = Transform2DMath.Build(source.Transform, source.Rotation, source.ScaleCycle)
                .FromLocalOrigin(source.DstX, source.DstY);
            if (source.RangeTransform is { } rangeTransform) affine = affine.Then(rangeTransform);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            var rawObject = _vm.Gfx.TryGet(source.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, source.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);

            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque)
                {
                    _perf?.RecordSkippedLayer();
                    continue;
                }
                int width = source.W > 0 ? source.W : _screenWidth;
                int height = source.H > 0 ? source.H : _screenHeight;
                _perf?.RecordFillLayer();
                if (_gpuRenderer.DrawFill(width, height, affine, source.Tint,
                    opacity * source.TintStrength / 255f))
                {
                    _perf?.RecordGpuLayer(width, height, affine, _screenWidth, _screenHeight,
                                          dynamic: false, BlendKind.Alpha);
                    drawn++;
                }
                continue;
            }

            if (!movieSurfaceBound && texture == null)
            {
                resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
                texture = _host.ResolveResIdTexture(source.SurfaceResId);
                _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            }
            if (texture == null)
            {
                _perf?.RecordSkippedLayer();
                continue;
            }
            var resolved = texture.Value;
            if (_gpuRenderer.DrawTexture(resolved.Image, resolved.AssetId, source.ColorKey,
                source.SrcX, source.SrcY, source.W, source.H, affine, source.Tint, source.TintStrength,
                opacity, source.MultiplyTint, resolved.IsDynamic,
                rawObject?.SourceSlot ?? source.Handle, source.Blend))
            {
                _perf?.RecordGpuLayer(source.W, source.H, affine, _screenWidth, _screenHeight,
                                      resolved.IsDynamic, source.Blend);
                drawn++;
            }
        }
        return drawn;
    }

    private void RecompositeSoftware(
        IReadOnlyList<RenderObject>? sampledVisible = null,
        bool preserveExistingPixels = false)
    {
        if (_perf != null)
        {
            var presentStep = _trace.LatestStep;
            _perf.RecordPresentationCoordinate(presentStep?.Script ?? "<startup>",
                                               presentStep?.Offset ?? -1, presentStep?.Opcode ?? -1);
        }
        long phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        bool hasScreenTransition = _host.TrySnapshotScreenTransition(out var transition);
        _perf?.RecordSnapshotAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        _perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);
        if (!hasScreenTransition && sampledVisible == null)
        {
            phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            preserveExistingPixels = _host.SnapshotBackbufferObjects(
                _vm.Gfx, _clock.NowMs, _visibleSnapshot).PreserveExistingPixels;
            _perf?.RecordSnapshotAllocation(
                PerformanceFrameLog.AllocatedBytes() - allocationPhase);
            _perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);
        }
        if (hasScreenTransition) preserveExistingPixels = false;
        _perf?.BeginRecomposite(hasScreenTransition);

        phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        if (!preserveExistingPixels)
        {
            System.Array.Clear(_screenPixels);
        }
        _perf?.RecordClear(PerformanceFrameLog.Timestamp() - phase);
        System.Collections.Generic.Dictionary<long, string>? decisions = _gfxLogPath != null || _timeline != null ? new() : null;
        if (hasScreenTransition)
        {
            allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            // Native mode 4 keeps the captured source opaque and alpha-composites the complete target
            // surface over it. Each offscreen target has an opaque-black clear beneath its objects.
            CompositeVisibleObjects(transition.Source, 1f, decisions);
            FillQuad(0, 0, _screenWidth, _screenHeight, 0, (float)transition.Progress);
            CompositeVisibleObjects(transition.Target, (float)transition.Progress, decisions);
            _perf?.RecordCompositeAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        }
        else
        {
            allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            CompositeVisibleObjects(sampledVisible ?? _visibleSnapshot, 1f, decisions);
            _perf?.RecordCompositeAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        }
        phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        _screen.SetData(_screenWidth, _screenHeight, false, Image.Format.Rgba8, _screenPixels);
        _perf?.RecordSetDataAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        _perf?.RecordSetData(PerformanceFrameLog.Timestamp() - phase);
        phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        _screenTex.Update(_screen);
        _perf?.RecordTextureUpdate(PerformanceFrameLog.Timestamp() - phase);
        if (decisions != null) LogGfxDecisionChanges(decisions);
        _perf?.EndRecomposite();
    }

    private void CompositeVisibleObjects(
        IReadOnlyList<RenderObject> visible,
        float globalOpacity,
        System.Collections.Generic.Dictionary<long, string>? decisions)
    {
        int z = 0;
        foreach (var v in visible)   // interpolate at the retained-presentation clock
        {
            _perf?.RecordObject(v.TimeVarying);
            var t = v.Transform;
            var affine = Age.Engine.Model.Transform2DMath.Build(t, v.Rotation, v.ScaleCycle);
            var localToDest = affine.FromLocalOrigin(v.DstX, v.DstY);
            if (v.RangeTransform is { } rangeTransform)
                localToDest = localToDest.Then(rangeTransform);
            var projected = localToDest.Apply(0, 0);
            int dstX = (int)System.Math.Round(projected.X);
            int dstY = (int)System.Math.Round(projected.Y);
            float opacity = v.Alpha / 255f * globalOpacity;  // transform Z is never opacity
            float strength = v.TintStrength / 255f;          // tint-blend / fill strength
            var rawObject = _vm.Gfx.TryGet(v.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var surfaceTexture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, v.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);
            // These strings exist only for --gfx-log/timeline diagnostics. DEBUGMAP visits roughly one
            // thousand retained objects per composition, so formatting them unconditionally creates
            // several megabytes of short-lived garbage even in an ordinary run.
            string? outcome = null;
            if (v.SurfaceTransition is { } transition)
            {
                _perf?.RecordTransitionLayer();
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
                if (v.Blend != Age.Engine.Model.BlendKind.Opaque)
                {
                    int baseW = v.W > 0 ? v.W : _screenWidth;
                    int baseH = v.H > 0 ? v.H : _screenHeight;
                    // One-shot/mode-1 packed color supplies opacity directly. Static mode-0 fills retain
                    // the tint-strength convention used by the existing effect objects.
                    float fillA = v.MultiplyTint ? opacity : opacity * strength;
                    _perf?.RecordFillLayer();
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
                    _perf?.RecordSkippedLayer();
                    if (decisions != null) outcome = "SKIP(no-resId, opaque render-target)";
                }
            }
            else
            {
                var texture = surfaceTexture;
                if (texture == null && !movieSurfaceBound)
                {
                    resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = _host.ResolveResIdTexture(v.SurfaceResId);
                    _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null)
                {
                    _perf?.RecordSkippedLayer();
                    if (decisions != null) outcome = $"SKIP(resId=0x{v.SurfaceResId:x} UNRESOLVED)";
                }
                else
                {
                    BlitLayer(texture.Value.Image, texture.Value.AssetId, v.ColorKey, v.Tint, strength, v.SrcX, v.SrcY, v.W, v.H,
                              localToDest, opacity, v.MultiplyTint, texture.Value.IsDynamic, v.Blend);
                    if (decisions != null)
                        outcome = $"slot={rawObject?.SourceSlot} DRAWN resId=0x{v.SurfaceResId:x} {texture.Value.Name} " +
                                  $"src=({v.SrcX},{v.SrcY} {v.W}x{v.H}) base=({v.DstX},{v.DstY}) " +
                                  $"anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) dst=({dstX},{dstY}) " +
                                  $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) " +
                                  $"rot=({t.RotationAngleDegrees:0.0}+{v.Rotation.AngleDegrees:0.0}) " +
                                  $"mode={rawObject?.StaticColorMode} op={opacity:0.00} tintStr={strength:0.00}" +
                                  ColorTimeline(v.ColorTransition);
                }
            }
            if (decisions != null) decisions[v.Handle] = $"z{z} {outcome}";
            z++;
        }
    }

    private static string ColorTimeline(Age.Engine.Model.ColorTransitionState? state)
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
            if (source.Handle < transition.RangeBStart || source.Handle >= end || source.SurfaceTransition != null)
                continue;
            _perf?.RecordObject(source.TimeVarying);
            var affine = Transform2DMath.Build(source.Transform, source.Rotation, source.ScaleCycle)
                .FromLocalOrigin(source.DstX, source.DstY);
            if (source.RangeTransform is { } rangeTransform)
                affine = affine.Then(rangeTransform);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            var rawObject = _vm.Gfx.TryGet(source.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawObject != null
                ? _host.ResolveSurfaceTexture(rawObject.SourceSlot, source.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawObject != null && _host.IsMovieSurfaceBound(rawObject.SourceSlot);
            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque)
                {
                    _perf?.RecordSkippedLayer();
                    continue;
                }
                int w = source.W > 0 ? source.W : _screenWidth;
                int h = source.H > 0 ? source.H : _screenHeight;
                _perf?.RecordFillLayer();
                FillAffineQuad(w, h, affine, source.Tint, opacity * source.TintStrength / 255f);
            }
            else
            {
                if (!movieSurfaceBound && texture == null)
                {
                    resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = _host.ResolveResIdTexture(source.SurfaceResId);
                    _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null)
                {
                    _perf?.RecordSkippedLayer();
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
            _gfxLog.WriteLine($"[frame {_gfxLogFrame} nowMs={_clock.NowMs} page={_pageCount}] {curr.Count} visible, {lines.Count} changes:");
            foreach (var l in lines) _gfxLog.WriteLine(l);
        }
        if (lines.Count > 0)
            _timeline?.Event("objects", new() { ["visible_count"] = curr.Count, ["changes"] = lines.ToArray() });
        _lastGfxDecision.Clear();
        foreach (var kv in curr) _lastGfxDecision[kv.Key] = kv.Value;
    }

    // Blit one object's surface rect. Static source pixels are cached per (assetId, colorKey): on first use, texels
    // matching the surface colorkey are made transparent (native bakes the key at load — engine-re.md §Blend).
    // Mode 0 uses tintStrength to LERP texel RGB toward tint. Mode 1 uses packed RGB modulation and
    // SRCALPHA/ONE additive composition; black source pixels therefore contribute nothing.
    private void BlitLayer(RgbaImage decoded, int assetId, long colorKey, long tint, float tintStrength, int srcX, int srcY, int w, int h,
                           Age.Engine.Model.Affine2D localToDest, float alpha = 1f, bool multiplyTint = false,
                           bool dynamic = false, BlendKind blend = BlendKind.Alpha)
    {
        // Native gfx_object_blit_d3d9 clips the explicit source rectangle and returns without drawing when
        // right<=left or bottom<=top. FIELD deliberately creates zero-area prototype objects from SO005;
        // expanding those dimensions to the full texture leaks the entire spritesheet onto the map.
        if (w <= 0 || h <= 0) return;
        long sourcePrepStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long sourcePrepAllocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
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
            if (Age.Engine.Model.BlendMath.HasColorKey(colorKey))
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
                if (Age.Engine.Model.BlendMath.HasColorKey(colorKey))
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
        _perf?.RecordSourcePrep(PerformanceFrameLog.Timestamp() - sourcePrepStarted);
        _perf?.RecordSourcePrepAllocation(PerformanceFrameLog.AllocatedBytes() - sourcePrepAllocated);
        if (sw <= 0 || sh <= 0) return;
        long rasterStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        Age.Engine.Model.SoftwareAffineRasterizer.BlitRgba(
            _screenPixels, _screenWidth, _screenHeight, sourcePixels, sourceWidth, sourceHeight,
            srcX, srcY, sw, sh, localToDest, tint, tintStrength, alpha, multiplyTint, blend);
        _perf?.RecordRaster(sw, sh, localToDest, _screenWidth, _screenHeight, dynamic, blend,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    private void FillAffineQuad(int w, int h, Age.Engine.Model.Affine2D localToDest, long tint, float alpha)
    {
        long rasterStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        Age.Engine.Model.SoftwareAffineRasterizer.FillRgba(
            _screenPixels, _screenWidth, _screenHeight, w, h, localToDest, tint, alpha);
        _perf?.RecordRaster(w, h, localToDest, _screenWidth, _screenHeight, false, BlendKind.Alpha,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    // Alpha-blend a solid tint (0xRRGGBB) rectangle over the screen — the surfaceless fade/flash fill.
    private void FillQuad(int dstX, int dstY, int w, int h, long tint, float alpha)
    {
        int ia = (int)(System.Math.Clamp(alpha, 0f, 1f) * 255);
        if (ia == 0) return;
        long rasterStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        int tr = (int)((tint >> 16) & 0xff), tg = (int)((tint >> 8) & 0xff), tb = (int)(tint & 0xff);
        byte[] dst = _screenPixels;
        int dw = _screenWidth, dh = _screenHeight;
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
        _perf?.RecordFillLayer();
        _perf?.RecordRaster(w, h, new Affine2D(1, 0, 0, 1, dstX, dstY),
                            _screenWidth, _screenHeight, false, BlendKind.Alpha,
                            PerformanceFrameLog.Timestamp() - rasterStarted);
    }

    // Make colorkey-matching texels transparent (native colorkey is baked at surface load).
    private static void BakeColorKey(byte[] px, long colorKey)
    {
        for (int i = 0; i < px.Length; i += 4)
            if (Age.Engine.Model.BlendMath.ColorKeyMatches(px[i], px[i + 1], px[i + 2], colorKey))
                px[i + 3] = 0;
    }

}
