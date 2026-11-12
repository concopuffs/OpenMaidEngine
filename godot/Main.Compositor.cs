using System.Collections.Generic;
using System.Linq;
using Godot;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;

public partial class Main
{
    private TextureRect _screenView = null!;              // shows the composited screen backbuffer
    private Image _screen = null!;                        // SYS4INI-sized immediate-mode canvas
    private ImageTexture _screenTex = null!;
    private GpuRetainedRenderer _gpuRenderer = null!;
    private bool _useGpuBackend = true;
    private readonly BlurRangeSources _blurSources = new();
    private SoftwareCompositor _compositor = null!;      // the software path (Ome.Engine), drawn into its own buffer

    private string? _gfxLogPath;                // --gfx-log <file>: log per-object compositor draw/skip CHANGES

    /// <summary>Drops the prepared pixels of every catalog image, so the software path takes each one from the host
    /// again: after a language switch, its language assets apply (localization spec §12). Main thread, as the
    /// compositor.</summary>
    private void ForgetPreparedPixels() => _compositor.ForgetPreparedPixels();
    private readonly System.Collections.Generic.List<RenderObject> _visibleSnapshot = new(1024);

    /// <summary>A retained object's live source slot, read under the gfx lock (the VM thread changes the object).</summary>
    private int? SourceSlotOf(long handle) => _vm.Gfx.TryGetSourceSlot(handle, out int slot) ? slot : null;

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
        if (_host.HasScreenTransition) return false; // P4: whole-screen offscreen targets, drawn by the software path
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
        _gpuRenderer.BeginFrame(publicationPolicy.AppendGpuLayers, _perf);
        long blurIndexStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long blurIndexAllocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        _blurSources.Collect(_visibleSnapshot);
        _perf?.RecordGpuBlurScan(PerformanceFrameLog.Timestamp() - blurIndexStarted,
            PerformanceFrameLog.AllocatedBytes() - blurIndexAllocated);
        foreach (var v in _visibleSnapshot)
        {
            _perf?.RecordObject(v.TimeVarying);
            long workStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            long workAllocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            bool blurSource = v.RadialBlurTransition == null && v.DirectionalBlurTransition == null &&
                _blurSources.Contains(v.Handle);
            _perf?.RecordGpuBlurScan(PerformanceFrameLog.Timestamp() - workStarted,
                PerformanceFrameLog.AllocatedBytes() - workAllocated);
            if (blurSource)
            {
                _perf?.RecordSkippedLayer();
                continue;
            }
            workStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            workAllocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            var affine = Transform2DMath.Build(v.Transform, v.Rotation, v.ScaleCycle, v.TranslationCycle)
                .FromLocalOrigin(v.DstX, v.DstY);
            if (v.RangeTransform is { } rangeTransform) affine = affine.Then(rangeTransform);
            _perf?.RecordGpuTransform(PerformanceFrameLog.Timestamp() - workStarted,
                PerformanceFrameLog.AllocatedBytes() - workAllocated);
            float opacity = v.Alpha / 255f;
            workStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            workAllocated = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            int? rawSlot = SourceSlotOf(v.Handle);
            _perf?.RecordGpuObjectLookup(PerformanceFrameLog.Timestamp() - workStarted,
                PerformanceFrameLog.AllocatedBytes() - workAllocated);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawSlot != null
                ? _host.ResolveSurfaceTexture(rawSlot.Value, v.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _host.IsMovieSurfaceBound(rawSlot.Value);

            if (v.DirectionalBlurTransition is { } directionalBlur)
            {
                _perf?.RecordTransitionLayer();
                DrawDirectionalBlurRangeGpu(_visibleSnapshot, directionalBlur);
            }
            else if (v.RadialBlurTransition is { } radialBlur)
            {
                _perf?.RecordTransitionLayer();
                DrawRadialBlurRangeGpu(_visibleSnapshot, radialBlur);
            }
            else if (v.PatternedSurfaceTransition is { } patterned)
            {
                // The portable contract retains the native mask mode/thickness. Until the GPU path has
                // clip-mask sprites, publish the same timed endpoints through a source-over crossfade.
                _perf?.RecordTransitionLayer();
                DrawTransitionRangeGpu(_visibleSnapshot, SoftwareCompositor.CrossfadeApproximation(patterned));
            }
            else if (v.SurfaceTransition is { } transition)
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
                        rawSlot ?? v.Handle, v.Blend);
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
            if (source.Handle < transition.RangeBStart || source.Handle >= end ||
                source.SurfaceTransition != null || source.PatternedSurfaceTransition != null)
                continue;
            _perf?.RecordObject(source.TimeVarying);
            var affine = Transform2DMath.Build(source.Transform, source.Rotation, source.ScaleCycle, source.TranslationCycle)
                .FromLocalOrigin(source.DstX, source.DstY);
            if (source.RangeTransform is { } rangeTransform) affine = affine.Then(rangeTransform);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            int? rawSlot = SourceSlotOf(source.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawSlot != null
                ? _host.ResolveSurfaceTexture(rawSlot.Value, source.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _host.IsMovieSurfaceBound(rawSlot.Value);

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
                rawSlot ?? source.Handle, source.Blend))
            {
                _perf?.RecordGpuLayer(source.W, source.H, affine, _screenWidth, _screenHeight,
                                      resolved.IsDynamic, source.Blend);
                drawn++;
            }
        }
        return drawn;
    }

    private int DrawDirectionalBlurRangeGpu(
        IReadOnlyList<RenderObject> visible, DirectionalBlurRangeTransitionState effect)
        => DrawBlurRangeGpu(visible, effect, null);

    // SYS4433's mode-0/1 shaders consume Angle/Length or CenterU/CenterV/Length. Godot has no copy of
    // those D3D9 effects, so approximate them with six alpha-weighted translated or center-zoom samples.
    // Length zero takes the exact one-sample path used at the native terminal frame.
    private int DrawRadialBlurRangeGpu(
        IReadOnlyList<RenderObject> visible, RadialBlurRangeTransitionState effect)
        => DrawBlurRangeGpu(visible, null, effect);

    private int DrawBlurRangeGpu(
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
            _perf?.RecordObject(source.TimeVarying);
            var baseAffine = Transform2DMath.Build(source.Transform, source.Rotation, source.ScaleCycle, source.TranslationCycle)
                .FromLocalOrigin(source.DstX, source.DstY);
            if (source.RangeTransform is { } rangeTransform)
                baseAffine = baseAffine.Then(rangeTransform);
            int? rawSlot = SourceSlotOf(source.Handle);
            long resolveStarted = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
            var texture = rawSlot != null
                ? _host.ResolveSurfaceTexture(rawSlot.Value, source.SurfaceResId)
                : null;
            _perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _host.IsMovieSurfaceBound(rawSlot.Value);
            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque) { _perf?.RecordSkippedLayer(); continue; }
                int width = source.W > 0 ? source.W : _screenWidth;
                int height = source.H > 0 ? source.H : _screenHeight;
                for (int sample = 0; sample < samples; sample++)
                {
                    var affine = SoftwareCompositor.BlurSampleAffine(baseAffine, directional, radial, sample, samples, _screenWidth);
                    _perf?.RecordFillLayer();
                    if (_gpuRenderer.DrawFill(
                        width, height, affine, source.Tint,
                        source.Alpha / 255f * source.TintStrength / 255f / samples))
                    {
                        _perf?.RecordGpuLayer(width, height, affine, _screenWidth, _screenHeight,
                                              dynamic: false, BlendKind.Alpha);
                        drawn++;
                    }
                }
                continue;
            }
            if (!movieSurfaceBound && texture == null)
                texture = _host.ResolveResIdTexture(source.SurfaceResId);
            if (texture == null) { _perf?.RecordSkippedLayer(); continue; }

            for (int sample = 0; sample < samples; sample++)
            {
                var affine = SoftwareCompositor.BlurSampleAffine(baseAffine, directional, radial, sample, samples, _screenWidth);
                var resolved = texture.Value;
                float opacity = source.Alpha / 255f / samples;
                if (_gpuRenderer.DrawTexture(
                    resolved.Image, resolved.AssetId, source.ColorKey,
                    source.SrcX, source.SrcY, source.W, source.H, affine,
                    source.Tint, source.TintStrength, opacity, source.MultiplyTint,
                    resolved.IsDynamic, rawSlot ?? source.Handle, BlendKind.Alpha))
                {
                    _perf?.RecordGpuLayer(source.W, source.H, affine, _screenWidth, _screenHeight,
                                          resolved.IsDynamic, BlendKind.Alpha);
                    drawn++;
                }
            }
        }
        return drawn;
    }


    // ---- the software path: SoftwareCompositor composes, Main uploads ----
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
        _compositor.Compose(sampledVisible, preserveExistingPixels, _pageCount);
        long phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocationPhase = _perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        _screen.SetData(_screenWidth, _screenHeight, false, Image.Format.Rgba8, _compositor.Pixels);
        _perf?.RecordSetDataAllocation(PerformanceFrameLog.AllocatedBytes() - allocationPhase);
        _perf?.RecordSetData(PerformanceFrameLog.Timestamp() - phase);
        phase = _perf != null ? PerformanceFrameLog.Timestamp() : 0;
        _screenTex.Update(_screen);
        _perf?.RecordTextureUpdate(PerformanceFrameLog.Timestamp() - phase);
        _perf?.EndRecomposite();
    }
}
