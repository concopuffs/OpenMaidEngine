using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>A whole-screen transition the native started: when it started and how long it runs, and what the
/// timeline records about it. The input and waits native blocks the engine thread until it is drawn.</summary>
public readonly record struct ScreenTransitionStart(long StartMs, long DurationMs, Dictionary<string, object?> Detail);

/// <summary>How a backbuffer publication lands: over the current pixels or on a clean frame, and whether a retained
/// renderer adds the items to its previous layers (a partial range, or an empty full redraw).</summary>
public readonly record struct BackbufferPublicationPolicy(bool PreserveExistingPixels, bool AppendGpuLayers);

/// <summary>The pending whole-screen transition at one instant: the source and target objects, the progress (0..1) and
/// the overlay colour the target is flattened over. A running pattern reveal carries its <see cref="Pattern"/> and a
/// running mosaic transition sets <see cref="Mosaic"/>, each with the timer <see cref="Periods"/> that have passed; at
/// its end, and when it is forced there, either is the target alone (progress 1).</summary>
public readonly record struct LegacyScreenTransitionSnapshot(
    IReadOnlyList<RenderObject> Source, IReadOnlyList<RenderObject> Target, double Progress, long OverlayRgb,
    SurfacePatternTransitionRequest? Pattern = null, long Periods = 0, bool Mosaic = false);

// Publication and the whole-screen transition: what op 0x222 and op 0x20c publish (a render target's captured objects
// and pixels, or the backbuffer's range and whether it keeps its pixels), and the 0x21/0x22/0x24/0x25 family's
// screen transition with its timing. The input and waits native's waits block on them.
public sealed partial class Graphics
{
    // surface slot -> the objects op 0x222/0x20c last published into it; the screen transitions draw from them.
    private readonly Dictionary<int, IReadOnlyList<RenderObject>> _renderTargetSnapshots = new();

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5).
    private sealed class Scene
    {
        public ScreenTransition? ScreenTransition;
        public GfxHandleRange BackbufferRange = GfxHandleRange.All;
        public bool BackbufferPublicationPreservesExistingPixels;
        public bool BackbufferClearPending;
    }

    private Scene _scene = new();

    // Op 0x252's flags (AGE 4.38D). Native's graphics manager sets the field to 0 when it is built and only the
    // opcode writes it, so the scene reset leaves it.
    private int _presentClear;
    private const int ClearBeforeFrameRender = 1, ClearBeforeRangePresent = 2;

    private readonly List<RenderObject> _terminalSample = new();   // engine thread only: CommitTerminalSample

    // ---- publication (ops 0x222 and 0x20c) ----

    /// <summary>Op 0x222. A selected render target draws the range into its surface at once; the backbuffer records
    /// the range, drawn over the current pixels, and returns true: native ends its scene and presents before the op
    /// returns, so the caller presents.</summary>
    public bool PresentObjectRange(long firstHandle, long count)
    {
        if ((_presentClear & ClearBeforeRangePresent) != 0) ClearRenderTarget();
        if (State.CurrentRenderTargetSlot >= 0)
        {
            PublishObjectRangeToSurface(firstHandle, count);
            return false;
        }
        _scene.BackbufferRange = new GfxHandleRange(firstHandle, count);
        _scene.BackbufferPublicationPreservesExistingPixels = true;
        _timeline?.Event("present-object-range", new() { ["first"] = firstHandle, ["count"] = count });
        return true;
    }

    /// <summary>Op 0x20c. A selected render target draws every visible object into its surface and keeps them as its
    /// captured objects. The backbuffer draws every visible object over the current pixels (gfx_render_frame does not
    /// clear; op 0x20e does), after the queued foreground effects start and complete, and returns true: the caller
    /// presents.</summary>
    public bool PresentFrame()
    {
        if ((_presentClear & ClearBeforeFrameRender) != 0) ClearRenderTarget();
        if (State.CurrentRenderTargetSlot >= 0)
        {
            int slot = State.CurrentRenderTargetSlot;
            var snapshot = State.SnapshotVisibleObjects(_clock.NowMs);
            PublishObjectRangeToSurface(0, long.MaxValue, snapshot);
            _renderTargetSnapshots[slot] = snapshot;
            _timeline?.Event("render-target-snapshot", new()
            {
                ["surface"] = slot, ["objects"] = snapshot.Count,
            });
            return false;
        }
        _scene.BackbufferRange = GfxHandleRange.All;
        _scene.BackbufferPublicationPreservesExistingPixels = true;
        long nowMs = _clock.NowMs;
        int started = State.StartForegroundTransitions(nowMs);
        State.SeedOneShotChannels(nowMs);   // gfx_render_frame samples, so seeds, every visible channel
        int completed = State.CompleteForegroundTransitions(nowMs);
        if (started > 0 || completed > 0)
            _timeline?.State("transition-skip-complete", new()
            {
                ["started"] = started, ["completed"] = completed,
            });
        return true;
    }

    /// <summary>Op 0x252 (AGE 4.38D): whether a present clears its render target to black before it draws, as op
    /// 0x20e does. Bit 0 is the frame render's (gfx_render_frame: op 0x20c, and every frame the engine draws by
    /// itself); bit 1 is a range present's (gfx_present_object_range: op 0x222).</summary>
    public void SetPresentClear(int flags) => _presentClear = flags;

    /// <summary>Native renders the terminal sample in the tick that ends an effect wait, and a render commits every
    /// channel it samples past its end: sample once on the engine thread, so the next burst starts from the committed
    /// endpoints.</summary>
    public void CommitTerminalSample()
    {
        State.SnapshotVisibleObjects(_clock.NowMs, _terminalSample);
        _terminalSample.Clear();
        RequestPresentation();
    }

    /// <summary>The visible objects the backbuffer publishes now, in <paramref name="snapshot"/>, and how they land:
    /// over the current pixels or a clean frame. Taking it consumes a pending op 0x20e clear.</summary>
    public BackbufferPublicationPolicy SnapshotBackbufferObjects(long nowMs, List<RenderObject> snapshot)
    {
        State.SnapshotVisibleObjects(nowMs, snapshot);
        GfxHandleRange range;
        bool preserveExistingPixels;
        range = _scene.BackbufferRange;
        preserveExistingPixels =
            _scene.BackbufferPublicationPreservesExistingPixels && !_scene.BackbufferClearPending
            && (_presentClear & ClearBeforeFrameRender) == 0;
        _scene.BackbufferClearPending = false;
        int write = 0;
        for (int read = 0; read < snapshot.Count; read++)
            if (range.Contains(snapshot[read].Handle)) snapshot[write++] = snapshot[read];
        if (write < snapshot.Count) snapshot.RemoveRange(write, snapshot.Count - write);
        return ResolveBackbufferPublicationPolicy(preserveExistingPixels, range, snapshot.Count);
    }

    /// <summary>Native preservation is a pixel rule, not a history of layers a retained renderer keeps: a partial
    /// range overlays, a nonempty full redraw replaces the layers, and an empty full redraw leaves them.</summary>
    public static BackbufferPublicationPolicy ResolveBackbufferPublicationPolicy(
        bool preserveExistingPixels, GfxHandleRange range, int visibleObjectCount)
    {
        bool appendGpuLayers = preserveExistingPixels
                               && (range != GfxHandleRange.All || visibleObjectCount == 0);
        return new BackbufferPublicationPolicy(preserveExistingPixels, appendGpuLayers);
    }

    /// <summary>Draw the visible objects of a handle range into a fresh image of surface
    /// <paramref name="targetSlot"/>'s size (the screen's when it has none): a movie mask transition's capture.</summary>
    public RgbaImage CaptureObjectRange(int targetSlot, long firstHandle, int count)
    {
        var dimensions = _slotDims.GetValueOrDefault(targetSlot, (W: _screenWidth, H: _screenHeight));
        int width = System.Math.Max(0, dimensions.W);
        int height = System.Math.Max(0, dimensions.H);
        var captured = new RgbaImage(width, height, new byte[checked(width * height * 4)]);
        DrawObjectRange(captured, State.SnapshotVisibleObjects(_clock.NowMs), firstHandle, System.Math.Max(0, count));
        return captured;
    }

    private void PublishObjectRangeToSurface(long firstHandle, long count, IReadOnlyList<RenderObject>? sampled = null)
    {
        int targetSlot = State.CurrentRenderTargetSlot;
        if (targetSlot < 0 || !_slotDims.TryGetValue(targetSlot, out var dimensions)
            || dimensions.W <= 0 || dimensions.H <= 0)
            return;

        RgbaImage destination;
        destination = _surfaceImages.TryGetValue(targetSlot, out var current)
            ? new RgbaImage(current.Width, current.Height, (byte[])current.Pixels.Clone())
            : new RgbaImage(dimensions.W, dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);

        IReadOnlyList<RenderObject> visible = sampled ?? State.SnapshotVisibleObjects(_clock.NowMs);
        int rendered = DrawObjectRange(destination, visible, firstHandle, count);

        _surfaceImages[targetSlot] = destination;
        IReadOnlyList<RenderObject> retained = visible
            .Where(item => item.Handle >= firstHandle && item.Handle - firstHandle < count)
            .ToArray();
        _renderTargetSnapshots[targetSlot] = retained;
        _timeline?.Event("render-target-publish", new()
        {
            ["surface"] = targetSlot,
            ["first"] = firstHandle,
            ["count"] = count,
            ["objects"] = retained.Count,
            ["rendered"] = rendered,
        });
    }

    private IReadOnlyList<RenderObject>? RenderTargetSnapshot(int slot)
    {
        return _renderTargetSnapshots.GetValueOrDefault(slot);
    }

    // ---- the whole-screen transition (ops 0x21, 0x22, 0x24, 0x25) ----

    /// <summary>Ops 0x21/0x22: fade surface <paramref name="surface"/>'s captured objects from or to black.</summary>
    public ScreenTransitionStart StartSurfaceBlackFade(
        int surface, long intervalArgument, SurfaceBlackFadeDirection direction, bool forceEndpoint)
    {
        long start = _clock.NowMs;
        IReadOnlyList<RenderObject> captured = RenderTargetSnapshot(surface) ?? System.Array.Empty<RenderObject>();
        IReadOnlyList<RenderObject> black = System.Array.Empty<RenderObject>();
        IReadOnlyList<RenderObject> source = direction == SurfaceBlackFadeDirection.FromBlack ? black : captured;
        IReadOnlyList<RenderObject> target = direction == SurfaceBlackFadeDirection.FromBlack ? captured : black;
        return StartScreenTransition(source, target, start, intervalArgument, forceEndpoint, new()
        {
            ["kind"] = "surface-black-fade",
            ["surface"] = surface,
            ["direction"] = direction == SurfaceBlackFadeDirection.FromBlack ? "from-black" : "to-black",
        });
    }

    /// <summary>Op 0x25: crossfade one surface's captured objects into another's (the live objects when the target
    /// has none). A negative target, which the op passes only when it finishes at once, is black: native's
    /// <c>screen_transition_finalize</c> fills scratch surface 0x20 with black for it.</summary>
    public ScreenTransitionStart StartSurfaceCrossfade(
        int sourceSurface, int targetSurface, long intervalArgument, bool forceEndpoint)
    {
        long start = _clock.NowMs;
        IReadOnlyList<RenderObject> source;
        IReadOnlyList<RenderObject> target;
        source = _renderTargetSnapshots.TryGetValue(sourceSurface, out var capturedSource)
            ? capturedSource : System.Array.Empty<RenderObject>();
        target = targetSurface < 0 ? System.Array.Empty<RenderObject>()
            : _renderTargetSnapshots.TryGetValue(targetSurface, out var capturedTarget)
                ? capturedTarget : State.SnapshotVisibleObjects(start);
        return StartScreenTransition(source, target, start, intervalArgument, forceEndpoint, new()
        {
            ["kind"] = "surface-crossfade",
            ["source"] = sourceSurface,
            ["target"] = targetSurface,
        });
    }

    /// <summary>The white fade: surface <paramref name="surface"/>'s captured objects fade to white.</summary>
    public ScreenTransitionStart StartSurfaceWhiteFade(int surface, long intervalArgument, bool forceEndpoint)
    {
        long start = _clock.NowMs;
        IReadOnlyList<RenderObject> captured = RenderTargetSnapshot(surface) ?? System.Array.Empty<RenderObject>();
        return StartScreenTransition(
            captured, System.Array.Empty<RenderObject>(), start, intervalArgument, forceEndpoint,
            new()
            {
                ["kind"] = "surface-white-fade",
                ["surface"] = surface,
                ["direction"] = "to-white",
            }, overlayRgb: 0xffffff);
    }

    /// <summary>Ops 0x26, 0x27 and 0x28: reveal a surface's captured objects over the live ones in the pattern's
    /// rectangles (<see cref="LegacyPatternReveal"/>). A negative surface, which the ops pass only when they finish at
    /// once, is black: native's <c>screen_transition_finalize</c> fills scratch surface 0x20 with black for it.</summary>
    public ScreenTransitionStart StartSurfacePatternReveal(SurfacePatternTransitionRequest request, bool forceEndpoint)
    {
        long start = _clock.NowMs;
        IReadOnlyList<RenderObject> target = request.Surface < 0 ? System.Array.Empty<RenderObject>()
            : RenderTargetSnapshot(request.Surface) ?? State.SnapshotVisibleObjects(start);
        IReadOnlyList<RenderObject> source = State.SnapshotVisibleObjects(start);
        long duration = LegacyScreenTransitionTiming.PatternDurationMilliseconds(
            _screenWidth, _screenHeight, request.IntervalMilliseconds, request.Divisions, request.Mode);
        return StartScreenTransition(source, target, start, request.IntervalMilliseconds,
            forceEndpoint || duration == 0, new()
            {
                ["kind"] = "surface-pattern-reveal",
                ["surface"] = request.Surface,
                ["divisions"] = request.Divisions,
                ["mode"] = (int)request.Mode,
            }, durationOverride: duration, pattern: request,
            periodMs: LegacyScreenTransitionTiming.PatternPeriodMilliseconds(request.IntervalMilliseconds));
    }

    /// <summary>Op 0x36: the source surface's captured objects break up into growing blocks, then the target's (the
    /// live objects when it has none) come back out of shrinking ones (<see
    /// cref="LegacyScreenTransitionTiming.MosaicAt"/>). A negative target, which the op passes only when it finishes
    /// at once, is black.</summary>
    public ScreenTransitionStart StartSurfaceMosaic(
        int sourceSurface, int targetSurface, long intervalMilliseconds, bool forceEndpoint)
    {
        long start = _clock.NowMs;
        IReadOnlyList<RenderObject> source = _renderTargetSnapshots.TryGetValue(sourceSurface, out var capturedSource)
            ? capturedSource : System.Array.Empty<RenderObject>();
        IReadOnlyList<RenderObject> target = targetSurface < 0 ? System.Array.Empty<RenderObject>()
            : _renderTargetSnapshots.TryGetValue(targetSurface, out var capturedTarget)
                ? capturedTarget : State.SnapshotVisibleObjects(start);
        return StartScreenTransition(source, target, start, intervalMilliseconds, forceEndpoint, new()
        {
            ["kind"] = "surface-mosaic",
            ["source"] = sourceSurface,
            ["target"] = targetSurface,
        }, durationOverride: LegacyScreenTransitionTiming.MosaicDurationMilliseconds(intervalMilliseconds),
            periodMs: LegacyScreenTransitionTiming.PatternPeriodMilliseconds(intervalMilliseconds), mosaic: true);
    }

    // The native 0x21/0x22/0x25 family advances an 8-bit alpha accumulator. Values <=64 use the operand as the timer
    // interval and step alpha by sixteen; larger values divide the interval by sixteen and step alpha by one. This
    // reproduces its blocking wall-clock duration.
    private ScreenTransitionStart StartScreenTransition(
        IReadOnlyList<RenderObject> source, IReadOnlyList<RenderObject> target,
        long start, long intervalArgument, bool forceEndpoint,
        Dictionary<string, object?> detail, long overlayRgb = 0, long? durationOverride = null,
        SurfacePatternTransitionRequest? pattern = null, long periodMs = 1, bool mosaic = false)
    {
        long duration = durationOverride ?? LegacyScreenTransitionTiming.DurationMilliseconds(intervalArgument);
        _scene.ScreenTransition = new ScreenTransition(source, target, start, duration, overlayRgb)
        {
            Forced = forceEndpoint,
            Pattern = pattern,
            PeriodMs = System.Math.Max(1, periodMs),
            Mosaic = mosaic,
        };
        RequestPresentation();
        detail["interval_argument"] = intervalArgument;
        detail["duration_ms"] = duration;
        detail["source_objects"] = source.Count;
        detail["target_objects"] = target.Count;
        detail["forced"] = forceEndpoint;
        return new ScreenTransitionStart(start, duration, detail);
    }

    /// <summary>A screen transition is pending: running, or forced and not yet drawn.</summary>
    public bool ScreenTransitionPending
    {
        get { return _scene.ScreenTransition != null; }
    }

    /// <summary>The screen transition has nothing left to run at <paramref name="nowMs"/> (none, forced, or past its
    /// end).</summary>
    public bool ScreenTransitionDue(long nowMs)
    {
        return _scene.ScreenTransition == null || _scene.ScreenTransition.Forced
            || nowMs - _scene.ScreenTransition.StartMs >= _scene.ScreenTransition.DurationMs;
    }

    /// <summary>Force the pending screen transition to its endpoint; false when there is none.</summary>
    public bool ForceScreenTransition()
    {
        if (_scene.ScreenTransition == null) return false;
        _scene.ScreenTransition.Forced = true;
        return true;
    }

    /// <summary>The pending screen transition at <paramref name="nowMs"/>. A forced (completed) one is handed out
    /// once: its terminal frame is drawn by this caller, so only the path that draws calls this.</summary>
    public bool TrySnapshotScreenTransition(long nowMs, out LegacyScreenTransitionSnapshot snapshot)
    {
        if (_scene.ScreenTransition == null)
        {
            snapshot = default;
            return false;
        }
        ScreenTransition transition = _scene.ScreenTransition;
        long elapsed = System.Math.Max(0, nowMs - transition.StartMs);
        double progress = transition.Forced ? 1.0
            : System.Math.Clamp(elapsed / (double)transition.DurationMs, 0.0, 1.0);
        // A pattern or a mosaic counts the timer's periods while it runs. At its end, natural or forced, native's
        // last tick and screen_transition_finalize show the target alone.
        bool counting = !transition.Forced && elapsed < transition.DurationMs;
        snapshot = new LegacyScreenTransitionSnapshot(
            transition.Source, transition.Target, progress, transition.OverlayRgb,
            counting ? transition.Pattern : null, counting ? elapsed / transition.PeriodMs : 0,
            counting && transition.Mosaic);
        if (transition.Forced) _scene.ScreenTransition = null;
        return true;
    }

    /// <summary>scene_context_init_reset: the backbuffer publishes everything again over a clean frame, and a pending
    /// screen transition goes. A render target's captured objects stay with its surface.</summary>
    public void ResetScene()
    {
        _scene = new();
        RequestPresentation();
    }

    private sealed class ScreenTransition
    {
        public IReadOnlyList<RenderObject> Source { get; }
        public IReadOnlyList<RenderObject> Target { get; }
        public long StartMs { get; }
        public long DurationMs { get; }
        public long OverlayRgb { get; }
        public bool Forced { get; set; }
        public SurfacePatternTransitionRequest? Pattern { get; init; }   // ops 0x26 to 0x28
        public bool Mosaic { get; init; }                                // op 0x36
        public long PeriodMs { get; init; } = 1;                         // the interval timer's period for those two

        public ScreenTransition(IReadOnlyList<RenderObject> source, IReadOnlyList<RenderObject> target,
                                long startMs, long durationMs, long overlayRgb)
        {
            Source = source;
            Target = target;
            StartMs = startMs;
            DurationMs = durationMs;
            OverlayRgb = overlayRgb;
        }
    }
}
