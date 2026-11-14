using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Text;

/// <summary>Shared test doubles: a host that records observable effects, and an in-memory script
/// provider for synthetic call-script targets. The host also hears the VM's text native (ITextListener), which
/// draws nothing in a VM given none: its lists record what the native was asked to show and draw.</summary>
internal class RecordingWaits : IInputAndWaits, IFastForwardListener, ITextListener
{
    public int Waits;
    public int Presents;
    public int TransitionWaits;
    /// <summary>The anim-clock duration each op 0x21c wait starts with.</summary>
    public readonly List<long> TransitionAnimClocks = new();
    public int InputCallbackFrames;
    // Run-state 0x08000000 and its two channels as the VM's skip and auto native last reported them. A VM built with
    // this host tells it (VirtualMachine's constructor); to start a run with fast-forward on, set the native's channel.
    public bool MessageSkip { get; private set; }
    public bool ScriptMessageSkip { get; private set; }
    public bool PhysicalMessageSkip { get; private set; }
    public readonly List<(int Offset, string Text)> Lines = new();
    public readonly List<(int Slot, int X, int Y)> TextCursors = new();
    public readonly List<(int Surface, int X, int Y, string Text)> SurfaceStrings = new();
    public readonly List<AdvTextHistoryRenderBatch> HistoryRenders = new();
    public readonly List<(AdvTextLayoutPresentationBinding Binding, AdvTextHistoryRenderBatch Batch)>
        RetainedHistoryRenders = new();
    public readonly Dictionary<int, AdvTextHistoryRenderBatch> ActiveHistoryRenders = new();
    public readonly List<(AdvTextLayoutPresentationBinding Binding, AdvTextRedrawBatch Batch)> Redraws = new();
    public int HistoryPresentationEnds;
    public readonly List<int> ClearedTextLayouts = new();
    public readonly List<(long First, long Count)> PresentedRanges = new();
    public readonly List<AdvWaitIndicatorConfig> WaitIndicators = new();
    public readonly List<bool> WaitIndicatorFollowLastGlyphChanges = new();
    public readonly List<(AdvTextLayoutPresentationBinding Binding, AdvTextLayoutSnapshot Layout)>
        WaitIndicatorBindings = new();
    public readonly List<bool> WaitIndicatorEnabledChanges = new();
    public readonly List<int> PublishedAdvTextLayouts = new();
    public readonly List<long> SleptDurations = new();
    public readonly List<long> TimedCallbackWaitDurations = new();
    public readonly List<(long Resource, int Surface, long Flags)> ModalMovies = new();
    public readonly List<(int Surface, long Interval, SurfaceBlackFadeDirection Direction)> SurfaceBlackFades = new();
    public readonly List<(int Surface, long Interval)> SurfaceWhiteFades = new();
    public readonly List<(int Source, int Target, long Interval)> SurfaceCrossfades = new();
    public readonly List<SurfacePatternTransitionRequest> SurfacePatternTransitions = new();
    public readonly List<bool> SurfaceBlackFadeForceEndpoints = new();
    public readonly List<bool> SurfaceWhiteFadeForceEndpoints = new();
    public readonly List<bool> SurfaceCrossfadeForceEndpoints = new();
    public readonly List<bool> SurfacePatternForceEndpoints = new();
    public readonly List<bool> MessageSkipChanges = new();
    public readonly List<bool> PhysicalMessageSkipChanges = new();
    public readonly List<string> Warnings = new();
    public readonly List<DiagnosticMessage> Diagnostics = new();
    public System.Action<DiagnosticMessage>? OnDiagnosticMessage;
    public readonly List<FullwidthTextEditRequest> FullwidthTextEdits = new();
    public System.Func<FullwidthTextEditRequest, FullwidthTextEditResult>? OnFullwidthTextEdit;
    public readonly List<long> CursorResources = new();
    public readonly List<(int X, int Y)> CursorWarps = new();
    public readonly List<bool> AdvPagePresentationSuspended = new();
    public readonly List<(AdvLiveTextRun Run, int GlyphDelayMilliseconds)> LiveTextRuns = new();
    public int CursorClearCount;
    public int SceneContextResets;
    public void ReportWarning(string message) => Warnings.Add(message);
    public void ShowDiagnosticMessage(DiagnosticMessage message)
    {
        Diagnostics.Add(message);
        OnDiagnosticMessage?.Invoke(message);
    }
    /// <summary>Answers for interpreter error boxes, in order; 中止 once they run out.</summary>
    public readonly Queue<ScriptErrorChoice> ScriptErrorAnswers = new();
    public ScriptErrorChoice ShowScriptError(DiagnosticMessage message)
    {
        ShowDiagnosticMessage(message);
        return ScriptErrorAnswers.Count > 0 ? ScriptErrorAnswers.Dequeue() : ScriptErrorChoice.Abort;
    }
    public FullwidthTextEditResult EditFullwidthString(FullwidthTextEditRequest request)
    {
        FullwidthTextEdits.Add(request);
        return OnFullwidthTextEdit?.Invoke(request) ?? new(false, request.CurrentText);
    }
    // ---- the text native (ITextListener) ----
    public virtual void RunShown(AdvLiveTextRun run, int glyphDelayMilliseconds)
    {
        Lines.Add((run.SourceOffset, run.Text));
        LiveTextRuns.Add((run, glyphDelayMilliseconds));
    }
    /// <summary>The later runs of a presented message (localization spec §6.2), which draw nothing.</summary>
    public readonly List<AdvLiveTextRun> HiddenTextRuns = new();
    public void HiddenRunShown(AdvLiveTextRun run)
    {
        Lines.Add((run.SourceOffset, run.Text));
        HiddenTextRuns.Add(run);
    }
    public void CursorMoved(int layoutSlot, int x, int y) => TextCursors.Add((layoutSlot, x, y));
    // A string the presenter replaced records the presented text.
    public void StringDrawn(int surfaceSlot, int x, int y, string text, AdvTextStyle style,
                            Ome.Engine.Localization.PresentedText? presented)
        => SurfaceStrings.Add((surfaceSlot, x, y, presented?.Text ?? text));
    public void HistoryGroupDrawn(AdvTextLayoutPresentationBinding binding, AdvTextHistoryRenderBatch batch)
    {
        RetainedHistoryRenders.Add((binding, batch));
        HistoryRenders.Add(batch);
        ActiveHistoryRenders[batch.LayoutSlot] = batch;
    }
    public void GroupRedrawn(AdvTextLayoutPresentationBinding binding, AdvTextRedrawBatch batch)
        => Redraws.Add((binding, batch));
    public void LayoutCleared(int layoutSlot)
    {
        ClearedTextLayouts.Add(layoutSlot);
        ActiveHistoryRenders.Remove(layoutSlot);
    }
    public void HistoryPresentationEnded()
    {
        HistoryPresentationEnds++;
        ActiveHistoryRenders.Clear();
    }
    public void WaitIndicatorConfigured(AdvWaitIndicatorConfig config) => WaitIndicators.Add(config);
    public void WaitIndicatorFollowsLastGlyph(bool enabled) => WaitIndicatorFollowLastGlyphChanges.Add(enabled);
    public void WaitIndicatorBound(AdvTextLayoutPresentationBinding binding, AdvTextLayoutSnapshot layout)
        => WaitIndicatorBindings.Add((binding, layout));
    public void WaitIndicatorEnabled(bool enabled) => WaitIndicatorEnabledChanges.Add(enabled);
    public void LayoutPublished(int layoutSlot) => PublishedAdvTextLayouts.Add(layoutSlot);
    public void PageSuspended(bool suspended) => AdvPagePresentationSuspended.Add(suspended);
    public void PresentObjectRange(GfxState gfx, long firstHandle, long count)
        => PresentedRanges.Add((firstHandle, count));
    public void WaitForInput() => Waits++;
    public virtual void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
    {
        while (serviceInputCallback()) { }
        WaitForInput();
    }
    public virtual void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                                     Func<AdvAutoWaitState> autoWaitState)
        => WaitForInput(layoutSlot, serviceInputCallback);
    // Op 0xfa's poll: no page, so it counts apart from Waits. Each records whether fast-forward was on at its entry.
    public readonly List<bool> AdvInputPolls = new();
    public virtual void WaitForAdvInputPoll(Func<bool> serviceInputCallback, Func<AdvAutoWaitState> autoWaitState)
    {
        AdvInputPolls.Add(MessageSkip);
        while (serviceInputCallback()) { }
    }
    public void InputCallbackCompleted(GfxState gfx) => InputCallbackFrames++;
    public virtual void EndInputWait() { }
    // Each hidden-window wait runs this first (a test supplies the input that ends it) and then takes the trigger.
    public Action? ServiceTriggerWaitStarted;
    public int ServiceTriggerWaits;
    public bool LastServiceTriggerWaitTriggered;
    public virtual void WaitForServiceTrigger(Func<bool> triggered)
    {
        ServiceTriggerWaits++;
        ServiceTriggerWaitStarted?.Invoke();
        LastServiceTriggerWaitTriggered = triggered();
    }
    public virtual long InputClockMilliseconds => Environment.TickCount64;
    public int JoystickButtonCount { get; set; }
    public void SetCursorResource(long resourceId) => CursorResources.Add(resourceId);
    public void ClearCursorResource() => CursorClearCount++;
    public void WarpCursor(int virtualX, int virtualY) => CursorWarps.Add((virtualX, virtualY));
    /// <summary>The clock an <see cref="AudioRig"/>'s natives run on: each sleep moves it by the sleep's
    /// length.</summary>
    public FrameClock? Clock { get; set; }
    public virtual void Sleep(long duration)
    {
        SleptDurations.Add(duration);
        Clock?.Advance(duration / 1000.0);
    }
    public virtual void WaitForTimedCallbackDeadline(long duration)
    {
        TimedCallbackWaitDurations.Add(duration);
        Sleep(duration);
    }
    public virtual void FrameYield() { }
    public virtual void ResetSceneContext() => SceneContextResets++;
    public int RootReloadInterrupts;
    public virtual void InterruptForRootReload() => RootReloadInterrupts++;
    // MessageSkipChanges records run-state 0x08000000 after each script-channel set, PhysicalMessageSkipChanges each
    // action-6 set, changed or not.
    public void FastForwardChannelSet(FastForwardChannel channel, bool value, bool active, bool changed)
    {
        MessageSkip = active;
        if (channel == FastForwardChannel.Script)
        {
            ScriptMessageSkip = value;
            MessageSkipChanges.Add(active);
        }
        else
        {
            PhysicalMessageSkip = value;
            PhysicalMessageSkipChanges.Add(value);
        }
    }
    public void PresentFrame(GfxState gfx)
    {
        Presents++;
        gfx.StartForegroundTransitions(100);
        gfx.CompleteForegroundTransitions(100);
    }
    public void WaitForForegroundTransition(GfxState gfx)
    {
        TransitionWaits++;
        TransitionAnimClocks.Add(gfx.AnimClockDurationTicks);
        gfx.StartForegroundTransitions(100);
        gfx.CompleteForegroundTransitions(100);
    }
    public void FadeSurfaceWithBlack(
        GfxState gfx, int surface, long intervalArgument, SurfaceBlackFadeDirection direction,
        bool forceEndpoint = false)
    {
        SurfaceBlackFades.Add((surface, intervalArgument, direction));
        SurfaceBlackFadeForceEndpoints.Add(forceEndpoint);
    }
    public void FadeSurfaceToWhite(
        GfxState gfx, int surface, long intervalArgument, bool forceEndpoint = false)
    {
        SurfaceWhiteFades.Add((surface, intervalArgument));
        SurfaceWhiteFadeForceEndpoints.Add(forceEndpoint);
    }
    public void CrossfadeSurfaces(
        GfxState gfx, int sourceSurface, int targetSurface, long intervalArgument,
        bool forceEndpoint = false)
    {
        SurfaceCrossfades.Add((sourceSurface, targetSurface, intervalArgument));
        SurfaceCrossfadeForceEndpoints.Add(forceEndpoint);
    }
    public void RevealSurfaceWithPattern(
        GfxState gfx, SurfacePatternTransitionRequest request, bool forceEndpoint = false)
    {
        SurfacePatternTransitions.Add(request);
        SurfacePatternForceEndpoints.Add(forceEndpoint);
    }
    public void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags)
        => ModalMovies.Add((resourceId, surfaceSlot, movieFlags));
    // Native's input latch handing a held click to the next modal movie (VirtualMachine.InputLatch.cs); the text native
    // keeps what it hands the next reveal (TextManager.TextRevealsFinishedUntilWait).
    public int ModalMovieCancels;
    public void CancelNextModalMovie() => ModalMovieCancels++;
}

internal sealed class MapProvider : IScriptProvider
{
    private readonly Dictionary<long, Script> _m;
    private readonly Dictionary<string, Script> _byName;
    public MapProvider(Dictionary<long, Script> m, Dictionary<string, Script>? byName = null)
    {
        _m = m;
        _byName = byName ?? new(StringComparer.OrdinalIgnoreCase);
    }
    public Script? GetById(long id) => _m.TryGetValue(id, out var s) ? s : null;
    public Script? GetByName(string name) => _byName.TryGetValue(name, out var s) ? s : null;
}

/// <summary>A controlled test double: every call-script id resolves to the same script (typically a
/// no-op that just exits). Lets a test run a real script with call-script handling ON while isolating
/// it from the real subroutines' game-state dependencies.</summary>
internal sealed class AnyProvider : IScriptProvider
{
    private readonly Script _s;
    public AnyProvider(Script s) => _s = s;
    public Script? GetById(long id) => _s;
}

/// <summary>Captures every trace event for assertions; TracingSteps is settable so a test can
/// exercise the Step gate both ways.</summary>
internal sealed class RecordingTraceSink : ITraceSink
{
    public bool TracingSteps { get; init; }
    public readonly List<TraceEvent> Events = new();
    public void Emit(in TraceEvent e) => Events.Add(e);
    public List<long> CallScriptIds =>
        Events.Where(e => e.Kind == TraceEventKind.CallScript).Select(e => e.Id).ToList();
}

/// <summary>Runs a VM on its own background thread for tests that hand off with it while it waits in a host call.
/// The test drives the handshake with blocking waits on its own thread, never an await: an await resumes on xUnit's
/// worker pool, which long synchronous tests can hold for minutes, and the VM's side of the handshake would time
/// out meanwhile. Every timeout here is <see cref="HangGuard"/>, which only turns a hang into a failure.</summary>
internal sealed class VmRunThread
{
    public static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private readonly Thread _thread;
    private System.Runtime.ExceptionServices.ExceptionDispatchInfo? _error;

    public VmRunThread(Ome.Engine.Vm.VirtualMachine vm)
    {
        _thread = new Thread(() =>
        {
            try { vm.Run(); }
            catch (Exception error) { _error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
        }) { IsBackground = true, Name = "test VM" };
        _thread.Start();
    }

    /// <summary>Wait for a signal the VM thread gives from inside a host call.</summary>
    public static void AwaitSignal(ManualResetEventSlim signal, string what)
    {
        if (!signal.Wait(HangGuard)) throw new TimeoutException($"the VM never {what}");
    }

    /// <summary>Wait for the run to end, and rethrow whatever ended it abnormally.</summary>
    public void Join()
    {
        if (!_thread.Join(HangGuard)) throw new TimeoutException("the VM run did not end");
        _error?.Throw();
    }
}

/// <summary>The VM's playtime clock (native timeGetTime) in milliseconds, moving only when a test moves it: by setting
/// <see cref="NowMs"/>, or through <see cref="ClockedHost"/>'s sleeps.</summary>
internal sealed class ManualPlayClock : TimeProvider
{
    public long NowMs { get; set; }

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => NowMs;
}

/// <summary>A recording host whose op-0xc8 sleeps advance <paramref name="clock"/>, so a script spends exact time.
/// </summary>
internal sealed class ClockedHost(ManualPlayClock clock) : RecordingWaits
{
    public override void Sleep(long duration)
    {
        base.Sleep(duration);
        clock.NowMs += duration;
    }
}

/// <summary>Skip and auto natives in the states tests start from.</summary>
internal static class TestSkipAndAuto
{
    /// <summary>Action 6's fast-forward already on, as a run under held Ctrl starts.</summary>
    public static SkipAndAuto HoldingFastForward()
    {
        SkipAndAuto skipAndAuto = SkipAndAuto.WithSystemClock();
        skipAndAuto.SetPhysicalFastForward(true);
        return skipAndAuto;
    }

    /// <summary>Read skip on: message:ReadTextSkip set and the current message read, as a text wait leaves it.</summary>
    public static SkipAndAuto ReadSkipping()
    {
        SkipAndAuto skipAndAuto = SkipAndAuto.WithSystemClock();
        skipAndAuto.ApplyReadTextSkip(readTextSkip: true, messageRead: true);
        return skipAndAuto;
    }
}

/// <summary>Surfaces for tests that run the real graphics native over no assets.</summary>
internal static class GraphicsTestSupport
{
    /// <summary>A <paramref name="width"/> × <paramref name="height"/> image whose pixels <paramref name="pixel"/>
    /// gives as RGBA.</summary>
    public static RgbaImage Image(int width, int height, Func<int, int, byte[]> pixel)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            pixel(x, y).CopyTo(pixels, (y * width + x) * 4);
        return new RgbaImage(width, height, pixels);
    }

    /// <summary>Surface <paramref name="slot"/>'s RGBA at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static byte[] PixelAt(this Graphics graphics, int slot, int x, int y)
    {
        RgbaImage image = graphics.SurfacePixels(slot) ?? throw new InvalidOperationException($"surface {slot} has no pixels");
        return image.Pixels.AsSpan((y * image.Width + x) * 4, 4).ToArray();
    }

    /// <summary>The graphics native's surface ops from now on, one line each (<see cref="Graphics.OpTrace"/>).</summary>
    public static List<string> TraceSurfaceOps(this Ome.Engine.Vm.VirtualMachine vm)
    {
        var ops = new List<string>();
        vm.Graphics.OpTrace = ops.Add;
        return ops;
    }
}

/// <summary>The VM's text native with devices, for tests that need text laid out and drawn: glyphs on a grid, and the
/// installed fonts the SYS4433 font ops read.</summary>
internal static class TestText
{
    /// <summary>Glyphs on a grid: each cell as tall as the request's pixel height and as wide as
    /// <paramref name="advance"/> says, inked full but for a space.</summary>
    internal sealed class GridGlyphs(GlyphRasterPolicy policy, Func<GlyphRasterRequest, int> advance)
        : IIdentifiedGlyphMaskRasterizer
    {
        public GlyphRasterizerBackendInfo BackendInfo { get; } =
            new("grid", "Grid glyphs", policy, false, "test glyphs on a grid");

        public GlyphMask Rasterize(GlyphRasterRequest request)
        {
            int width = advance(request);
            int ink = request.UnicodeScalar is ' ' or 0x3000 ? 0 : width;
            int height = request.PixelHeight;
            return new GlyphMask(ink, height, ink, originX: 0, originY: height, cellAdvanceX: width, cellAdvanceY: 0,
                                 cellWidth: width, cellHeight: height, Enumerable.Repeat((byte)16, ink * height).ToArray(),
                                 ascent: height);
        }
    }

    private static bool HalfWidth(GlyphRasterRequest request)
        => request.Cp932Code is { } code ? code <= 0xff : request.UnicodeScalar < 0x80;

    /// <summary>CP932 glyphs <paramref name="fullWidth"/> pixels wide, <paramref name="halfWidth"/> for a single-byte
    /// code.</summary>
    public static GridGlyphs Fixed(int fullWidth, int halfWidth)
        => new(GlyphRasterPolicy.NativeCp932Gray4, request => HalfWidth(request) ? halfWidth : fullWidth);

    /// <summary>CP932 glyphs as wide as their pixel height, half that for a single-byte code.</summary>
    public static GridGlyphs Square()
        => new(GlyphRasterPolicy.NativeCp932Gray4,
               request => HalfWidth(request) ? request.PixelHeight / 2 : request.PixelHeight);

    /// <summary>Unicode glyphs, every one <paramref name="width"/> pixels wide: what presented text draws with.</summary>
    public static GridGlyphs Unicode(int width) => new(GlyphRasterPolicy.PortableUnicode, _ => width);

    /// <summary>Source text drawn with <paramref name="source"/>, and presented text with <paramref name="presented"/>
    /// while a language is active.</summary>
    public static TextDevices Glyphs(GridGlyphs source, GridGlyphs? presented = null)
        => new(source, GlyphCoverageAlpha.Sixteenths, Presented: presented == null ? null : () => presented);

    /// <summary>Square glyphs, and the installed font families <paramref name="faces"/> in order.</summary>
    public static TextDevices Fonts(IReadOnlyList<string> faces)
        => new(Square(), GlyphCoverageAlpha.Sixteenths,
               FontFamilies: () => [.. faces.Select(face => new EnumeratedFontFamily(face, 0))]);

    /// <summary>A VM whose text native draws with <paramref name="devices"/>, over a headless graphics native.</summary>
    public static Ome.Engine.Vm.VirtualMachine Vm(Script script, OpcodeTable table, IInputAndWaits host,
                                                  TextDevices devices, Ome.Engine.Vm.EngineBuild? build = null,
                                                  IScriptProvider? provider = null)
    {
        var clock = new FrameClock();
        SkipAndAuto skip = SkipAndAuto.WithSystemClock();
        var graphics = new Graphics(null, clock, new Sys4LogicalCanvas(0, 0));
        var text = new TextManager(graphics, skip, clock, devices, RubyLayoutRules.FixedOffsets);
        return new Ome.Engine.Vm.VirtualMachine(script, table, host, provider: provider, build: build,
                                                skipAndAuto: skip, graphics: graphics, text: text);
    }
}
