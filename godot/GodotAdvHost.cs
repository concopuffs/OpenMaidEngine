using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;

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

public sealed class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _rootScene;
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
    private readonly object _imageLock = new();
    private readonly Dictionary<int, RgbaImage?> _images = new();        // packed catalog id -> decoded pixels
    // Mutable AGE surfaces are published by replacing immutable RgbaImage snapshots, so the compositor
    // can safely finish reading an old frame while the VM prepares a copied-rectangle update.
    private readonly Dictionary<int, RgbaImage> _surfaceImages = new();
    private readonly Dictionary<int, long> _surfaceColorKeys = new();
    private readonly Dictionary<int, long> _surfaceResources = new();    // surface slot -> packed catalog id
    private readonly MovieSurfaceRegistry _movieSurfaces = new();
    private sealed record MovieMaskPlayback(
        GfxState Gfx, MovieMaskTransitionRequest Request, RgbaImage Captured);
    private readonly object _movieMaskLock = new();
    private readonly Dictionary<long, MovieMaskPlayback> _movieMasksByPlayback = new();
    private readonly Dictionary<int, long> _movieMaskPlaybackBySurface = new();
    private readonly string?[] _sfxNames = new string?[10];              // SC0000 native channel subset
    // slot -> dimensions of the currently allocated surface. Slot 0 begins as the selected game's
    // logical canvas, but op 0x1fa releases it like any other slot; subsequent queries return 0x0.
    private readonly Dictionary<int, (int W, int H)> _slotDims = new();
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly SemaphoreSlim _gate = new(0, 1);
    private readonly AutoResetEvent _inputCallbackSignal = new(false);
    private readonly Age.Engine.Hosting.FrameClock _clock;
    private readonly GodotTimelineLog? _timeline;
    private readonly PageLocatorState _locator;
    private readonly System.Threading.AutoResetEvent _frameSignal = new(false);
    private volatile bool _stopping;
    private readonly object _textLock = new();
    private readonly Dictionary<int, List<SurfaceTextDraw>> _surfaceText = new();
    private readonly Dictionary<int, AdvTextHistoryRenderBatch> _historyText = new();
    private sealed class LiveTextState
    {
        public required AdvLiveTextRun Run;
        public required long StartedMs;
        public required int GlyphDelayMilliseconds;
    }
    private readonly List<LiveTextState> _liveText = new();
    private LiveTextState? _activeLiveText;
    private string _advText = "";
    private int _advTextX = 100, _advTextY = 47;
    private int _currentAdvLayout = 1; // SYSTEM4's ordinary SC0000 ADV layout
    private long _advTextStartedMs;
    private int _activeGlyphDelayMilliseconds = 50;
    private int _messageGlyphDelayMilliseconds = 50;
    private volatile int _messageWindowAlphaSetting;
    private bool _advTextForceComplete;
    private readonly Dictionary<int, AdvWaitIndicatorConfig> _waitIndicators = new();
    private readonly object _messageSkipLock = new();
    private bool _scriptMessageSkipActive;
    private bool _physicalMessageSkipActive;
    private volatile bool _messageSkipActive;
    private int _voiceBgmDuckControl;
    private (AudioPayload Audio, int PlaybackVariant)? _queuedSkippedVoice;
    private readonly object _scheduledVoiceLock = new();
    private (AudioPayload Audio, int PlaybackVariant, uint DelayMs, uint? StartMs)? _scheduledVoice;
    private int _activeWaitLayout;
    private long _waitIndicatorStartedMs;
    private bool _waitIndicatorEnabled;
    private volatile bool _advPagePresentationSuspended;
    private volatile bool _modalMovieWaiting;
    private volatile bool _modalMovieCancelled;
    private GfxState? _foregroundGfx;
    private volatile bool _foregroundTransitionWaitBypassed;
    private readonly object _screenTransitionLock = new();
    private readonly Dictionary<int, IReadOnlyList<RenderObject>> _renderTargetSnapshots = new();
    private readonly object _backbufferRangeLock = new();
    private GfxHandleRange _backbufferRange = GfxHandleRange.All;
    private LegacyScreenTransition? _screenTransition;
    public volatile bool IsWaiting;
    public volatile bool IsTransitionWaiting;
    public volatile bool IsSleeping;
    public volatile bool IsTextRevealing;
    public bool IsModalMovieWaiting => _modalMovieWaiting;
    private int _presentRequested = 1;
    private long _transitionStartedAtMs = -1;
    public long TransitionStartedAtMs => System.Threading.Interlocked.Read(ref _transitionStartedAtMs);
    public readonly List<(int Offset, string Text)> Captured = new();

    public GodotAdvHost(Main main, ResourceMap res, string scene, Age.Engine.Hosting.FrameClock clock,
                        PageLocatorState locator, Sys4LogicalCanvas logicalCanvas,
                        GodotTimelineLog? timeline = null,
                        bool synchronizeExplicitPresentation = true)
    {
        _main = main; _res = res; _rootScene = scene; _clock = clock;
        _locator = locator; _timeline = timeline;
        _screenWidth = logicalCanvas.Width;
        _screenHeight = logicalCanvas.Height;
        _slotDims[0] = (_screenWidth, _screenHeight);
        _synchronizeExplicitPresentation = synchronizeExplicitPresentation;
    }

    public Sys4LogicalCanvas LogicalCanvas => new(_screenWidth, _screenHeight);

    public void ReportWarning(string message) => System.Console.Error.WriteLine(message);

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

    public int MessageGlyphDelayMilliseconds
    {
        get
        {
            lock (_textLock) return _messageGlyphDelayMilliseconds;
        }
    }

    public void SetMessageGlyphDelayMilliseconds(int milliseconds)
    {
        lock (_textLock) _messageGlyphDelayMilliseconds = System.Math.Max(0, milliseconds);
        _timeline?.Event("message-glyph-delay", new() { ["delay_ms"] = milliseconds });
    }

    public void ShowText(int offset, string text)
    {
        AdvTextLayoutSnapshot layout;
        int delay;
        lock (_textLock)
        {
            layout = new AdvTextLayoutSnapshot(
                _currentAdvLayout, _screenWidth, _screenHeight, 0, 0,
                _advTextX, _advTextY, _screenWidth, _screenHeight);
            delay = _messageGlyphDelayMilliseconds;
        }
        ShowText(new AdvLiveTextRun(
            offset, layout, AdvTextStyle.Default, text, Array.Empty<string>()), delay);
    }

    public void ShowText(AdvLiveTextRun run, int glyphDelayMilliseconds)
    {
        Captured.Add((run.SourceOffset, run.Text));
        _locator.Text(run.SourceOffset, run.Text);
        int delay = System.Math.Max(0, glyphDelayMilliseconds);
        var state = new LiveTextState
        {
            Run = run,
            StartedMs = _clock.NowMs,
            GlyphDelayMilliseconds = delay,
        };
        lock (_textLock)
        {
            _liveText.Add(state);
            _activeLiveText = state;
            _advText = run.Text;
            _advTextX = run.Layout.CursorX;
            _advTextY = run.Layout.CursorY;
            _currentAdvLayout = run.Layout.Slot;
            _advTextStartedMs = state.StartedMs;
            _activeGlyphDelayMilliseconds = delay;
            _advTextForceComplete = _messageSkipActive;
            IsTextRevealing = run.Text.Length > 0 && delay > 0 && !_messageSkipActive;
        }
        _timeline?.State("text-reveal", new()
        {
            ["offset"] = $"0x{run.SourceOffset:x}",
            ["layout"] = run.Layout.Slot,
            ["x"] = run.Layout.OriginX + run.Layout.CursorX,
            ["y"] = run.Layout.OriginY + run.Layout.CursorY,
            ["glyphs"] = run.Text.Length, ["delay_ms"] = delay,
        });
        if (!IsTextRevealing)
        {
            Interlocked.Exchange(ref _presentRequested, 1);
            _timeline?.State("running", new() { ["text_reveal_complete"] = true });
            return;
        }
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
            while (IsTextRevealing && !_stopping)
            {
                lock (_textLock)
                {
                    if (_advTextForceComplete
                        || _clock.NowMs - _advTextStartedMs >= run.Text.Length * (long)delay)
                        IsTextRevealing = false;
                }
                if (IsTextRevealing) _frameSignal.WaitOne(50);
            }
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _timeline?.State("running", new() { ["text_reveal_complete"] = true });
    }

    public void SetAdvTextCursor(int layoutSlot, int x, int y)
    {
        lock (_textLock)
        {
            if (layoutSlot != 0) _currentAdvLayout = layoutSlot;
            _advTextX = x;
            _advTextY = y;
        }
        _timeline?.Event("text-cursor", new() { ["slot"] = layoutSlot, ["x"] = x, ["y"] = y });
    }

    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text)
        => DrawStringToSurface(surfaceSlot, x, y, text, AdvTextStyle.Default);

    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text, AdvTextStyle style)
    {
        lock (_textLock)
        {
            if (!_surfaceText.TryGetValue(surfaceSlot, out var draws))
                _surfaceText[surfaceSlot] = draws = new List<SurfaceTextDraw>();
            draws.RemoveAll(draw => draw.X == x && draw.Y == y);
            draws.Add(new SurfaceTextDraw(x, y, text, style));
        }
        _timeline?.Event("draw-string", new() { ["surface"] = surfaceSlot, ["x"] = x, ["y"] = y, ["text"] = text });
    }

    public (string Text, int X, int Y, int VisibleGlyphs, bool Revealing) SnapshotAdvText()
    {
        lock (_textLock)
        {
            int visible = _advTextForceComplete || !IsTextRevealing
                ? _advText.Length
                : (int)System.Math.Clamp(
                    (_clock.NowMs - _advTextStartedMs) / System.Math.Max(1, _activeGlyphDelayMilliseconds) + 1,
                    0, _advText.Length);
            return (_advText, _advTextX, _advTextY, visible, IsTextRevealing);
        }
    }

    public IReadOnlyList<LiveAdvTextSnapshot> SnapshotLiveAdvText()
    {
        lock (_textLock)
        {
            var snapshot = new LiveAdvTextSnapshot[_liveText.Count];
            for (int i = 0; i < _liveText.Count; i++)
            {
                var state = _liveText[i];
                bool revealing = ReferenceEquals(state, _activeLiveText) && IsTextRevealing;
                int visible = !revealing || _advTextForceComplete || state.GlyphDelayMilliseconds == 0
                    ? state.Run.Text.Length
                    : (int)System.Math.Clamp(
                        (_clock.NowMs - state.StartedMs) / state.GlyphDelayMilliseconds + 1,
                        0, state.Run.Text.Length);
                snapshot[i] = new LiveAdvTextSnapshot(state.Run, visible, revealing);
            }
            return snapshot;
        }
    }

    /// <summary>
    /// Layout that owns the ordinary ADV overlay. Nested callback scripts such as HISTORY can select and
    /// mutate other layouts while the parent wait remains parked; those transient selections must not move
    /// the parent page when its overlay becomes visible again.
    /// </summary>
    public int AdvPageLayoutSlot
    {
        get
        {
            lock (_textLock)
                return IsWaiting && _activeWaitLayout != 0 ? _activeWaitLayout : _currentAdvLayout;
        }
    }

    public IReadOnlyList<SurfaceTextDraw> SnapshotSurfaceText(int surfaceSlot)
    {
        lock (_textLock)
            return _surfaceText.TryGetValue(surfaceSlot, out var draws) ? draws.ToArray() : Array.Empty<SurfaceTextDraw>();
    }

    public void SnapshotSurfaceText(int surfaceSlot, List<SurfaceTextDraw> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_textLock)
        {
            snapshot.Clear();
            if (_surfaceText.TryGetValue(surfaceSlot, out var draws)) snapshot.AddRange(draws);
        }
    }

    public void ClearRenderedAdvTextLayout(int layoutSlot)
    {
        lock (_textLock)
        {
            int slot = layoutSlot == 0 ? _currentAdvLayout : layoutSlot;
            _historyText.Remove(slot);
            _liveText.RemoveAll(state => state.Run.Layout.Slot == slot);
            if (_activeLiveText?.Run.Layout.Slot == slot)
            {
                _activeLiveText = null;
                _advText = "";
                _advTextForceComplete = false;
                IsTextRevealing = false;
            }
        }
    }

    public void RenderTextHistory(AdvTextHistoryRenderBatch batch)
    {
        lock (_textLock) _historyText[batch.LayoutSlot] = batch;
        _timeline?.Event("history-render", new()
        {
            ["layout"] = batch.LayoutSlot, ["record"] = batch.FirstRecordIndex,
            ["x"] = batch.Layout.OriginX + batch.Layout.CursorX,
            ["y"] = batch.Layout.OriginY + batch.Layout.CursorY,
            ["text"] = batch.Text,
        });
    }

    public void EndTextHistoryPresentation()
    {
        lock (_textLock) _historyText.Clear();
        _timeline?.Event("history-presentation-end");
    }

    public IReadOnlyList<AdvTextHistoryRenderBatch> SnapshotRenderedTextHistory()
    {
        lock (_textLock) return _historyText.Values.OrderBy(batch => batch.LayoutSlot).ToArray();
    }

    public int MessageWindowAlphaSetting => _messageWindowAlphaSetting;

    public void SetMessageWindowAlphaSetting(int value)
    {
        _messageWindowAlphaSetting = value;
        _timeline?.Event("message-window-alpha", new() { ["value"] = value });
    }

    public void FillSurfaceRect(SurfaceRectFill fill)
    {
        lock (_textLock)
        {
            if (_surfaceText.TryGetValue(fill.SurfaceSlot, out var draws))
            {
                long right = (long)fill.X + System.Math.Max(0, fill.Width);
                long bottom = (long)fill.Y + System.Math.Max(0, fill.Height);
                draws.RemoveAll(draw => draw.X >= fill.X && draw.X < right
                    && draw.Y >= fill.Y && draw.Y < bottom);
            }
        }

        RgbaImage? destination = ResolveSurfacePixels(fill.SurfaceSlot);
        if (destination == null && _slotDims.TryGetValue(fill.SurfaceSlot, out var dimensions)
            && dimensions.W >= 0 && dimensions.H >= 0)
            destination = new RgbaImage(dimensions.W, dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
        if (destination != null)
        {
            var updated = new RgbaImage(destination.Width, destination.Height,
                (byte[])destination.Pixels.Clone());
            if (RgbaSurfaceOps.FillRect(updated, fill.X, fill.Y, fill.Width, fill.Height,
                                        unchecked((byte)fill.Alpha), fill.Rgb))
            {
                lock (_imageLock) _surfaceImages[fill.SurfaceSlot] = updated;
                System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
            }
        }
        _timeline?.Event("surface-fill", new()
        {
            ["surface"] = fill.SurfaceSlot, ["x"] = fill.X, ["y"] = fill.Y,
            ["w"] = fill.Width, ["h"] = fill.Height, ["alpha"] = fill.Alpha, ["rgb"] = fill.Rgb,
        });
    }

    public void PresentObjectRange(GfxState gfx, long firstHandle, long count)
    {
        if (gfx.CurrentRenderTargetSlot >= 0)
        {
            PublishObjectRangeToSurface(gfx, firstHandle, count);
            return;
        }
        // The software/GPU port reconstructs the backbuffer instead of preserving native D3D pixels.
        // Zero-based ranges therefore define the complete published scene. A nonzero range is an
        // incremental overlay in native code, so retain the full reconstruction for those call sites.
        lock (_backbufferRangeLock)
            _backbufferRange = firstHandle == 0
                ? new GfxHandleRange(firstHandle, count)
                : GfxHandleRange.All;
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("present-object-range", new() { ["first"] = firstHandle, ["count"] = count });
    }

    public void SnapshotBackbufferObjects(GfxState gfx, long nowMs, List<RenderObject> snapshot)
    {
        gfx.SnapshotVisibleObjects(nowMs, snapshot);
        GfxHandleRange range;
        lock (_backbufferRangeLock) range = _backbufferRange;
        int write = 0;
        for (int read = 0; read < snapshot.Count; read++)
            if (range.Contains(snapshot[read].Handle)) snapshot[write++] = snapshot[read];
        if (write < snapshot.Count) snapshot.RemoveRange(write, snapshot.Count - write);
    }

    public void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config)
    {
        lock (_textLock) _waitIndicators[config.LayoutSlot] = config;
        _timeline?.Event("wait-indicator-config", new()
        {
            ["layout"] = config.LayoutSlot, ["x"] = config.X, ["y"] = config.Y,
            ["surface"] = config.SurfaceSlot, ["cell_w"] = config.CellWidth,
            ["cell_h"] = config.CellHeight, ["terminal_frame"] = config.TerminalFrame,
            ["period_ms"] = config.FramePeriodMs,
        });
    }

    public void SetAdvWaitIndicatorEnabled(bool enabled)
    {
        lock (_textLock)
        {
            if (enabled && !_waitIndicatorEnabled) _waitIndicatorStartedMs = _clock.NowMs;
            _waitIndicatorEnabled = enabled;
        }
        _timeline?.Event("wait-indicator-enabled", new() { ["enabled"] = enabled });
    }

    public void PublishAdvTextLayout(int layoutSlot)
    {
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("adv-text-layout-publish", new() { ["layout"] = layoutSlot });
    }

    public AdvWaitIndicatorSnapshot? SnapshotAdvWaitIndicator()
    {
        if (!IsWaiting || _advPagePresentationSuspended) return null;
        AdvWaitIndicatorConfig config;
        long resourceId;
        lock (_textLock)
        {
            if (!_waitIndicatorEnabled) return null;
            if (!_waitIndicators.TryGetValue(_activeWaitLayout, out config)) return null;
            if (!_surfaceResources.TryGetValue(config.SurfaceSlot, out resourceId)) return null;
        }
        var asset = _res.ResolveTexture(resourceId);
        var image = asset != null ? Decode(asset) : null;
        if (asset == null || image == null || config.CellWidth <= 0 || config.CellHeight <= 0) return null;
        int frame = config.FrameAt(_clock.NowMs - _waitIndicatorStartedMs);
        return new AdvWaitIndicatorSnapshot(image, asset.Name, asset.PackedId, config, frame);
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
                int bucket = active.Count == 0 ? 100 : (int)System.Math.Floor(active[0].Progress * 10);
                if (bucket != lastBucket)
                {
                    lastBucket = bucket;
                    _timeline?.State("transition-progress", new()
                    {
                        ["progress"] = active.Count == 0 ? 1.0 : active[0].Progress,
                        ["forced"] = active.Count != 0 && active[0].Forced,
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
        lock (_backbufferRangeLock) _backbufferRange = GfxHandleRange.All;
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

    // The native 0x21/0x22/0x25 family advances an 8-bit alpha accumulator. Values <=64 use
    // the operand as the timer interval and step alpha by sixteen; larger values divide the
    // interval by sixteen and step alpha by one. This reproduces its blocking wall-clock duration.
    private void RunLegacyScreenTransition(
        IReadOnlyList<RenderObject> source, IReadOnlyList<RenderObject> target,
        long start, long intervalArgument, bool forceEndpoint,
        Dictionary<string, object?> timelineDetail)
    {
        long duration = LegacyScreenTransitionTiming.DurationMilliseconds(intervalArgument);
        lock (_screenTransitionLock)
            _screenTransition = new LegacyScreenTransition(source, target, start, duration)
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
                _screenTransition.Source, _screenTransition.Target, progress);
            if (_screenTransition.Forced) _screenTransition = null;
            return true;
        }
    }

    // Native retained-object writes are not front-buffer writes. Publish explicit/service-boundary dirtiness
    // once, then continue only while the sampled retained scene can actually change. Text reveal is a separate
    // Godot Label; waiting/sleeping alone do not alter background pixels.
    public HostPresentationReason ConsumePresentationReasons(GfxState gfx)
    {
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
        lock (_backbufferRangeLock) _backbufferRange = GfxHandleRange.All;
        lock (_textLock)
        {
            _surfaceText.Clear();
            _surfaceResources.Clear();
            _historyText.Clear();
            _liveText.Clear();
            _activeLiveText = null;
            _advText = "";
            _advTextX = 100;
            _advTextY = 47;
            _advTextForceComplete = false;
            _waitIndicators.Clear();
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

    // ---- texture ops (run on the VM thread; marshal Godot node work to the main thread) ----
    public bool TraceOps;   // --gfx-log: print set-texture/create-texture slot assignments (diagnose slot collisions)

    public void CreateTexture(int slot, int width, int height)
    {
        lock (_textLock) _surfaceText.Remove(slot);
        lock (_textLock) _surfaceResources.Remove(slot);
        int safeWidth = System.Math.Max(0, width);
        int safeHeight = System.Math.Max(0, height);
        lock (_imageLock)
        {
            _surfaceImages[slot] = new RgbaImage(safeWidth, safeHeight,
                new byte[checked(safeWidth * safeHeight * 4)]);
            _surfaceColorKeys.Remove(slot);
        }
        _slotDims[slot] = (safeWidth, safeHeight);
        if (TraceOps) Godot.GD.Print($"[op] create-texture slot={slot} {width}x{height}");
    }

    public void SetTexture(long resourceId, int slot) => SetTexture(resourceId, slot, -1);

    public void SetTexture(long resourceId, int slot, long colorKey)
    {
        lock (_imageLock)
        {
            _surfaceImages.Remove(slot);
            _surfaceColorKeys[slot] = colorKey;
        }
        lock (_textLock)
        {
            _surfaceText.Remove(slot);
            _surfaceResources[slot] = resourceId;
        }
        var asset = _res.ResolveTexture(resourceId);
        var image = asset != null ? Decode(asset) : null;
        _slotDims[slot] = image != null ? (image.Width, image.Height) : (0, 0);
        if (TraceOps) Godot.GD.Print($"[op] set-texture slot={slot} resId=0x{resourceId:x} -> {(asset?.Name ?? "<none>")}");
    }

    // AGF is decoded synchronously on the VM thread so geometry queried immediately afterward sees real dims.
    public (int Width, int Height) GetTextureSize(int slot)
        => _slotDims.TryGetValue(slot, out var d) ? (d.W, d.H) : (0, 0);

    public RgbaImage? CaptureSurfacePixels(int slot)
    {
        RgbaImage? image = ResolveSurfacePixels(slot);
        return image == null
            ? null
            : new RgbaImage(image.Width, image.Height, (byte[])image.Pixels.Clone());
    }

    public bool ReplaceSurfacePixels(int slot, RgbaImage image)
    {
        if (image.Width <= 0 || image.Height <= 0
            || image.Pixels.Length != checked(image.Width * image.Height * 4))
            return false;
        lock (_imageLock)
        {
            _surfaceImages[slot] =
                new RgbaImage(image.Width, image.Height, (byte[])image.Pixels.Clone());
            _surfaceColorKeys.Remove(slot);
        }
        lock (_textLock)
        {
            _surfaceText.Remove(slot);
            _surfaceResources.Remove(slot);
        }
        _slotDims[slot] = (image.Width, image.Height);
        return true;
    }

    // Retained render model: draw-texture updates GfxState (object -> surface bind); Main._Process composites
    // the visible objects each frame in ascending-handle order. No immediate blit here.
    public void DrawTexture(int slot, int srcX, int srcY, int width, int height, int dstX, int dstY) { }

    public void CopySurfaceRect(SurfaceRectCopy copy)
    {
        RgbaImage? source = ResolveSurfacePixels(copy.SourceSurface);
        RgbaImage? destination = ResolveSurfacePixels(copy.DestinationSurface);
        if (destination == null && _slotDims.TryGetValue(copy.DestinationSurface, out var dimensions)
            && dimensions.W >= 0 && dimensions.H >= 0)
            destination = new RgbaImage(dimensions.W, dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
        if (source == null || destination == null)
        {
            ReportWarning($"surface copy unresolved source={copy.SourceSurface} destination={copy.DestinationSurface}");
            return;
        }

        var updated = new RgbaImage(destination.Width, destination.Height, (byte[])destination.Pixels.Clone());
        if (RgbaSurfaceOps.CopyRect(source, updated, copy.SourceX, copy.SourceY, copy.Width, copy.Height,
                                   copy.DestinationX, copy.DestinationY))
        {
            lock (_imageLock) _surfaceImages[copy.DestinationSurface] = updated;
            System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        }
        _timeline?.Event("surface-copy", new()
        {
            ["source"] = copy.SourceSurface, ["source_x"] = copy.SourceX, ["source_y"] = copy.SourceY,
            ["w"] = copy.Width, ["h"] = copy.Height, ["destination"] = copy.DestinationSurface,
            ["destination_x"] = copy.DestinationX, ["destination_y"] = copy.DestinationY,
        });
    }

    private RgbaImage? ResolveSurfacePixels(int slot)
    {
        lock (_imageLock)
            if (_surfaceImages.TryGetValue(slot, out var mutable)) return mutable;
        long resourceId;
        lock (_textLock)
            if (!_surfaceResources.TryGetValue(slot, out resourceId)) return null;
        var resolved = ResolveResIdTexture(resourceId);
        if (resolved == null) return null;
        long colorKey;
        lock (_imageLock) colorKey = _surfaceColorKeys.GetValueOrDefault(slot, -1);
        return RgbaSurfaceOps.WithColorKey(resolved.Value.Image, colorKey);
    }

    /// <summary>Resolve a gfx surface through universal packed addressing and decode it from the
    /// loose-first asset store.</summary>
    public (RgbaImage Image, string Name, int AssetId, bool IsDynamic)? ResolveResIdTexture(long resId)
    {
        if (_movieSurfaces.TryResolveResource(resId, out var movie) && movie != null)
            return (movie.Image, movie.Name, movie.AssetId, true);
        // Movie payloads use the same .AGF extension as still images. Do not misclassify the MPEG program
        // stream before its first frame or during the cleanup frame after its surface binding is detached.
        // Packed catalog identity is immutable, so a resource which entered the typed movie path remains
        // a movie even when it has no live playback.
        if (_movieSurfaces.IsKnownMovieResource(resId)) return null;
        var asset = _res.ResolveTexture(resId);
        var image = asset != null ? Decode(asset) : null;
        return asset != null && image != null ? (image, asset.Name, asset.PackedId, false) : null;
    }

    public (RgbaImage Image, string Name, int AssetId, bool IsDynamic)? ResolveSurfaceTexture(
        int surfaceSlot, long fallbackResourceId)
    {
        lock (_movieMaskLock)
            if (_movieMaskPlaybackBySurface.ContainsKey(surfaceSlot))
                lock (_imageLock)
                    if (_surfaceImages.TryGetValue(surfaceSlot, out var masked))
                        return (masked, $"<movie-mask:{surfaceSlot}>",
                            int.MinValue + surfaceSlot, true);
        if (_movieSurfaces.TryResolveSurface(surfaceSlot, out var movie) && movie != null)
            return (movie.Image, movie.Name, movie.AssetId, true);
        if (_movieSurfaces.IsBound(surfaceSlot)) return null;
        lock (_imageLock)
            if (_surfaceImages.TryGetValue(surfaceSlot, out var surface))
                return (surface, $"<surface:{surfaceSlot}>", int.MinValue + surfaceSlot, true);
        return fallbackResourceId != 0 ? ResolveResIdTexture(fallbackResourceId) : null;
    }

    public bool IsMovieSurfaceBound(int surfaceSlot) => _movieSurfaces.IsBound(surfaceSlot);

    public long? PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask)
    {
        string scene = CurrentScene;
        var asset = _res.ResolveMovie(resourceId);
        if (asset == null) { Godot.GD.Print($"movie unresolved {scene}:0x{resourceId:x}"); return null; }
        StartMovie(asset, resourceId, surfaceSlot, movieFlags, syncMask, modal: false,
                   initialPositionMs: 0, startDelayMs: 0, presentationDurationMs: null,
                   movieMask: null, out long? stopTimeMs, out _);
        return stopTimeMs ?? 0;
    }

    public long? PlayMovieToSurfaceAtPosition(
        long resourceId, int surfaceSlot, long movieFlags, long syncMask, long positionMs)
    {
        string scene = CurrentScene;
        var asset = _res.ResolveMovie(resourceId);
        if (asset == null) { Godot.GD.Print($"movie unresolved {scene}:0x{resourceId:x}"); return null; }
        StartMovie(asset, resourceId, surfaceSlot, movieFlags, syncMask, modal: false,
                   initialPositionMs: positionMs, startDelayMs: 0, presentationDurationMs: null,
                   movieMask: null, out long? stopTimeMs, out _);
        return stopTimeMs ?? 0;
    }

    public void PlayMovieMaskTransition(GfxState gfx, MovieMaskTransitionRequest request)
    {
        gfx.QueueMovieMaskTransition(request);
        RgbaImage captured = CaptureRetainedRange(
            gfx, request.SurfaceSlot, request.SourceRangeStart, request.SourceRangeCount);
        byte initialFill = request.Mode == 1 ? (byte)0 : (byte)255;
        PublishMaskedCapture(
            request.SurfaceSlot, captured,
            CreateFilledMask(request.Width, request.Height, initialFill), request);

        var asset = _res.ResolveMovie(request.ResourceId);
        if (asset == null)
        {
            Godot.GD.Print($"movie mask unresolved {CurrentScene}:0x{request.ResourceId:x}");
            PublishMovieMaskTerminal(new MovieMaskPlayback(gfx, request, captured));
            return;
        }

        var mask = new MovieMaskPlayback(gfx, request, captured);
        bool started = StartMovie(
            asset, request.ResourceId, request.SurfaceSlot, movieFlags: 6, syncMask: 0,
            modal: false, initialPositionMs: 0,
            startDelayMs: request.StartDelayMs,
            presentationDurationMs: request.DurationMs,
            movieMask: mask, out long? stopTimeMs, out _);
        gfx.SetMovieStopTime(request.SurfaceSlot, stopTimeMs ?? 0);
        if (!started) PublishMovieMaskTerminal(mask);
    }

    public bool IsMovieSurfaceActive(int surfaceSlot)
        => _movieSurfaces.IsActive(surfaceSlot);

    public GodotHostDiagnosticSnapshot CaptureDiagnosticSnapshot()
    {
        IReadOnlyList<MovieSurfaceDiagnostic> movies = _movieSurfaces.Snapshot();
        IReadOnlyList<long> completed = _movieSurfaces.CompletedPlaybackIds();
        bool screenTransitionActive;
        lock (_screenTransitionLock) screenTransitionActive = _screenTransition != null;
        return new GodotHostDiagnosticSnapshot(
            CurrentScene,
            IsWaiting,
            IsTransitionWaiting,
            IsSleeping,
            IsTextRevealing,
            _modalMovieWaiting,
            _advPagePresentationSuspended,
            _messageSkipActive,
            screenTransitionActive,
            TransitionStartedAtMs,
            movies,
            completed);
    }

    public void PlayModalMovieToSurface(long resourceId, int surfaceSlot, long movieFlags)
    {
        var asset = _res.ResolveMovie(resourceId);
        if (asset == null)
        {
            Godot.GD.Print($"modal movie unresolved packed:0x{resourceId:x}");
            return;
        }

        _modalMovieCancelled = false;
        _modalMovieWaiting = true;
        bool scriptSuspended = false;
        try
        {
            if (!StartMovie(asset, resourceId, surfaceSlot, movieFlags, 0, modal: true,
                            initialPositionMs: 0, startDelayMs: 0,
                            presentationDurationMs: null, movieMask: null,
                            out _, out long playbackId)) return;
            _timeline?.State("modal-movie-wait", new()
            {
                ["resource"] = resourceId, ["playback"] = playbackId,
                ["surface"] = surfaceSlot, ["file"] = asset.Name,
            });
            scriptSuspended = SuspendScriptForPresentation();
            RequestSynchronizedPresentation();
            while (!_stopping && !_modalMovieCancelled)
            {
                if (!_movieSurfaces.IsActive(surfaceSlot)) break;
                _frameSignal.WaitOne(50);
            }

            // Cancellation is a completed modal presentation from the script's perspective. The
            // wrapper's following surface-release opcode performs the ordinary decoder teardown.
            if (_modalMovieCancelled)
                _movieSurfaces.Complete(playbackId);
            _timeline?.State("running", new()
            {
                ["modal_movie_complete"] = !_modalMovieCancelled,
                ["modal_movie_cancelled"] = _modalMovieCancelled,
            });
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
            _modalMovieWaiting = false;
            _modalMovieCancelled = false;
        }
    }

    private bool StartMovie(AssetEntry asset, long resourceId, int surfaceSlot, long movieFlags,
                            long syncMask, bool modal, long initialPositionMs, long startDelayMs,
                            long? presentationDurationMs, MovieMaskPlayback? movieMask,
                            out long? stopTimeMs, out long playbackId)
    {
        stopTimeMs = null;
        // A playback is a surface-owned instance, not the shared resource id. BTL can schedule the same
        // asset on multiple surfaces; replacing one binding must not erase another binding's completion.
        MovieSurfaceBinding binding = _movieSurfaces.Begin(surfaceSlot, resourceId, out var replaced);
        playbackId = binding.PlaybackId;
        if (replaced is { } prior)
        {
            AbandonMovieMaskPlayback(prior.PlaybackId);
            ForgetMovieMaskSurface(prior.SurfaceSlot, prior.PlaybackId);
            _main.CallDeferred("StopMovie", prior.PlaybackId);
        }
        if (movieMask != null)
            lock (_movieMaskLock)
            {
                _movieMasksByPlayback[playbackId] = movieMask;
                _movieMaskPlaybackBySurface[surfaceSlot] = playbackId;
            }
        lock (_imageLock)
        {
            if (movieMask == null) _surfaceImages.Remove(surfaceSlot);
            _surfaceColorKeys.Remove(surfaceSlot);
        }
        // Movie surfaces inherit the selected game's primary size until a decoded frame supplies content.
        _slotDims[surfaceSlot] = movieMask == null
            ? (_screenWidth, _screenHeight)
            : (movieMask.Captured.Width, movieMask.Captured.Height);
        try
        {
            var movie = _res.ReadMovie(asset);
            _timeline?.Event("movie-start", new()
            {
                ["resource"] = resourceId, ["playback"] = playbackId,
                ["surface"] = surfaceSlot, ["file"] = movie.Name,
                ["flags"] = movieFlags, ["sync_mask"] = syncMask, ["modal"] = modal,
                ["initial_position_ms"] = Math.Max(0, initialPositionMs),
                ["start_delay_ms"] = Math.Max(0, startDelayMs),
                ["presentation_duration_ms"] = presentationDurationMs,
                ["movie_mask"] = movieMask != null,
            });
            bool started = _main.TryPlayMovie(
                movie.Bytes, movie.Name, playbackId, resourceId, asset.PackedId, movieFlags,
                initialPositionMs, startDelayMs, presentationDurationMs, out stopTimeMs);
            if (!started)
            {
                stopTimeMs = 0;
                NotifyMovieCompleted(playbackId);
            }
            return started;
        }
        catch (System.Exception e)
        {
            AbandonMovieMaskPlayback(playbackId);
            _movieSurfaces.Abandon(playbackId, out _);
            _slotDims.Remove(surfaceSlot);
            stopTimeMs = 0;
            Godot.GD.Print($"movie read failed {asset.Name}: {e.Message}");
            return false;
        }
    }

    public void ReleaseSurface(int slot)
    {
        lock (_screenTransitionLock) _renderTargetSnapshots.Remove(slot);
        MovieSurfaceRelease movieRelease = _movieSurfaces.ReleaseIfCompleted(slot);
        if (movieRelease.Kind == MovieSurfaceReleaseKind.Active)
            return; // Static surface setup before 0x21c must not evict an active movie playback.
        lock (_imageLock)
        {
            _surfaceImages.Remove(slot);
            _surfaceColorKeys.Remove(slot);
        }
        lock (_textLock)
        {
            _surfaceText.Remove(slot);
            _surfaceResources.Remove(slot);
        }
        _slotDims.Remove(slot);
        if (movieRelease.Kind == MovieSurfaceReleaseKind.Released)
        {
            var binding = movieRelease.Binding;
            AbandonMovieMaskPlayback(binding.PlaybackId);
            ForgetMovieMaskSurface(binding.SurfaceSlot, binding.PlaybackId);
            _timeline?.Event("movie-stop", new()
            {
                ["resource"] = binding.ResourceId,
                ["playback"] = binding.PlaybackId,
                ["surface"] = slot,
            });
            _main.CallDeferred("StopMovie", binding.PlaybackId);
        }
    }

    public void ClearRenderTarget(int surfaceSlot)
    {
        // The retained compositor rebuilds the backbuffer from black at the next publication boundary.
        // For an offscreen target, discard separately retained text draws so its modeled pixel contents
        // observe the native D3D clear as well.
        if (surfaceSlot >= 0)
        {
            lock (_textLock) _surfaceText.Remove(surfaceSlot);
            lock (_imageLock)
            {
                if (_surfaceImages.TryGetValue(surfaceSlot, out var image))
                    System.Array.Clear(image.Pixels);
                else if (_slotDims.TryGetValue(surfaceSlot, out var dimensions)
                         && dimensions.W > 0 && dimensions.H > 0)
                    _surfaceImages[surfaceSlot] = new RgbaImage(
                        dimensions.W, dimensions.H,
                        new byte[checked(dimensions.W * dimensions.H * 4)]);
            }
        }
        _timeline?.Event("render-target-clear", new() { ["surface"] = surfaceSlot });
    }

    private void PublishObjectRangeToSurface(
        GfxState gfx, long firstHandle, long count,
        IReadOnlyList<RenderObject>? sampled = null)
    {
        int targetSlot = gfx.CurrentRenderTargetSlot;
        if (targetSlot < 0 || !_slotDims.TryGetValue(targetSlot, out var dimensions)
            || dimensions.W <= 0 || dimensions.H <= 0)
            return;

        RgbaImage destination;
        lock (_imageLock)
            destination = _surfaceImages.TryGetValue(targetSlot, out var current)
                ? new RgbaImage(current.Width, current.Height, (byte[])current.Pixels.Clone())
                : new RgbaImage(dimensions.W, dimensions.H,
                    new byte[checked(dimensions.W * dimensions.H * 4)]);

        IReadOnlyList<RenderObject> visible = sampled ?? gfx.SnapshotVisibleObjects(_clock.NowMs);
        int rendered = RetainedSurfaceRasterizer.CompositeRange(
            destination, visible, firstHandle, count,
            item =>
            {
                var raw = gfx.TryGet(item.Handle);
                var resolved = raw != null
                    ? ResolveSurfaceTexture(raw.SourceSlot, item.SurfaceResId)
                    : ResolveResIdTexture(item.SurfaceResId);
                return resolved == null
                    ? null
                    : RgbaSurfaceOps.WithColorKey(resolved.Value.Image, item.ColorKey);
            });

        lock (_imageLock) _surfaceImages[targetSlot] = destination;
        PublishSurfaceTextRangeToSurface(
            gfx, visible, firstHandle, count, targetSlot, dimensions.W, dimensions.H);
        IReadOnlyList<RenderObject> retained = visible
            .Where(item => item.Handle >= firstHandle && item.Handle - firstHandle < count)
            .ToArray();
        lock (_screenTransitionLock) _renderTargetSnapshots[targetSlot] = retained;
        _timeline?.Event("render-target-publish", new()
        {
            ["surface"] = targetSlot,
            ["first"] = firstHandle,
            ["count"] = count,
            ["objects"] = retained.Count,
            ["rendered"] = rendered,
        });
    }

    private void PublishSurfaceTextRangeToSurface(
        GfxState gfx, IReadOnlyList<RenderObject> visible, long firstHandle, long count,
        int targetSlot, int targetWidth, int targetHeight)
    {
        List<SurfaceTextDraw> projected;
        lock (_textLock)
            projected = _surfaceText.TryGetValue(targetSlot, out var retained)
                ? new List<SurfaceTextDraw>(retained)
                : new List<SurfaceTextDraw>();

        foreach (RenderObject item in visible)
        {
            if (item.Handle < firstHandle || item.Handle - firstHandle >= count) continue;
            var raw = gfx.TryGet(item.Handle);
            if (raw == null) continue;
            List<SurfaceTextDraw>? source;
            lock (_textLock)
                source = _surfaceText.TryGetValue(raw.SourceSlot, out var draws)
                    ? new List<SurfaceTextDraw>(draws)
                    : null;
            if (source == null) continue;

            Affine2D localToTarget =
                Transform2DMath.Build(item.Transform, item.Rotation, item.ScaleCycle)
                    .FromLocalOrigin(item.DstX, item.DstY);
            if (item.RangeTransform is { } rangeTransform)
                localToTarget = localToTarget.Then(rangeTransform);
            foreach (SurfaceTextDraw draw in source)
            {
                if (draw.X < item.SrcX || draw.X >= item.SrcX + item.W ||
                    draw.Y < item.SrcY || draw.Y >= item.SrcY + item.H) continue;
                var position = localToTarget.Apply(draw.X - item.SrcX, draw.Y - item.SrcY);
                int x = (int)System.Math.Round(position.X);
                int y = (int)System.Math.Round(position.Y);
                if (x < 0 || x >= targetWidth || y < 0 || y >= targetHeight) continue;
                projected.Add(new SurfaceTextDraw(x, y, draw.Text, draw.Style));
            }
        }

        lock (_textLock)
        {
            if (projected.Count == 0) _surfaceText.Remove(targetSlot);
            else _surfaceText[targetSlot] = projected;
        }
    }

    public void ReleaseSurfaceRange(int firstSlot, int count)
    {
        IReadOnlyList<MovieSurfaceBinding> stoppedMovies = _movieSurfaces.ReleaseRange(firstSlot, count);
        int end = checked(firstSlot + count);
        lock (_screenTransitionLock)
            for (int slot = firstSlot; slot < end; slot++) _renderTargetSnapshots.Remove(slot);
        lock (_imageLock)
        {
            for (int slot = firstSlot; slot < end; slot++)
            {
                _surfaceImages.Remove(slot);
                _surfaceColorKeys.Remove(slot);
                _slotDims.Remove(slot);
            }
        }
        lock (_textLock)
        {
            for (int slot = firstSlot; slot < end; slot++)
            {
                _surfaceText.Remove(slot);
                _surfaceResources.Remove(slot);
            }
        }
        foreach (MovieSurfaceBinding binding in stoppedMovies)
        {
            AbandonMovieMaskPlayback(binding.PlaybackId);
            ForgetMovieMaskSurface(binding.SurfaceSlot, binding.PlaybackId);
            _timeline?.Event("movie-stop", new()
            {
                ["resource"] = binding.ResourceId,
                ["playback"] = binding.PlaybackId,
                ["range_release"] = true,
            });
            _main.CallDeferred("StopMovie", binding.PlaybackId);
        }
        _timeline?.Event("surface-range-release", new() { ["first"] = firstSlot, ["count"] = count });
    }

    // Main-thread decoder handoff. Replacing the newest frame mirrors the native texture renderer's
    // sample callback: the retained object keeps its surface binding while only the surface pixels change.
    public void PublishMovieFrame(long playbackId, string name, int assetId, RgbaImage frame)
    {
        MovieMaskPlayback? maskPlayback;
        lock (_movieMaskLock) _movieMasksByPlayback.TryGetValue(playbackId, out maskPlayback);
        if (maskPlayback != null)
        {
            var request = maskPlayback.Request;
            byte initialFill = request.Mode == 1 ? (byte)0 : (byte)255;
            byte[] mask = MovieMaskSurface.ExtractGreen(
                frame, request.Width, request.Height, initialFill);
            PublishMaskedCapture(request.SurfaceSlot, maskPlayback.Captured, mask, request);
            return;
        }
        if (_movieSurfaces.PublishFrame(playbackId, frame, name, assetId))
            System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
    }

    public void NotifyMovieCompleted(long playbackId)
    {
        MovieMaskPlayback? maskPlayback = RemoveMovieMaskPlayback(playbackId);
        if (maskPlayback != null) PublishMovieMaskTerminal(maskPlayback);
        if (!_movieSurfaces.Complete(playbackId)) return;
        _timeline?.Event("movie-complete", new() { ["playback"] = playbackId });
        _frameSignal.Set();
    }

    private bool HasActiveMoviePresentation()
        => _movieSurfaces.HasActivePlayback;

    private RgbaImage CaptureRetainedRange(
        GfxState gfx, int targetSlot, long firstHandle, int count)
    {
        var dimensions = _slotDims.GetValueOrDefault(
            targetSlot, (W: _screenWidth, H: _screenHeight));
        int width = System.Math.Max(0, dimensions.W);
        int height = System.Math.Max(0, dimensions.H);
        var captured = new RgbaImage(width, height, new byte[checked(width * height * 4)]);
        IReadOnlyList<RenderObject> visible = gfx.SnapshotVisibleObjects(_clock.NowMs);
        RetainedSurfaceRasterizer.CompositeRange(
            captured, visible, firstHandle, System.Math.Max(0, count),
            item =>
            {
                var raw = gfx.TryGet(item.Handle);
                var resolved = raw != null
                    ? ResolveSurfaceTexture(raw.SourceSlot, item.SurfaceResId)
                    : ResolveResIdTexture(item.SurfaceResId);
                return resolved == null
                    ? null
                    : RgbaSurfaceOps.WithColorKey(resolved.Value.Image, item.ColorKey);
            });
        return captured;
    }

    private static byte[] CreateFilledMask(int width, int height, byte fill)
    {
        var mask = new byte[checked(System.Math.Max(0, width) * System.Math.Max(0, height))];
        if (fill != 0) System.Array.Fill(mask, fill);
        return mask;
    }

    private void PublishMaskedCapture(
        int surfaceSlot, RgbaImage captured, byte[] mask, MovieMaskTransitionRequest request)
    {
        RgbaImage image = MovieMaskSurface.Apply(
            captured, mask, System.Math.Max(0, request.Width),
            System.Math.Max(0, request.Height), request.X, request.Y);
        lock (_imageLock) _surfaceImages[surfaceSlot] = image;
        _slotDims[surfaceSlot] = (image.Width, image.Height);
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        _frameSignal.Set();
    }

    private void PublishMovieMaskTerminal(MovieMaskPlayback playback)
    {
        byte terminalFill = playback.Request.Mode == 1 ? (byte)255 : (byte)0;
        PublishMaskedCapture(
            playback.Request.SurfaceSlot, playback.Captured,
            CreateFilledMask(playback.Request.Width, playback.Request.Height, terminalFill),
            playback.Request);
        playback.Gfx.CompleteMovieMaskTransition(playback.Request.SurfaceSlot);
    }

    private MovieMaskPlayback? RemoveMovieMaskPlayback(long playbackId)
    {
        lock (_movieMaskLock)
        {
            if (!_movieMasksByPlayback.Remove(playbackId, out var playback)) return null;
            return playback;
        }
    }

    private void AbandonMovieMaskPlayback(long playbackId)
    {
        MovieMaskPlayback? playback = RemoveMovieMaskPlayback(playbackId);
        if (playback != null)
        {
            ForgetMovieMaskSurface(playback.Request.SurfaceSlot, playbackId);
            playback.Gfx.CompleteMovieMaskTransition(playback.Request.SurfaceSlot);
        }
    }

    private void ForgetMovieMaskSurface(int surfaceSlot, long playbackId)
    {
        lock (_movieMaskLock)
            if (_movieMaskPlaybackBySurface.GetValueOrDefault(surfaceSlot) == playbackId)
                _movieMaskPlaybackBySurface.Remove(surfaceSlot);
    }

    private RgbaImage? Decode(AssetEntry asset)
    {
        lock (_imageLock)
        {
            if (_images.TryGetValue(asset.PackedId, out var cached)) return cached;
            try { return _images[asset.PackedId] = _res.DecodeTexture(asset); }
            catch (System.Exception e)
            {
                Godot.GD.Print($"AGF decode failed {asset.Name}: {e.Message}");
                _images[asset.PackedId] = null;
                return null;
            }
        }
    }

    // ---- audio ops (OGG plays natively in Godot) ----
    // BGM is addressed by direct name (BGM{id:D3}.OGG); voice uses the universal packed catalog.
    public void PlayBgm(long id)
        => DispatchBgm(id, 1, false);

    public void RestartBgm(long id, int startMode)
        => DispatchBgm(id, startMode, true);

    private void DispatchBgm(long id, int startMode, bool forceRestart)
    {
        var asset = _res.ResolveBgm(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("bgm", new()
        {
            ["id"] = id,
            ["file"] = audio?.Name,
            ["start_mode"] = startMode,
            ["force_restart"] = forceRestart,
        });
        if (audio == null) return;
        if (forceRestart)
            _main.CallDeferred("RestartBgm", audio.Bytes, audio.Name, startMode);
        else
            _main.CallDeferred("PlayBgm", audio.Bytes, audio.Name);
    }

    public void StopBgm()
    {
        _timeline?.Event("bgm-stop", new());
        _main.CallDeferred("StopBgm");
    }

    public void PlayVoice(long id) => PlayVoice(id, 0);

    public void PlayVoice(long id, int playbackVariant)
    {
        var asset = _res.ResolveVoice(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("voice", new() { ["id"] = id, ["file"] = audio?.Name,
            ["playback_variant"] = playbackVariant });
        if (audio == null) return;
        bool queuedForSkip;
        bool firstQueued = false;
        lock (_messageSkipLock)
        {
            queuedForSkip = _messageSkipActive;
            if (queuedForSkip)
            {
                firstQueued = _queuedSkippedVoice == null;
                _queuedSkippedVoice = (audio, playbackVariant);
            }
        }
        if (queuedForSkip)
        {
            if (firstQueued) _main.CallDeferred("StopVoiceForMessageSkip");
            return;
        }
        DispatchVoice(audio, playbackVariant);
    }

    private void DispatchVoice(AudioPayload audio, int playbackVariant)
    {
        int generation = _main.QueueVoicePlayback();
        bool duckBgm = (System.Threading.Volatile.Read(ref _voiceBgmDuckControl) & 1) == 0;
        // Godot's stream player has no matching AGE start-mode control. Retain the native
        // variant through dispatch/timeline so that distinction is not erased at the VM seam.
        _main.CallDeferred("PlayVoice", audio.Bytes, audio.Name, generation, duckBgm, 50);
    }

    public void SetVoiceBgmDuckControl(long flags)
    {
        System.Threading.Volatile.Write(ref _voiceBgmDuckControl, unchecked((int)flags));
        _timeline?.State("voice-bgm-duck-control", new() { ["flags"] = flags });
    }

    public void ScheduleVoicePlayback(long id, int playbackVariant, long delayMs)
    {
        var asset = _res.ResolveVoice(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("voice-scheduled", new()
        {
            ["id"] = id, ["file"] = audio?.Name, ["playback_variant"] = playbackVariant,
            ["delay_ms"] = unchecked((uint)delayMs),
        });
        lock (_scheduledVoiceLock)
            _scheduledVoice = audio == null
                ? null
                : (audio, playbackVariant, unchecked((uint)delayMs), null);
    }

    public void LoadSoundEffect(long resourceId, int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        var asset = _res.ResolveSoundEffect(resourceId);
        var audio = asset != null ? LoadAudio(asset) : null;
        _sfxNames[channel] = audio?.Name;
        _timeline?.Event("sfx-load", new() { ["resource"] = resourceId, ["channel"] = channel,
            ["file"] = audio?.Name });
        if (audio != null) _main.CallDeferred("LoadSoundEffect", audio.Bytes, audio.Name, channel);
    }

    public void StartSoundEffect(int channel) => StartSoundEffect(channel, 0);

    public void StartSoundEffect(int channel, int startMode)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        _timeline?.Event("sfx-start", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["file"] = _sfxNames[channel] });
        _main.CallDeferred("StartSoundEffect", channel, startMode);
    }

    public void ScheduleSoundEffectStart(int channel, int startMode, long delayMs)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        long ms = System.Math.Clamp(delayMs, 0, 60_000);
        double realSeconds = ms / 1000.0 / System.Math.Max(0.05, _clock.Speed);
        _timeline?.Event("sfx-start-scheduled", new() { ["channel"] = channel,
            ["start_mode"] = startMode, ["delay_ms"] = ms, ["file"] = _sfxNames[channel] });
        _main.CallDeferred("ScheduleSoundEffectStart", channel, startMode, realSeconds);
    }

    public void ReleaseSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length) return;
        _timeline?.Event("sfx-release", new() { ["channel"] = channel,
            ["file"] = _sfxNames[channel] });
        _sfxNames[channel] = null;
        _main.CallDeferred("ReleaseSoundEffect", channel);
    }

    private AudioPayload? LoadAudio(AssetEntry asset)
    {
        try { return _res.ReadAudio(asset); }
        catch (System.Exception e)
        {
            Godot.GD.Print($"audio read failed {asset.Name}: {e.Message}");
            return null;
        }
    }

    public void FadeBgm(int targetPercent, long durationMs)
    {
        long ms = System.Math.Clamp(durationMs, 0, 60_000);
        double realSeconds = ms / 1000.0 / System.Math.Max(0.05, _clock.Speed);
        _timeline?.State("bgm-fade", new() { ["target_percent"] = targetPercent, ["duration_ms"] = ms });
        _main.CallDeferred("FadeBgm", targetPercent, realSeconds);
        long deadline = _clock.NowMs + ms;
        IsSleeping = true;
        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            // Native parks the interpreter in its audio service while the main render loop continues.
            // Publish scene changes accumulated before the fade (notably GAMESTART -> SC0000's black
            // frame), then leave presentation ownership with the compositor for the timed wait.
            RequestSynchronizedPresentation();
            while (_clock.NowMs < deadline && !_stopping) _frameSignal.WaitOne(50);
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
            IsSleeping = false;
        }
        _timeline?.State("running", new() { ["bgm_fade_complete"] = true });
    }

    public void ApplyAudioVolume(int category, int basisPoints)
    {
        _timeline?.State("audio-volume", new()
        {
            ["category"] = category,
            ["basis_points"] = basisPoints,
        });
        _main.CallDeferred("ApplyAudioVolume", category, basisPoints);
    }

    public void ApplyAudioRouteEnabled(int category, bool enabled)
    {
        _timeline?.State("audio-route", new()
        {
            ["category"] = category,
            ["enabled"] = enabled,
        });
        _main.CallDeferred("ApplyAudioRouteEnabled", category, enabled);
    }
}

public readonly record struct SurfaceTextDraw(int X, int Y, string Text, AdvTextStyle Style);
public readonly record struct LiveAdvTextSnapshot(
    AdvLiveTextRun Run, int VisibleGlyphs, bool Revealing);
public sealed record GodotHostDiagnosticSnapshot(
    string CurrentScene, bool IsInputWaiting, bool IsTransitionWaiting, bool IsSleeping,
    bool IsTextRevealing, bool IsModalMovieWaiting, bool IsAdvPagePresentationSuspended,
    bool IsMessageSkipActive, bool IsScreenTransitionActive, long TransitionStartedAtMs,
    IReadOnlyList<MovieSurfaceDiagnostic> MovieSurfaces, IReadOnlyList<long> CompletedMoviePlaybackIds);
public readonly record struct LegacyScreenTransitionSnapshot(
    IReadOnlyList<RenderObject> Source, IReadOnlyList<RenderObject> Target, double Progress);

internal sealed class LegacyScreenTransition
{
    public IReadOnlyList<RenderObject> Source { get; }
    public IReadOnlyList<RenderObject> Target { get; }
    public long StartMs { get; }
    public long DurationMs { get; }
    public bool Forced { get; set; }

    public LegacyScreenTransition(IReadOnlyList<RenderObject> source, IReadOnlyList<RenderObject> target,
                                  long startMs, long durationMs)
    {
        Source = source;
        Target = target;
        StartMs = startMs;
        DurationMs = durationMs;
    }
}

public readonly record struct AdvWaitIndicatorSnapshot(
    RgbaImage Image, string Name, int AssetId, AdvWaitIndicatorConfig Config, int Frame);
