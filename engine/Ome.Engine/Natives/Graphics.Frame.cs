using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

// The frame lowering (engine layering spec §6.3, part 1): the visible objects, sampled once, become one frame
// description that every renderer draws. This is the only traversal of the object model for drawing: effects are
// lowered here (a surface transition draws its target range at the sampled progress, a blur draws its samples), and
// a render-target publication lowers its range here too.
public sealed partial class Graphics
{
    // The engine thread's, in a frame turn. Two descriptions take turns, each with its screen transition, so the front
    // end draws one while the next is lowered into the other; the snapshot they are lowered from is reused.
    private readonly FrameDescription?[] _frames = new FrameDescription?[2];
    private readonly FrameScreenTransition[] _frameScreenTransitions = [new(), new()];
    private int _nextFrame;
    private readonly List<RenderObject> _frameObjects = new(1024);
    private readonly BlurRangeSources _blurSources = new();

    /// <summary>
    /// Lower the frame to present at <paramref name="nowMs"/>: the pending screen transition, or the backbuffer's
    /// published objects and whether they land over the current pixels. The description is valid until the call after
    /// next: the waits lower it in a frame turn, and the front end draws it while the next is lowered into the other.
    /// </summary>
    /// <param name="perf"><c>--perf-log</c>'s counters: snapshot, object visits, layers and resolve times. The caller
    /// starts the frame's record and ends it after drawing; this begins the recomposition.</param>
    /// <param name="decisions">When given, each object's outcome by handle, the text <c>--gfx-log</c> and the
    /// timeline's <c>objects</c> event report.</param>
    public FrameDescription LowerFrame(long nowMs, PerformanceFrameLog? perf = null,
                                       Dictionary<long, string>? decisions = null)
    {
        // The images this frame resolves stay in the decoded-image cache past its budget; it trims at the end.
        _images.BeginFrame();
        int buffer = _nextFrame;
        _nextFrame ^= 1;
        FrameDescription frame = _frames[buffer] ??= new FrameDescription { Width = _screenWidth, Height = _screenHeight };
        frame.Items.Clear();
        frame.ScreenTransition = null;

        long phase = perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocated = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        bool hasScreenTransition = TrySnapshotScreenTransition(nowMs, out var transition);
        perf?.RecordSnapshotAllocation(PerformanceFrameLog.AllocatedBytes() - allocated);
        perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);
        BackbufferPublicationPolicy policy = default;
        if (!hasScreenTransition)
        {
            phase = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            allocated = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            policy = SnapshotBackbufferObjects(nowMs, _frameObjects);
            perf?.RecordSnapshotAllocation(PerformanceFrameLog.AllocatedBytes() - allocated);
            perf?.RecordSnapshot(PerformanceFrameLog.Timestamp() - phase);
        }
        perf?.BeginRecomposite(hasScreenTransition);

        frame.BackgroundRgb = State.DisplayBackgroundColor;
        frame.PreserveExistingPixels = policy.PreserveExistingPixels;
        frame.AppendLayers = policy.AppendGpuLayers;
        allocated = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        if (hasScreenTransition)
        {
            // Native mode 4 keeps the captured source opaque and alpha-composites the complete target surface over
            // it. The target is one flattened surface: its objects drawn opaquely over the surface's clear (black, or
            // the overlay colour for the white fade). Fading each object on its own instead dims the whole frame
            // mid-transition and lets the target's layers show through each other.
            FrameScreenTransition screen = _frameScreenTransitions[buffer];
            screen.Source.Clear();
            screen.Target.Clear();
            screen.Reveal.Clear();
            screen.Patterned = false;
            screen.MosaicDot = 0;
            screen.OverlayRgb = transition.OverlayRgb;
            if (transition.Mosaic)
            {
                // Op 0x36: one of the two surfaces alone, in blocks. Before the first period nothing has been drawn
                // yet, and the port shows the source as it is; it does the same for the dot of 0 MosaicTexture
                // refuses on the first period.
                (bool showsTarget, int dot) = transition.Periods <= 0
                    ? (false, 1) : LegacyScreenTransitionTiming.MosaicAt(transition.Periods);
                LowerObjects(showsTarget ? transition.Target : transition.Source, screen.Source, perf, decisions);
                screen.Alpha = 0;
                screen.MosaicDot = dot;
            }
            else
            {
                LowerObjects(transition.Source, screen.Source, perf, decisions);
                if (transition.Pattern is { } pattern)
                {
                    // Ops 0x26 to 0x28: the target shows inside the rectangles the tick has drawn so far.
                    LegacyPatternReveal.Rects(_screenWidth, _screenHeight, pattern.Divisions, pattern.Mode,
                                              transition.Periods, screen.Reveal);
                    screen.Patterned = true;
                    screen.Alpha = screen.Reveal.Count == 0 ? 0 : 256;
                }
                else screen.Alpha = LegacyScreenTransitionComposite.Alpha(transition.Progress);
                if (screen.Alpha != 0) LowerObjects(transition.Target, screen.Target, perf, decisions);
            }
            frame.ScreenTransition = screen;
        }
        else LowerObjects(_frameObjects, frame.Items, perf, decisions);
        perf?.RecordCompositeAllocation(PerformanceFrameLog.AllocatedBytes() - allocated);
        _images.Trim();
        return frame;
    }

    // The objects in z-order (ascending handle). Native scale and translation channels are already sampled; an
    // object's opacity comes only from its blend and colour. The decision strings exist only for --gfx-log and the
    // timeline: DEBUGMAP visits about a thousand objects per frame, so they are formatted only when asked for.
    private void LowerObjects(IReadOnlyList<RenderObject> visible, List<FrameItem> items,
                              PerformanceFrameLog? perf, Dictionary<long, string>? decisions)
    {
        long started = perf != null ? PerformanceFrameLog.Timestamp() : 0;
        long allocated = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
        _blurSources.Collect(visible);
        perf?.RecordGpuBlurScan(PerformanceFrameLog.Timestamp() - started,
            PerformanceFrameLog.AllocatedBytes() - allocated);
        int z = 0;
        for (int index = 0; index < visible.Count; index++)
        {
            RenderObject v = visible[index];
            perf?.RecordObject(v.TimeVarying);
            if (v.Polygon is { } polygon)
            {
                // A polygon in a blur's source range is drawn here, unblurred (docs/backlog.md).
                bool drew = LowerPolygon(polygon, 1f, items);
                if (!drew) perf?.RecordSkippedLayer();
                if (decisions != null)
                    decisions[v.Handle] = $"z{z} POLYGON triangles={polygon.Geometry.Vertices.Length / 3} " +
                                          $"blend={polygon.Geometry.Blend} textures=({polygon.Texture0.Slot}," +
                                          $"{polygon.Texture1.Slot}){(drew ? "" : " SKIP(no triangles)")}";
                z++;
                continue;
            }
            started = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            allocated = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            bool blurSource = v.RadialBlurTransition == null && v.DirectionalBlurTransition == null &&
                              _blurSources.Contains(v.Handle);
            perf?.RecordGpuBlurScan(PerformanceFrameLog.Timestamp() - started,
                PerformanceFrameLog.AllocatedBytes() - allocated);
            if (blurSource)
            {
                perf?.RecordSkippedLayer();
                if (decisions != null) decisions[v.Handle] = "SKIP(blur source range)";
                continue;
            }
            started = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            allocated = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            Affine2D matrix = ObjectMatrix(v);
            perf?.RecordGpuTransform(PerformanceFrameLog.Timestamp() - started,
                PerformanceFrameLog.AllocatedBytes() - allocated);
            float opacity = v.Alpha / 255f;           // transform Z is never opacity
            float strength = v.TintStrength / 255f;   // tint-blend / fill strength
            started = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            allocated = perf != null ? PerformanceFrameLog.AllocatedBytes() : 0;
            int? rawSlot = State.TryGetSourceSlot(v.Handle, out int slot) ? slot : null;
            perf?.RecordGpuObjectLookup(PerformanceFrameLog.Timestamp() - started,
                PerformanceFrameLog.AllocatedBytes() - allocated);
            long resolveStarted = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            SurfaceTexture? surfaceTexture = rawSlot != null
                ? ResolveSurfaceTexture(rawSlot.Value, v.SurfaceResId)
                : null;
            perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _movies.IsBound(rawSlot.Value);
            string? outcome = null;
            if (v.DirectionalBlurTransition is { } directionalBlur)
            {
                perf?.RecordTransitionLayer();
                int layers = LowerBlurRange(visible, directionalBlur, null, items, perf);
                if (decisions != null)
                    outcome = $"DIRECTIONAL-BLUR key=0x{directionalBlur.CommandKey:x} " +
                              $"angle={directionalBlur.AngleDegrees:0.0} " +
                              $"length={directionalBlur.Length:0.0} " +
                              $"progress={directionalBlur.Progress:0.000} layers={layers}";
            }
            else if (v.RadialBlurTransition is { } radialBlur)
            {
                perf?.RecordTransitionLayer();
                int layers = LowerBlurRange(visible, null, radialBlur, items, perf);
                if (decisions != null)
                    outcome = $"RADIAL-BLUR key=0x{radialBlur.CommandKey:x} " +
                              $"center=({radialBlur.CenterX:0.0},{radialBlur.CenterY:0.0}) " +
                              $"length={radialBlur.Length:0.0} progress={radialBlur.Progress:0.000} layers={layers}";
            }
            else if (v.PatternedSurfaceTransition is { } patterned)
            {
                // The portable contract keeps the native mask mode and thickness. No renderer has the pattern masks
                // yet, so the same timed ranges are drawn as a crossfade.
                perf?.RecordTransitionLayer();
                int layers = LowerTransitionRange(visible, CrossfadeApproximation(patterned), items, perf);
                if (decisions != null)
                    outcome = $"PATTERN-TRANSITION slot={patterned.TargetSlot} " +
                              $"key=0x{patterned.CommandKey:x} mode={(int)patterned.Mode} " +
                              $"thickness={patterned.PatternThickness} progress={patterned.Progress:0.000} " +
                              $"forced={patterned.Forced} layers={layers}";
            }
            else if (v.SurfaceTransition is { } transition)
            {
                perf?.RecordTransitionLayer();
                int layers = LowerTransitionRange(visible, transition, items, perf);
                if (decisions != null)
                    outcome = $"TRANSITION slot={transition.TargetSlot} key=0x{transition.CommandKey:x} " +
                              $"progress={transition.Progress:0.000} forced={transition.Forced} layers={layers}";
            }
            else if (v.SurfaceResId == 0 && surfaceTexture == null)
            {
                // A coloured object with no bound surface is a fade or flash fill (fade-to-black, say). Its presence
                // is the tint strength (0 = absent, 255 = solid), scaled by the object's opacity; a one-shot or
                // mode-1 packed colour gives the opacity directly. An uncoloured surfaceless object is a render
                // target, and draws nothing.
                if (v.Blend != BlendKind.Opaque)
                {
                    int width = v.W > 0 ? v.W : _screenWidth;
                    int height = v.H > 0 ? v.H : _screenHeight;
                    float fillOpacity = v.MultiplyTint ? opacity : opacity * strength;
                    perf?.RecordFillLayer();
                    items.Add(FrameItem.Fill(width, height, matrix, v.Tint, fillOpacity));
                    if (decisions != null)
                    {
                        var t = v.Transform;
                        var (dstX, dstY) = Projected(matrix);
                        outcome = $"FILL tint=0x{v.Tint:x6} a={fillOpacity:0.00} {width}x{height}@({dstX},{dstY}) " +
                                  $"base=({v.DstX},{v.DstY}) anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) " +
                                  $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) " +
                                  $"trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) rot={v.Rotation.AngleDegrees:0.0}" +
                                  ColorTimeline(v.ColorTransition);
                    }
                }
                else
                {
                    perf?.RecordSkippedLayer();
                    if (decisions != null) outcome = "SKIP(no-resId, opaque render-target)";
                }
            }
            else
            {
                SurfaceTexture? texture = surfaceTexture;
                if (texture == null && !movieSurfaceBound)
                {
                    resolveStarted = perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = ResolveResIdTexture(v.SurfaceResId);
                    perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null)
                {
                    perf?.RecordSkippedLayer();
                    if (decisions != null) outcome = $"SKIP(resId=0x{v.SurfaceResId:x} UNRESOLVED)";
                }
                else
                {
                    items.Add(TextureItem(v, texture.Value, rawSlot, matrix, opacity, v.Blend));
                    if (decisions != null)
                    {
                        var t = v.Transform;
                        var (dstX, dstY) = Projected(matrix);
                        outcome = $"slot={rawSlot} DRAWN resId=0x{v.SurfaceResId:x} {texture.Value.Name} " +
                                  $"src=({v.SrcX},{v.SrcY} {v.W}x{v.H}) base=({v.DstX},{v.DstY}) " +
                                  $"anchor=({t.AnchorX:0.0},{t.AnchorY:0.0}) dst=({dstX},{dstY}) " +
                                  $"scale=({t.ScaleX:0.00},{t.ScaleY:0.00}) trans=({t.TranslateX:0.0},{t.TranslateY:0.0}) " +
                                  $"rot=({t.RotationAngleDegrees:0.0}+{v.Rotation.AngleDegrees:0.0}) " +
                                  $"mode={State.TryGet(v.Handle)?.StaticColorMode} op={opacity:0.00} tintStr={strength:0.00}" +
                                  ColorTimeline(v.ColorTransition);
                    }
                }
            }
            if (decisions != null) decisions[v.Handle] = $"z{z} {outcome}";
            z++;
        }
    }

    // Native type-0 surface commands first leave range A in normal z-order, then alpha-composite range B into the
    // target surface. SC0000 binds that target to handle+2, above both source handles, so drawing range B here with
    // progress produces old*(1-progress)+new*progress without disturbing ambient channels.
    private int LowerTransitionRange(IReadOnlyList<RenderObject> visible, SurfaceTransitionState transition,
                                     List<FrameItem> items, PerformanceFrameLog? perf)
    {
        int lowered = 0;
        long end = transition.RangeBStart + transition.RangeBCount;
        for (int index = 0; index < visible.Count; index++)
        {
            RenderObject source = visible[index];
            if (source.Handle < transition.RangeBStart || source.Handle >= end ||
                source.SurfaceTransition != null || source.PatternedSurfaceTransition != null)
                continue;
            perf?.RecordObject(source.TimeVarying);
            if (source.Polygon is { } polygon)
            {
                // Native's pre-pass draws a range's polygons into the transition's surface with its objects.
                if (LowerPolygon(polygon, (float)transition.Progress, items)) lowered++;
                continue;
            }
            Affine2D matrix = ObjectMatrix(source);
            float opacity = source.Alpha / 255f * (float)transition.Progress;
            int? rawSlot = State.TryGetSourceSlot(source.Handle, out int slot) ? slot : null;
            long resolveStarted = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            SurfaceTexture? texture = rawSlot != null
                ? ResolveSurfaceTexture(rawSlot.Value, source.SurfaceResId)
                : null;
            perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _movies.IsBound(rawSlot.Value);
            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque)
                {
                    perf?.RecordSkippedLayer();
                    continue;
                }
                int width = source.W > 0 ? source.W : _screenWidth;
                int height = source.H > 0 ? source.H : _screenHeight;
                perf?.RecordFillLayer();
                items.Add(FrameItem.Fill(width, height, matrix, source.Tint, opacity * source.TintStrength / 255f));
            }
            else
            {
                if (!movieSurfaceBound && texture == null)
                {
                    resolveStarted = perf != null ? PerformanceFrameLog.Timestamp() : 0;
                    texture = ResolveResIdTexture(source.SurfaceResId);
                    perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
                }
                if (texture == null)
                {
                    perf?.RecordSkippedLayer();
                    continue;
                }
                items.Add(TextureItem(source, texture.Value, rawSlot, matrix, opacity, source.Blend));
            }
            lowered++;
        }
        return lowered;
    }

    /// <summary>A patterned transition drawn as a crossfade over the same ranges and timing: no renderer has the
    /// pattern masks yet.</summary>
    private static SurfaceTransitionState CrossfadeApproximation(PatternedSurfaceTransitionState transition)
        => new(transition.CommandKey, transition.TargetSlot,
               transition.RangeAStart, transition.RangeACount,
               transition.RangeBStart, transition.RangeBCount,
               transition.DelayMs, transition.DurationMs, transition.StartMs,
               transition.Progress, transition.Forced);

    // SYS4433's mode-0/1 shaders consume Angle/Length or CenterU/CenterV/Length. We have no copy of those D3D9
    // effects, so a blur is six alpha-weighted translated or centre-zoomed samples of its range; length zero takes
    // the exact one-sample path native draws at the terminal frame.
    private int LowerBlurRange(IReadOnlyList<RenderObject> visible,
                               DirectionalBlurRangeTransitionState? directional,
                               RadialBlurRangeTransitionState? radial,
                               List<FrameItem> items, PerformanceFrameLog? perf)
    {
        double length = directional?.Length ?? radial!.Value.Length;
        int samples = System.Math.Abs(length) < 0.5 ? 1 : 6;
        int lowered = 0;
        for (int index = 0; index < visible.Count; index++)
        {
            RenderObject source = visible[index];
            bool contains = directional?.Contains(source.Handle) ?? radial!.Value.Contains(source.Handle);
            if (!contains || source.DirectionalBlurTransition != null || source.RadialBlurTransition != null
                || source.Polygon != null)
                continue;
            perf?.RecordObject(source.TimeVarying);
            Affine2D baseMatrix = ObjectMatrix(source);
            int? rawSlot = State.TryGetSourceSlot(source.Handle, out int slot) ? slot : null;
            long resolveStarted = perf != null ? PerformanceFrameLog.Timestamp() : 0;
            SurfaceTexture? texture = rawSlot != null
                ? ResolveSurfaceTexture(rawSlot.Value, source.SurfaceResId)
                : null;
            perf?.RecordResolve(PerformanceFrameLog.Timestamp() - resolveStarted);
            bool movieSurfaceBound = rawSlot != null && _movies.IsBound(rawSlot.Value);
            if (source.SurfaceResId == 0 && texture == null)
            {
                if (source.Blend == BlendKind.Opaque) { perf?.RecordSkippedLayer(); continue; }
                int width = source.W > 0 ? source.W : _screenWidth;
                int height = source.H > 0 ? source.H : _screenHeight;
                for (int sample = 0; sample < samples; sample++)
                {
                    perf?.RecordFillLayer();
                    items.Add(FrameItem.Fill(
                        width, height, BlurSampleMatrix(baseMatrix, directional, radial, sample, samples),
                        source.Tint, source.Alpha / 255f * source.TintStrength / 255f / samples));
                    lowered++;
                }
                continue;
            }
            if (!movieSurfaceBound && texture == null)
                texture = ResolveResIdTexture(source.SurfaceResId);
            if (texture == null) { perf?.RecordSkippedLayer(); continue; }

            for (int sample = 0; sample < samples; sample++)
            {
                items.Add(TextureItem(source, texture.Value, rawSlot,
                    BlurSampleMatrix(baseMatrix, directional, radial, sample, samples),
                    source.Alpha / 255f / samples, BlendKind.Alpha));
                lowered++;
            }
        }
        return lowered;
    }

    // One of a blur's samples: a translated copy along the angle (directional) or a zoom about the centre (radial).
    private Affine2D BlurSampleMatrix(Affine2D source, DirectionalBlurRangeTransitionState? directional,
                                      RadialBlurRangeTransitionState? radial, int sample, int sampleCount)
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
        double scale = 1.0 + effect.Length / System.Math.Max(1, _screenWidth) * fraction;
        var zoom = new Affine2D(scale, 0, 0, scale,
            effect.CenterX * (1 - scale), effect.CenterY * (1 - scale));
        return source.Then(zoom);
    }

    // ---- a published range (ops 0x222 and 0x20c into a render target, and a movie mask's capture) ----

    // A published range draws its objects plainly: each object in the range as its texture or its fill, with no
    // effects (a transition or blur on an object in the range is the backbuffer's) and fills sized to the
    // destination. An object whose source rectangle starts off its texture, or misses it, draws nothing. Returns how
    // many objects drew.
    private int LowerPublishedRange(IReadOnlyList<RenderObject> visible, long firstHandle, long count,
                                    int width, int height, List<FrameItem> items)
    {
        if (count <= 0) return 0;
        int lowered = 0;
        for (int index = 0; index < visible.Count; index++)
        {
            RenderObject item = visible[index];
            if (item.Handle < firstHandle || item.Handle - firstHandle >= count) continue;
            if (item.Polygon is { } polygon)
            {
                if (LowerPolygon(polygon, 1f, items)) lowered++;
                continue;
            }
            Affine2D matrix = ObjectMatrix(item);
            int? rawSlot = State.TryGetSourceSlot(item.Handle, out int slot) ? slot : null;
            SurfaceTexture? texture = rawSlot != null
                ? ResolveSurfaceTexture(rawSlot.Value, item.SurfaceResId)
                : ResolveResIdTexture(item.SurfaceResId);
            float opacity = item.Alpha / 255f;
            if (texture == null)
            {
                if (item.SurfaceResId != 0 || item.Blend == BlendKind.Opaque) continue;
                int fillWidth = item.W > 0 ? item.W : width;
                int fillHeight = item.H > 0 ? item.H : height;
                float fillOpacity = item.MultiplyTint ? opacity : opacity * (item.TintStrength / 255f);
                items.Add(FrameItem.Fill(fillWidth, fillHeight, matrix, item.Tint, fillOpacity));
                lowered++;
                continue;
            }
            RgbaImage image = texture.Value.Image;
            if (item.W <= 0 || item.H <= 0 || item.SrcX < 0 || item.SrcY < 0
                || System.Math.Min(item.W, image.Width - item.SrcX) <= 0
                || System.Math.Min(item.H, image.Height - item.SrcY) <= 0)
                continue;
            items.Add(TextureItem(item, texture.Value, rawSlot, matrix, opacity, item.Blend));
            lowered++;
        }
        return lowered;
    }

    /// <summary>Draw the objects of <paramref name="visible"/> whose handles fall in the range into
    /// <paramref name="destination"/>, as a render-target publication does: lowered plainly (no effects, fills sized to
    /// the destination) and drawn by the reference renderer's rules. Returns how many objects drew.</summary>
    public int DrawObjectRange(RgbaImage destination, IReadOnlyList<RenderObject> visible, long firstHandle, long count)
    {
        if (destination.Width <= 0 || destination.Height <= 0
            || destination.Pixels.Length != checked(destination.Width * destination.Height * 4))
            return 0;
        var items = new List<FrameItem>();
        int lowered = LowerPublishedRange(visible, firstHandle, count, destination.Width, destination.Height, items);
        ReferenceRenderer.DrawItems(destination, items);
        return lowered;
    }

    // ---- shared ----

    // A polygon object as one item: its triangles, with the textures of the stages that name a surface. Native binds
    // what it finds in order, so a second stage's texture alone is the first. Returns false when it has no triangle.
    private bool LowerPolygon(PolygonRenderState polygon, float opacity, List<FrameItem> items)
    {
        if (polygon.Geometry.Vertices.Length == 0) return false;
        SurfaceTexture? first = PolygonTexture(polygon.Texture0), second = PolygonTexture(polygon.Texture1);
        PolygonTextureState firstState = polygon.Texture0, secondState = polygon.Texture1;
        if (first == null)
        {
            (first, firstState) = (second, secondState);
            second = null;
        }
        if (first is not { } texture)
        {
            items.Add(FrameItem.PolygonItem(polygon.Geometry, opacity));
            return true;
        }
        FramePolygonTexture? secondTexture = second is { } other
            ? new FramePolygonTexture(other.Image, other.AssetId, other.IsDynamic, secondState.Slot,
                                      secondState.ColorKey)
            : null;
        items.Add(FrameItem.PolygonItem(polygon.Geometry, opacity, texture.Image, texture.AssetId, texture.IsDynamic,
                                        firstState.Slot, firstState.ColorKey, secondTexture));
        return true;

        SurfaceTexture? PolygonTexture(PolygonTextureState state)
            => state.Slot < 0 ? null : ResolveSurfaceTexture(state.Slot, state.ResourceId);
    }

    private static Affine2D ObjectMatrix(in RenderObject item)
    {
        Affine2D matrix = Transform2DMath.Build(item.Transform, item.Rotation, item.ScaleCycle, item.TranslationCycle)
            .FromLocalOrigin(item.DstX, item.DstY);
        return item.RangeTransform is { } rangeTransform ? matrix.Then(rangeTransform) : matrix;
    }

    // A dynamic surface is cached by its slot (its pixels change by replacement); an object with no slot by its handle.
    private static FrameItem TextureItem(in RenderObject item, SurfaceTexture texture, int? rawSlot, Affine2D matrix,
                                         float opacity, BlendKind blend)
        => new(FrameItemKind.Texture, texture.Image, texture.AssetId, texture.IsDynamic, rawSlot ?? item.Handle,
               item.ColorKey, item.SrcX, item.SrcY, item.W, item.H, matrix, opacity, item.Tint, item.TintStrength,
               item.MultiplyTint, blend);

    private static (int X, int Y) Projected(Affine2D matrix)
    {
        var projected = matrix.Apply(0, 0);
        return ((int)System.Math.Round(projected.X), (int)System.Math.Round(projected.Y));
    }

    private static string ColorTimeline(ColorTransitionState? state)
        => state is { } c
            ? $" color=0x{c.Current:x8}->0x{c.Target:x8} colorProgress={c.Progress:0.000}"
            : "";
}
