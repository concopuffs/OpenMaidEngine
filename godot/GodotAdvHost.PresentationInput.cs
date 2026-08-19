using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;

[Flags]
public enum HostPresentationReason
{
    None = 0,
    HostRequest = 1,
    ScreenTransition = 2,
    RetainedMutation = 4,
    ContinuousChannel = 8,
    DiscreteSourceCell = 16,
}

public readonly record struct BackbufferPublicationPolicy(
    bool PreserveExistingPixels,
    bool AppendGpuLayers);

public sealed partial class GodotAdvHost
{
    private readonly object _scriptContextLock = new();
    private readonly Stack<string> _scriptContexts = new();
    private readonly ScriptPresentationBarrier _presentationBarrier = new();
    private readonly AutoResetEvent _presentationRequestConsumed = new(false);
    private readonly AutoResetEvent _diagnosticMessageCompleted = new(false);
    private readonly AutoResetEvent _fullwidthTextEditCompleted = new(false);
    private readonly object _fullwidthTextEditLock = new();
    private FullwidthTextEditResult _fullwidthTextEditResult;
    private readonly bool _synchronizeExplicitPresentation;
    private long _explicitPresentationRequestGeneration;
    private long _consumedPresentationRequestGeneration;
    private readonly SemaphoreSlim _gate = new(0, 1);
    private readonly AutoResetEvent _inputCallbackSignal = new(false);
    private readonly System.Threading.AutoResetEvent _frameSignal = new(false);
    private volatile bool _stopping;
    private readonly object _messageSkipLock = new();
    private bool _scriptMessageSkipActive;
    private bool _physicalMessageSkipActive;
    private volatile bool _messageSkipActive;
    private volatile bool _advPagePresentationSuspended;
    private volatile bool _modalMovieWaiting;
    private volatile bool _modalMovieCancelled;
    private GfxState? _foregroundGfx;
    private volatile bool _foregroundTransitionWaitBypassed;
    private readonly object _screenTransitionLock = new();
    private readonly Dictionary<int, IReadOnlyList<RenderObject>> _renderTargetSnapshots = new();
    private readonly object _backbufferRangeLock = new();
    private GfxHandleRange _backbufferRange = GfxHandleRange.All;
    private bool _backbufferPublicationPreservesExistingPixels;
    private bool _backbufferClearPending;
    private LegacyScreenTransition? _screenTransition;
    public volatile bool IsWaiting;
    public volatile bool IsTransitionWaiting;
    public volatile bool IsSleeping;
    public bool IsModalMovieWaiting => _modalMovieWaiting;
    private int _presentRequested = 1;
    private long _transitionStartedAtMs = -1;
    public long TransitionStartedAtMs => System.Threading.Interlocked.Read(ref _transitionStartedAtMs);

    public void ShowDiagnosticMessage(DiagnosticMessage message)
    {
        if (_stopping) return;
        _timeline?.Event("diagnostic-message", new()
        {
            ["caption"] = message.Caption,
            ["text"] = message.Text,
        });
        _main.CallDeferred("ShowAgeDiagnostic", message.Text, message.Caption);
        while (!_stopping && !_diagnosticMessageCompleted.WaitOne(50)) { }
    }

    public void CompleteDiagnosticMessage() => _diagnosticMessageCompleted.Set();

    public FullwidthTextEditResult EditFullwidthString(FullwidthTextEditRequest request)
    {
        if (_stopping) return new(false, request.CurrentText);
        lock (_fullwidthTextEditLock)
            _fullwidthTextEditResult = new(false, request.CurrentText);
        _timeline?.Event("fullwidth-text-edit", new()
        {
            ["current"] = request.CurrentText,
            ["initial"] = request.InitialText,
        });
        _main.CallDeferred("ShowAgeFullwidthTextEditor", request.InitialText);
        while (!_stopping && !_fullwidthTextEditCompleted.WaitOne(50)) { }
        lock (_fullwidthTextEditLock) return _fullwidthTextEditResult;
    }

    public void CompleteFullwidthTextEdit(bool accepted, string text)
    {
        lock (_fullwidthTextEditLock)
            _fullwidthTextEditResult = new(accepted, text);
        _fullwidthTextEditCompleted.Set();
    }

    private string CurrentScene
    {
        get { lock (_scriptContextLock) return _scriptContexts.TryPeek(out var scene) ? scene : _rootScene; }
    }

    public void EnterScriptContext(string scriptName)
    {
        _presentationBarrier.EnterScript();
        string scene = System.IO.Path.GetFileNameWithoutExtension(scriptName).ToUpperInvariant();
        lock (_scriptContextLock) _scriptContexts.Push(scene);
        _timeline?.Event("script-context-enter", new() { ["scene"] = scene });
    }

    public void ExitScriptContext()
    {
        string? scene = null;
        lock (_scriptContextLock)
            if (_scriptContexts.TryPop(out var popped)) scene = popped;
        if (scene != null) _timeline?.Event("script-context-exit", new() { ["scene"] = scene });
        _presentationBarrier.ExitScript();
    }

    public bool TryEnterPresentation() => _presentationBarrier.TryEnterPresentation();

    public void ExitPresentation() => _presentationBarrier.ExitPresentation();

    private bool SuspendScriptForPresentation() => _presentationBarrier.SuspendScript();

    private void ResumeScriptAfterPresentation(bool suspended)
        => _presentationBarrier.ResumeScript(suspended);

    private void RequestSynchronizedPresentation()
    {
        long requested = Interlocked.Increment(ref _explicitPresentationRequestGeneration);
        Interlocked.Exchange(ref _presentRequested, 1);
        if (!_synchronizeExplicitPresentation) return;
        while (Interlocked.Read(ref _consumedPresentationRequestGeneration) < requested && !_stopping)
            _presentationRequestConsumed.WaitOne(50);
    }

    public void PresentObjectRange(GfxState gfx, long firstHandle, long count)
    {
        if (gfx.CurrentRenderTargetSlot >= 0)
        {
            PublishObjectRangeToSurface(gfx, firstHandle, count);
            return;
        }
        lock (_backbufferRangeLock)
        {
            _backbufferRange = new GfxHandleRange(firstHandle, count);
            _backbufferPublicationPreservesExistingPixels = true;
        }
        _timeline?.Event("present-object-range", new() { ["first"] = firstHandle, ["count"] = count });
        // Native backbuffer 0x222 ends its D3D scene and calls Present before returning. Snapshot this
        // exact retained state before its callback can mutate or release the capture surface.
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
    }

    public BackbufferPublicationPolicy SnapshotBackbufferObjects(
        GfxState gfx, long nowMs, List<RenderObject> snapshot)
    {
        gfx.SnapshotVisibleObjects(nowMs, snapshot);
        GfxHandleRange range;
        bool preserveExistingPixels;
        lock (_backbufferRangeLock)
        {
            range = _backbufferRange;
            preserveExistingPixels =
                _backbufferPublicationPreservesExistingPixels && !_backbufferClearPending;
            _backbufferClearPending = false;
        }
        int write = 0;
        for (int read = 0; read < snapshot.Count; read++)
            if (range.Contains(snapshot[read].Handle)) snapshot[write++] = snapshot[read];
        if (write < snapshot.Count) snapshot.RemoveRange(write, snapshot.Count - write);
        return ResolveBackbufferPublicationPolicy(
            preserveExistingPixels, range, snapshot.Count);
    }

    internal static BackbufferPublicationPolicy ResolveBackbufferPublicationPolicy(
        bool preserveExistingPixels, GfxHandleRange range, int visibleObjectCount)
    {
        // Native preservation is a pixel rule, not permission to retain an unbounded history of
        // Godot Sprite2D nodes. Partial ranges must overlay. A nonempty full redraw compacts the GPU
        // stage, while an empty full redraw leaves the preceding stage untouched.
        bool appendGpuLayers = preserveExistingPixels
                               && (range != GfxHandleRange.All || visibleObjectCount == 0);
        return new BackbufferPublicationPolicy(preserveExistingPixels, appendGpuLayers);
    }

    public volatile int Pages;   // VM-thread page counter (incremented before IsWaiting so shot-gating can't race)

    public void WaitForInput() => WaitForInput(0);

    public void WaitForInput(int layoutSlot)
        => WaitForInput(layoutSlot, static () => false, static () => default);

    public void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback)
        => WaitForInput(layoutSlot, serviceInputCallback, static () => default);

    public void WaitForInput(int layoutSlot, Func<bool> serviceInputCallback,
                             Func<AdvAutoWaitState> autoWaitState)
    {
        Pages++;
        _locator.Wait(Pages);
        _main.CallDeferred("PageBreak");
        lock (_textLock)
        {
            _activeWaitLayout = layoutSlot == 0 ? _currentAdvLayout : layoutSlot;
            _waitIndicatorStartedMs = _clock.NowMs;
            _waitIndicatorEnabled = true;
        }
        IsWaiting = true;
        _timeline?.State("input-wait", new() { ["page"] = Pages });
        var autoTimer = new AdvAutoAdvanceTimer();
        bool autoAdvanced = false;
        bool messageSkipped = false;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            // Publish the completed pre-wait burst once. Callback scripts temporarily reacquire the
            // write side below, so each hover/click update is likewise exposed only after it returns.
            RequestSynchronizedPresentation();
            while (!_stopping)
            {
                while (true)
                {
                    bool markerWasEnabled;
                    lock (_textLock) markerWasEnabled = _waitIndicatorEnabled;
                    if (scriptSuspended) ResumeScriptAfterPresentation(true);
                    bool keepServicing;
                    try
                    {
                        keepServicing = serviceInputCallback();
                    }
                    finally
                    {
                        if (scriptSuspended && !SuspendScriptForPresentation())
                            throw new InvalidOperationException("Input callback did not retain the script burst.");
                    }
                    // The native callback returns through the shared ADV redraw/wait path, whose op 0x72
                    // re-arms a marker stopped by nested HISTORY. Our blocking host keeps the parent wait
                    // parked, so restore that parent-owned state at the equivalent callback boundary.
                    lock (_textLock)
                    {
                        if (markerWasEnabled && !_waitIndicatorEnabled)
                        {
                            _waitIndicatorEnabled = true;
                            _waitIndicatorStartedMs = _clock.NowMs;
                        }
                    }
                    if (!keepServicing) break;
                }
                if (_messageSkipActive)
                {
                    messageSkipped = true;
                    break;
                }
                if (autoTimer.Poll(autoWaitState(), _main.IsVoicePlaybackActive, _clock.NowMs))
                {
                    autoAdvanced = true;
                    break;
                }
                if (_gate.Wait(0)) break;
                WaitHandle.WaitAny(new[] { _gate.AvailableWaitHandle, _inputCallbackSignal, _frameSignal });
                if (_gate.Wait(0)) break;
            }
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        IsWaiting = false;
        lock (_textLock) _waitIndicatorEnabled = false;
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.State("running", new()
        {
            ["input"] = messageSkipped ? "message-skip" : autoAdvanced ? "auto" : "user",
        });
        lock (_textLock)
        {
            _advText = "";
            _advTextX = 100;
            _advTextY = 47;
        }
        _main.CallDeferred("ClearPage");
    }

    // Called from the main thread (click) or an auto-clicker. A transition click is consumed by
    // the foreground lifecycle; it never pre-arms or advances the following stable input wait.
    public void SignalInput()
    {
        if (_modalMovieWaiting)
        {
            _modalMovieCancelled = true;
            _timeline?.State("modal-movie-cancel", new());
            _frameSignal.Set();
            return;
        }
        if (IsTextRevealing)
        {
            lock (_textLock) _advTextForceComplete = true;
            _timeline?.State("text-reveal-forced-complete", new());
            _frameSignal.Set();
            return;
        }
        if (TryForceActiveTransition("advance")) return;
        if (IsWaiting && _gate.CurrentCount == 0) _gate.Release();
    }

    private bool TryForceActiveTransition(string source)
    {
        if (IsTransitionWaiting && _foregroundGfx != null)
        {
            bool accepted = _foregroundGfx.TryCompleteClickSkippableTimedPresentation(
                _clock.NowMs, out int completed);
            if (accepted)
            {
                _foregroundTransitionWaitBypassed = true;
                _timeline?.State("transition-skip-accepted", new()
                {
                    ["endpoints_completed"] = completed,
                    ["source"] = source,
                    ["wait_bypassed"] = true,
                });
                _frameSignal.Set();
                return true;
            }
        }
        if (IsTransitionWaiting)
        {
            lock (_screenTransitionLock)
            {
                if (_screenTransition != null)
                {
                    _screenTransition.Forced = true;
                    _timeline?.State("screen-transition-forced-complete", new()
                    {
                        ["source"] = source,
                    });
                    _frameSignal.Set();
                    return true;
                }
            }
        }
        return false;
    }

    public bool IsMessageSkipActive => _messageSkipActive;

    public void SetMessageSkipActive(bool active)
        => SetMessageSkipChannel(active, physical: false);

    public void SetPhysicalMessageSkipActive(bool active)
        => SetMessageSkipChannel(active, physical: true);

    private void SetMessageSkipChannel(bool active, bool physical)
    {
        bool effective;
        bool changed;
        (AudioPayload Audio, int PlaybackVariant)? queued = null;
        lock (_messageSkipLock)
        {
            if (physical) _physicalMessageSkipActive = active;
            else _scriptMessageSkipActive = active;
            effective = _scriptMessageSkipActive || _physicalMessageSkipActive;
            changed = effective != _messageSkipActive;
            _messageSkipActive = effective;
            if (changed && !effective)
            {
                queued = _queuedSkippedVoice;
                _queuedSkippedVoice = null;
            }
        }
        if (!changed) return;
        _timeline?.State("message-skip", new()
        {
            ["enabled"] = effective,
            ["source"] = physical ? "logical-action-6" : "script",
        });
        if (effective)
        {
            lock (_textLock) _advTextForceComplete = true;
            TryForceActiveTransition("message-skip");
            _frameSignal.Set();
            _inputCallbackSignal.Set();
            return;
        }
        if (queued != null) DispatchVoice(queued.Value.Audio, queued.Value.PlaybackVariant);
    }

    public void WakeInputCallbackService() => _inputCallbackSignal.Set();

    public bool IsAdvPagePresentationSuspended => _advPagePresentationSuspended;

    public void SetAdvPagePresentationSuspended(bool suspended)
    {
        _advPagePresentationSuspended = suspended;
        _timeline?.State(suspended ? "adv-page-suspended" : "adv-page-restored", new());
    }

    public void SetAdvPagePresentationSuspended(GfxState gfx, bool suspended)
    {
        lock (_textLock)
        {
            if (suspended)
            {
                int slot = IsWaiting && _activeWaitLayout != 0
                    ? _activeWaitLayout
                    : _currentAdvLayout;
                if (_retainedTextLayouts.TryGetValue(
                        slot, out RetainedAdvTextLayoutPresentation? presentation))
                    presentation.ErasePublished(gfx);
                if (_retainedWaitIndicators.TryGetValue(
                        slot, out RetainedAdvWaitIndicatorPresentation? indicator))
                    indicator.Erase(gfx);
                if (_retainedTextLayouts.ContainsKey(slot)
                    || _retainedWaitIndicators.ContainsKey(slot))
                    _suspendedRetainedTextLayoutSlot = slot;
            }
            else if (_suspendedRetainedTextLayoutSlot != 0)
            {
                if (_retainedTextLayouts.TryGetValue(
                        _suspendedRetainedTextLayoutSlot,
                        out RetainedAdvTextLayoutPresentation? presentation))
                    presentation.Republish(gfx);
                if (_retainedWaitIndicators.TryGetValue(
                        _suspendedRetainedTextLayoutSlot,
                        out RetainedAdvWaitIndicatorPresentation? indicator))
                    indicator.Republish(
                        gfx,
                        _clock.NowMs - _waitIndicatorStartedMs,
                        IsWaiting
                        && _waitIndicatorEnabled
                        && _activeWaitLayout == _suspendedRetainedTextLayoutSlot);
                _suspendedRetainedTextLayoutSlot = 0;
            }
        }
        Interlocked.Exchange(ref _presentRequested, 1);
        SetAdvPagePresentationSuspended(suspended);
    }

    public void InputCallbackCompleted(GfxState gfx)
    {
        // Callback completion itself is not native graphics dirtiness. Any retained writes made by the
        // callback are published through GfxState's mutation generation; host-owned surface writes set
        // _presentRequested at their actual mutation sites. FIELD services a 50 ms hover callback even
        // while the pointer is idle, so an unconditional request here recreates its sleep-poll overdraw.
    }

    public long InputClockMilliseconds => _clock.NowMs;

    public void SetCursorResource(long resourceId)
    {
        var asset = _res.ResolveCursor(resourceId);
        if (asset == null) return;
        try
        {
            var cursor = _res.DecodeCursor(asset);
            _main.CallDeferred("SetAgeCursor", cursor.Image.Pixels, cursor.Image.Width, cursor.Image.Height,
                               cursor.HotspotX, cursor.HotspotY);
            _timeline?.Event("cursor-set", new() { ["resource"] = resourceId, ["asset"] = asset.Name });
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"cursor decode {asset.Name}: {ex.Message}");
        }
    }

    public void ClearCursorResource()
    {
        _main.CallDeferred("ClearAgeCursor");
        _timeline?.Event("cursor-clear", new());
    }

    public void WarpCursor(int virtualX, int virtualY)
    {
        // Script execution is on the VM thread; Godot input/display APIs belong to the main thread.
        _main.CallDeferred("WarpAgeCursor", virtualX, virtualY);
        _timeline?.Event("cursor-warp", new() { ["x"] = virtualX, ["y"] = virtualY });
    }

    public void WaitForForegroundTransition(GfxState gfx)
    {
        _foregroundTransitionWaitBypassed = false;
        int started = gfx.StartForegroundTransitions(_clock.NowMs);
        bool hasActivePresentation =
            gfx.HasActiveTimedPresentation(_clock.NowMs) || HasActiveMoviePresentation();
        if (hasActivePresentation)
        {
            _foregroundGfx = gfx;
            System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, _clock.NowMs);
            IsTransitionWaiting = true;
            _timeline?.State("transition-start", new() { ["count"] = started });
            // Skip may already have been active before this service boundary. Native ADV script usually
            // selects 0x243+0x20c in that case, but applying it here also covers transition helpers that
            // enter run-state 0x400 without repeating the script-side query.
            if (_messageSkipActive) TryForceActiveTransition("message-skip");
        }
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
            if (!hasActivePresentation) return;
            int lastBucket = -1;
            while (!_foregroundTransitionWaitBypassed
                   && (gfx.HasActiveTimedPresentation(_clock.NowMs) || HasActiveMoviePresentation())
                   && !_stopping)
            {
                var active = gfx.SnapshotForegroundTransitions(_clock.NowMs);
                var radial = gfx.SnapshotRadialBlurRangeTransitions(_clock.NowMs);
                double progress = active.Count != 0 ? active[0].Progress
                    : radial.Count != 0 ? radial[0].Progress : 1.0;
                bool forced = active.Count != 0 ? active[0].Forced
                    : radial.Count != 0 && radial[0].Forced;
                int bucket = (int)System.Math.Floor(progress * 10);
                if (bucket != lastBucket)
                {
                    lastBucket = bucket;
                    _timeline?.State("transition-progress", new()
                    {
                        ["progress"] = progress,
                        ["forced"] = forced,
                    });
                }
                _frameSignal.WaitOne(50);
            }
            // Publish the natural terminal sample or the forced service-resume sample once before the
            // following script burst mutates/releases its retained inputs.
            RequestSynchronizedPresentation();
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        IsTransitionWaiting = false;
        _foregroundTransitionWaitBypassed = false;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, -1);
        _foregroundGfx = null;
        _timeline?.State("running", new() { ["transition_complete"] = true });
    }

    public void PresentFrame(GfxState gfx)
    {
        if (gfx.CurrentRenderTargetSlot >= 0)
        {
            int slot = gfx.CurrentRenderTargetSlot;
            var snapshot = gfx.SnapshotVisibleObjects(_clock.NowMs);
            PublishObjectRangeToSurface(gfx, 0, long.MaxValue, snapshot);
            lock (_screenTransitionLock) _renderTargetSnapshots[slot] = snapshot;
            _timeline?.Event("render-target-snapshot", new()
            {
                ["surface"] = slot, ["objects"] = snapshot.Count,
            });
            return;
        }
        lock (_backbufferRangeLock)
        {
            _backbufferRange = GfxHandleRange.All;
            // Native gfx_render_frame draws every visible retained object over the current target.
            // It does not clear first; backbuffer clearing is the separate explicit opcode 0x20e.
            _backbufferPublicationPreservesExistingPixels = true;
        }
        int started = gfx.StartForegroundTransitions(_clock.NowMs);
        int completed = gfx.CompleteForegroundTransitions(_clock.NowMs);
        if (started > 0 || completed > 0)
            _timeline?.State("transition-skip-complete", new()
            {
                ["started"] = started, ["completed"] = completed,
            });
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
    }

    public void FadeSurfaceWithBlack(
        GfxState gfx, int surface, long intervalArgument, SurfaceBlackFadeDirection direction,
        bool forceEndpoint = false)
    {
        long start = _clock.NowMs;
        IReadOnlyList<RenderObject> captured;
        lock (_screenTransitionLock)
            captured = _renderTargetSnapshots.TryGetValue(surface, out var snapshot)
                ? snapshot : System.Array.Empty<RenderObject>();

        IReadOnlyList<RenderObject> black = System.Array.Empty<RenderObject>();
        IReadOnlyList<RenderObject> source =
            direction == SurfaceBlackFadeDirection.FromBlack ? black : captured;
        IReadOnlyList<RenderObject> target =
            direction == SurfaceBlackFadeDirection.FromBlack ? captured : black;
        RunLegacyScreenTransition(source, target, start, intervalArgument, forceEndpoint, new()
        {
            ["kind"] = "surface-black-fade",
            ["surface"] = surface,
            ["direction"] = direction == SurfaceBlackFadeDirection.FromBlack
                ? "from-black" : "to-black",
        });
    }

    public void CrossfadeSurfaces(
        GfxState gfx, int sourceSurface, int targetSurface, long intervalArgument,
        bool forceEndpoint = false)
    {
        IReadOnlyList<RenderObject> source;
        IReadOnlyList<RenderObject> target;
        long start = _clock.NowMs;
        lock (_screenTransitionLock)
        {
            source = _renderTargetSnapshots.TryGetValue(sourceSurface, out var capturedSource)
                ? capturedSource : System.Array.Empty<RenderObject>();
            target = _renderTargetSnapshots.TryGetValue(targetSurface, out var capturedTarget)
                ? capturedTarget : gfx.SnapshotVisibleObjects(start);
        }
        RunLegacyScreenTransition(source, target, start, intervalArgument, forceEndpoint, new()
        {
            ["kind"] = "surface-crossfade",
            ["source"] = sourceSurface,
            ["target"] = targetSurface,
        });
    }

    public void FadeSurfaceToWhite(
        GfxState gfx, int surface, long intervalArgument, bool forceEndpoint = false)
    {
        long start = _clock.NowMs;
        IReadOnlyList<RenderObject> captured;
        lock (_screenTransitionLock)
            captured = _renderTargetSnapshots.TryGetValue(surface, out var snapshot)
                ? snapshot : System.Array.Empty<RenderObject>();

        RunLegacyScreenTransition(
            captured, System.Array.Empty<RenderObject>(), start, intervalArgument, forceEndpoint,
            new()
            {
                ["kind"] = "surface-white-fade",
                ["surface"] = surface,
                ["direction"] = "to-white",
            }, overlayRgb: 0xffffff);
    }

    // The native 0x21/0x22/0x25 family advances an 8-bit alpha accumulator. Values <=64 use
    // the operand as the timer interval and step alpha by sixteen; larger values divide the
    // interval by sixteen and step alpha by one. This reproduces its blocking wall-clock duration.
    private void RunLegacyScreenTransition(
        IReadOnlyList<RenderObject> source, IReadOnlyList<RenderObject> target,
        long start, long intervalArgument, bool forceEndpoint,
        Dictionary<string, object?> timelineDetail, long overlayRgb = 0)
    {
        long duration = LegacyScreenTransitionTiming.DurationMilliseconds(intervalArgument);
        lock (_screenTransitionLock)
            _screenTransition = new LegacyScreenTransition(source, target, start, duration, overlayRgb)
            {
                Forced = forceEndpoint,
            };
        _foregroundGfx = null;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, start);
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        IsTransitionWaiting = true;
        timelineDetail["interval_argument"] = intervalArgument;
        timelineDetail["duration_ms"] = duration;
        timelineDetail["source_objects"] = source.Count;
        timelineDetail["target_objects"] = target.Count;
        timelineDetail["forced"] = forceEndpoint;
        _timeline?.State("screen-transition-start", timelineDetail);
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
            while (!_stopping)
            {
                bool complete;
                lock (_screenTransitionLock)
                    complete = _screenTransition == null || _screenTransition.Forced
                        || _clock.NowMs - start >= duration;
                if (complete) break;
                _frameSignal.WaitOne(50);
            }
            // Publish the exact target endpoint before the following surface releases/root reload.
            lock (_screenTransitionLock)
                if (_screenTransition != null) _screenTransition.Forced = true;
            RequestSynchronizedPresentation();
            // Do not let the VM release both source surfaces (or immediately reload SYSTEM4) until the
            // main thread has actually published the terminal target frame.
            while (!_stopping)
            {
                lock (_screenTransitionLock)
                    if (_screenTransition == null) break;
                _frameSignal.WaitOne(50);
            }
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        IsTransitionWaiting = false;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, -1);
        _timeline?.State("running", new() { ["screen_transition_complete"] = true });
    }

    public bool TrySnapshotScreenTransition(out LegacyScreenTransitionSnapshot snapshot)
    {
        lock (_screenTransitionLock)
        {
            if (_screenTransition == null)
            {
                snapshot = default;
                return false;
            }
            double progress = _screenTransition.Forced ? 1.0
                : System.Math.Clamp((_clock.NowMs - _screenTransition.StartMs)
                    / (double)_screenTransition.DurationMs, 0.0, 1.0);
            snapshot = new LegacyScreenTransitionSnapshot(
                _screenTransition.Source, _screenTransition.Target, progress,
                _screenTransition.OverlayRgb);
            if (_screenTransition.Forced) _screenTransition = null;
            return true;
        }
    }

    // Native retained-object writes are not front-buffer writes. Publish explicit/service-boundary dirtiness
    // once, then continue only while the sampled retained scene can actually change. The wait atlas advances
    // its ordinary retained source cell here so it contributes only when the frame actually changes.
    public HostPresentationReason ConsumePresentationReasons(GfxState gfx)
    {
        UpdateRetainedWaitIndicator(gfx);
        bool screenTransitionActive;
        lock (_screenTransitionLock) screenTransitionActive = _screenTransition != null;
        var reasons = HostPresentationReason.None;
        if (System.Threading.Interlocked.Exchange(ref _presentRequested, 0) != 0)
        {
            reasons |= HostPresentationReason.HostRequest;
            long requested = Interlocked.Read(ref _explicitPresentationRequestGeneration);
            Interlocked.Exchange(ref _consumedPresentationRequestGeneration, requested);
            _presentationRequestConsumed.Set();
        }
        if (screenTransitionActive) reasons |= HostPresentationReason.ScreenTransition;
        GfxPresentationReason gfxReasons = gfx.ConsumePresentationReasons(_clock.NowMs);
        if ((gfxReasons & GfxPresentationReason.RetainedMutation) != 0)
            reasons |= HostPresentationReason.RetainedMutation;
        if ((gfxReasons & GfxPresentationReason.ContinuousChannel) != 0)
            reasons |= HostPresentationReason.ContinuousChannel;
        if ((gfxReasons & GfxPresentationReason.DiscreteSourceCell) != 0)
            reasons |= HostPresentationReason.DiscreteSourceCell;
        return reasons;
    }

    public void Stop()
    {
        _stopping = true;
        _main.CallDeferred("ClearAgeCursor");
        lock (_textLock) _advTextForceComplete = true;
        if (_gate.CurrentCount == 0) _gate.Release();
        _inputCallbackSignal.Set();
        _frameSignal.Set();
        _diagnosticMessageCompleted.Set();
        _fullwidthTextEditCompleted.Set();
    }

    public void ResetSceneContext()
    {
        // scene_context_init_reset releases ordinary surface/movie bindings but keeps decoded asset
        // caches and process-owned audio/configuration available to the reloaded SYSTEM4 root.
        ReleaseSurfaceRange(0, 1000);
        lock (_backbufferRangeLock)
        {
            _backbufferRange = GfxHandleRange.All;
            _backbufferPublicationPreservesExistingPixels = false;
            _backbufferClearPending = false;
        }
        lock (_textLock)
        {
            _surfaceResources.Clear();
            _retainedHistoryLayouts.Clear();
            _liveText.Clear();
            _retainedTextLayouts.Clear();
            _activeLiveText = null;
            _suspendedRetainedTextLayoutSlot = 0;
            _advText = "";
            _advTextX = 100;
            _advTextY = 47;
            _advTextForceComplete = false;
            _waitIndicators.Clear();
            _waitIndicatorBindings.Clear();
            _retainedWaitIndicators.Clear();
            _activeWaitLayout = 0;
            _waitIndicatorEnabled = false;
        }
        while (_gate.Wait(0)) { }
        _inputCallbackSignal.WaitOne(0);
        lock (_messageSkipLock)
        {
            _scriptMessageSkipActive = false;
            _physicalMessageSkipActive = false;
            _messageSkipActive = false;
            _queuedSkippedVoice = null;
        }
        lock (_scheduledVoiceLock) _scheduledVoice = null;
        System.Threading.Volatile.Write(ref _voiceBgmDuckControl, 0);
        _advPagePresentationSuspended = false;
        _modalMovieCancelled = false;
        _modalMovieWaiting = false;
        _foregroundGfx = null;
        lock (_screenTransitionLock)
        {
            _renderTargetSnapshots.Clear();
            _screenTransition = null;
        }
        IsWaiting = false;
        IsTransitionWaiting = false;
        IsSleeping = false;
        IsTextRevealing = false;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, -1);
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        _main.CallDeferred("CancelScheduledSoundEffectStarts");
        _main.CallDeferred("ClearPage");
        _timeline?.State("scene-context-reset", new());
    }

    // Main thread, once per rendered frame: releases a VM thread parked in Sleep or a presentation/input wait.
    public void PulseFrame()
    {
        (AudioPayload Audio, int PlaybackVariant)? due = null;
        lock (_scheduledVoiceLock)
        {
            if (_scheduledVoice is { } pending)
            {
                uint now = unchecked((uint)_clock.NowMs);
                if (!pending.StartMs.HasValue)
                    _scheduledVoice = pending with { StartMs = now };
                else if (unchecked(now - pending.StartMs.Value) >= pending.DelayMs)
                {
                    due = (pending.Audio, pending.PlaybackVariant);
                    _scheduledVoice = null;
                }
            }
        }
        if (due.HasValue)
        {
            _timeline?.Event("voice-scheduled-start", new()
            {
                ["file"] = due.Value.Audio.Name, ["playback_variant"] = due.Value.PlaybackVariant,
            });
            DispatchVoice(due.Value.Audio, due.Value.PlaybackVariant);
        }
        _frameSignal.Set();
    }

    // Native dispatch remains burst-fast between explicit presentation/sleep/input services even when
    // message Skip removes those services. Per-op frame pacing turns ordinary skipped setup/cleanup bursts
    // into multi-second invisible stalls.
    public void FrameYield() { }

    // op 0xc8: block the VM background thread while the main-thread compositor keeps presenting retained state.
    // Time-based sibling of WaitForInput's suspend. The native op arms a non-blocking main-loop-polled timer;
    // blocking this throwaway task thread is behaviorally equivalent given our threading model. Operand is
    // MILLISECONDS (docs/engine-re.md sleep section + opcodes.toml 0xc8). Headless CLI hosts no-op it (parity).
    public double SleepScale = 1.0;   // --sleep-scale <f>: debug multiplier for explicit op-0xc8 holds only
    // Wait on the unified FrameClock timebase (not Thread.Sleep) so the Speed multiplier scales
    // sleeps together with retained presentation clocks. Main._Process advances the clock + pulses each frame.
    internal static long NormalizeSleepMilliseconds(long duration, double scale)
        => (long)System.Math.Clamp(duration * scale, 1, 60_000);

    public void Sleep(long duration)
    {
        // Native sleep_timer_arm clamps the duration to at least 1 ms. In menu poll loops, sleep(0)
        // therefore yields to the next engine tick instead of becoming a free-running no-op.
        long ms = NormalizeSleepMilliseconds(duration, SleepScale);
        long deadline = _clock.NowMs + ms;
        _timeline?.State("sleep", new() { ["duration_ms"] = ms, ["deadline_ms"] = deadline });
        IsSleeping = true;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
            while (_clock.NowMs < deadline)
            {
                if (_stopping) break;
                _frameSignal.WaitOne(50);
            }
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        IsSleeping = false;
        _timeline?.State("running", new() { ["sleep_complete"] = true });
    }

    public void WaitForTimedCallbackDeadline(long duration)
    {
        long ms = NormalizeSleepMilliseconds(duration, 1.0);
        long deadline = _clock.NowMs + ms;
        _timeline?.State("timed-callback-wait",
            new() { ["duration_ms"] = ms, ["deadline_ms"] = deadline });
        // Keep the script-side presentation barrier held. Native run-state 0x40 advances the timer
        // and message pump but suppresses ordinary retained rendering until the callback's explicit 0x222.
        while (_clock.NowMs < deadline && !_stopping)
            _frameSignal.WaitOne(50);
        _timeline?.State("running", new() { ["timed_callback_due"] = true });
    }

    // ---- texture ops (run on the VM thread; marshal Godot node work to the main thread) ----
}

public readonly record struct LegacyScreenTransitionSnapshot(
    IReadOnlyList<RenderObject> Source, IReadOnlyList<RenderObject> Target, double Progress,
    long OverlayRgb);

internal sealed class LegacyScreenTransition
{
    public IReadOnlyList<RenderObject> Source { get; }
    public IReadOnlyList<RenderObject> Target { get; }
    public long StartMs { get; }
    public long DurationMs { get; }
    public long OverlayRgb { get; }
    public bool Forced { get; set; }

    public LegacyScreenTransition(IReadOnlyList<RenderObject> source, IReadOnlyList<RenderObject> target,
                                  long startMs, long durationMs, long overlayRgb)
    {
        Source = source;
        Target = target;
        StartMs = startMs;
        DurationMs = durationMs;
        OverlayRgb = overlayRgb;
    }
}
