using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;

[SupportedOSPlatform("windows")]
public sealed class GodotAdvHost : IHost
{
    private readonly Main _main;
    private readonly ResourceMap _res;
    private readonly string _rootScene;
    private readonly object _scriptContextLock = new();
    private readonly Stack<string> _scriptContexts = new();
    private readonly object _imageLock = new();
    private readonly Dictionary<int, RgbaImage?> _images = new();        // packed catalog id -> decoded pixels
    // Mutable AGE surfaces are published by replacing immutable RgbaImage snapshots, so the compositor
    // can safely finish reading an old frame while the VM prepares a copied-rectangle update.
    private readonly Dictionary<int, RgbaImage> _surfaceImages = new();
    private readonly Dictionary<int, long> _surfaceColorKeys = new();
    private readonly Dictionary<int, long> _surfaceResources = new();    // surface slot -> packed catalog id
    private readonly Dictionary<long, (RgbaImage Image, string Name, int AssetId)> _movieFrames = new();
    private readonly Dictionary<int, long> _movieBySurface = new();
    private readonly HashSet<long> _completedMovies = new();
    private readonly string?[] _sfxNames = new string?[10];              // SC0000 native channel subset
    // slot -> dimensions of the currently allocated surface. Slot 0 begins as the engine's 800x600
    // primary surface, but op 0x1fa releases it like any other slot; subsequent size queries must return 0x0.
    private readonly Dictionary<int, (int W, int H)> _slotDims = new() { { 0, (800, 600) } };
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
    private string _advText = "";
    private int _advTextX = 100, _advTextY = 47;
    private int _currentAdvLayout = 1; // SYSTEM4's ordinary SC0000 ADV layout
    private long _advTextStartedMs;
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
    private readonly object _screenTransitionLock = new();
    private readonly Dictionary<int, IReadOnlyList<RenderObject>> _renderTargetSnapshots = new();
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
                        PageLocatorState locator, GodotTimelineLog? timeline = null)
    {
        _main = main; _res = res; _rootScene = scene; _clock = clock;
        _locator = locator; _timeline = timeline;
    }

    public void ReportWarning(string message) => System.Console.Error.WriteLine(message);

    private string CurrentScene
    {
        get { lock (_scriptContextLock) return _scriptContexts.TryPeek(out var scene) ? scene : _rootScene; }
    }

    public void EnterScriptContext(string scriptName)
    {
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
    }

    public void ShowText(int offset, string text)
    {
        Captured.Add((offset, text));
        _locator.Text(offset, text);
        lock (_textLock)
        {
            _advText = text;
            _advTextStartedMs = _clock.NowMs;
            _advTextForceComplete = _messageSkipActive;
            IsTextRevealing = text.Length > 0 && !_messageSkipActive;
        }
        _timeline?.State("text-reveal", new()
        {
            ["offset"] = $"0x{offset:x}", ["x"] = _advTextX, ["y"] = _advTextY,
            ["glyphs"] = text.Length, ["delay_ms"] = 50,
        });
        while (IsTextRevealing && !_stopping)
        {
            lock (_textLock)
            {
                if (_advTextForceComplete || _clock.NowMs - _advTextStartedMs >= text.Length * 50L)
                    IsTextRevealing = false;
            }
            if (IsTextRevealing) _frameSignal.WaitOne(50);
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
                : (int)System.Math.Clamp((_clock.NowMs - _advTextStartedMs) / 50L + 1, 0, _advText.Length);
            return (_advText, _advTextX, _advTextY, visible, IsTextRevealing);
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

    public void ClearRenderedAdvTextLayout(int layoutSlot)
    {
        lock (_textLock) _historyText.Remove(layoutSlot == 0 ? _currentAdvLayout : layoutSlot);
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

    public int MessageWindowAlphaSetting => 0;

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
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("present-object-range", new() { ["first"] = firstHandle, ["count"] = count });
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
        int frames = System.Math.Max(1, config.TerminalFrame + 1);
        long period = System.Math.Max(1, config.FramePeriodMs);
        int frame = (int)((_clock.NowMs - _waitIndicatorStartedMs) / period % frames);
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
        // Publish retained mutations accumulated before the wait once. A static input wait is not itself a
        // reason to rebuild the 800x600 background every frame; ambient channels are queried separately.
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
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
        while (!_stopping)
        {
            while (true)
            {
                bool markerWasEnabled;
                lock (_textLock) markerWasEnabled = _waitIndicatorEnabled;
                bool keepServicing = serviceInputCallback();
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
        if (IsTransitionWaiting && _foregroundGfx != null)
        {
            int completed = _foregroundGfx.CompleteForegroundTransitions(_clock.NowMs);
            if (completed > 0)
            {
                _timeline?.State("transition-forced-complete", new() { ["count"] = completed });
                _frameSignal.Set();
                return;
            }
        }
        if (IsTransitionWaiting)
        {
            lock (_screenTransitionLock)
            {
                if (_screenTransition != null)
                {
                    _screenTransition.Forced = true;
                    _timeline?.State("screen-transition-forced-complete", new());
                    _frameSignal.Set();
                    return;
                }
            }
        }
        if (IsWaiting && _gate.CurrentCount == 0) _gate.Release();
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
        => Interlocked.Exchange(ref _presentRequested, 1);

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
        int started = gfx.StartForegroundTransitions(_clock.NowMs);
        if (started == 0 && !gfx.HasActiveTimedPresentation(_clock.NowMs) && !HasActiveMoviePresentation()) return;
        _foregroundGfx = gfx;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, _clock.NowMs);
        IsTransitionWaiting = true;
        _timeline?.State("transition-start", new() { ["count"] = started });
        int lastBucket = -1;
        while ((gfx.HasActiveTimedPresentation(_clock.NowMs) || HasActiveMoviePresentation()) && !_stopping)
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
        // The active query becomes false at the exact transition/movie endpoint. Publish that terminal sample
        // once so the last visible frame cannot remain fractionally incomplete.
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        IsTransitionWaiting = false;
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
            lock (_screenTransitionLock) _renderTargetSnapshots[slot] = snapshot;
            _timeline?.Event("render-target-snapshot", new()
            {
                ["surface"] = slot, ["objects"] = snapshot.Count,
            });
            return;
        }
        int started = gfx.StartForegroundTransitions(_clock.NowMs);
        int completed = gfx.CompleteForegroundTransitions(_clock.NowMs);
        if (started > 0 || completed > 0)
            _timeline?.State("transition-skip-complete", new()
            {
                ["started"] = started, ["completed"] = completed,
            });
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
    }

    // Op 0x25's native mode-4 path advances an 8-bit alpha accumulator. Values <=64 use the
    // operand as the timer interval and step alpha by sixteen; larger values divide the interval
    // by sixteen and step alpha by one. This reproduces the resulting wall-clock duration.
    public void CrossfadeSurfaces(GfxState gfx, int sourceSurface, int targetSurface, long intervalArgument)
    {
        IReadOnlyList<RenderObject> source;
        IReadOnlyList<RenderObject> target;
        long start = _clock.NowMs;
        long duration = LegacyScreenTransitionTiming.DurationMilliseconds(intervalArgument);
        lock (_screenTransitionLock)
        {
            source = _renderTargetSnapshots.TryGetValue(sourceSurface, out var capturedSource)
                ? capturedSource : System.Array.Empty<RenderObject>();
            target = _renderTargetSnapshots.TryGetValue(targetSurface, out var capturedTarget)
                ? capturedTarget : gfx.SnapshotVisibleObjects(start);
            _screenTransition = new LegacyScreenTransition(source, target, start, duration);
        }
        _foregroundGfx = null;
        System.Threading.Interlocked.Exchange(ref _transitionStartedAtMs, start);
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        IsTransitionWaiting = true;
        _timeline?.State("screen-transition-start", new()
        {
            ["source"] = sourceSurface, ["target"] = targetSurface,
            ["interval_argument"] = intervalArgument, ["duration_ms"] = duration,
            ["source_objects"] = source.Count, ["target_objects"] = target.Count,
        });
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
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        // Do not let the VM release both source surfaces (or immediately reload SYSTEM4) until the
        // main thread has actually published the terminal target frame.
        while (!_stopping)
        {
            lock (_screenTransitionLock)
                if (_screenTransition == null) break;
            _frameSignal.WaitOne(50);
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
    public bool ShouldRecomposite(GfxState gfx)
    {
        bool screenTransitionActive;
        lock (_screenTransitionLock) screenTransitionActive = _screenTransition != null;
        return System.Threading.Interlocked.Exchange(ref _presentRequested, 0) != 0 ||
               screenTransitionActive || gfx.HasActiveVisualPresentation(_clock.NowMs);
    }

    public void Stop()
    {
        _stopping = true;
        _main.CallDeferred("ClearAgeCursor");
        lock (_textLock) _advTextForceComplete = true;
        if (_gate.CurrentCount == 0) _gate.Release();
        _inputCallbackSignal.Set();
        _frameSignal.Set();
    }

    public void ResetSceneContext()
    {
        // scene_context_init_reset releases ordinary surface/movie bindings but keeps decoded asset
        // caches and process-owned audio/configuration available to the reloaded SYSTEM4 root.
        ReleaseSurfaceRange(0, 1000);
        lock (_textLock)
        {
            _surfaceText.Clear();
            _surfaceResources.Clear();
            _historyText.Clear();
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

    // Ordinary opcode bursts run to the next service boundary without frame pacing. Persistent message
    // Skip removes most of those boundaries, but native adv_interpreter_tick still executes one opcode per
    // engine tick; retain that cadence here so Skip advances quickly instead of free-running whole scenes.
    public void FrameYield()
    {
        if (_messageSkipActive && !_stopping) _frameSignal.WaitOne(50);
    }

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
        // A sleep is a service boundary: make preceding retained writes visible once even when no animation
        // channel is active during the hold.
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        IsSleeping = true;
        while (_clock.NowMs < deadline)
        {
            if (_stopping) break;
            _frameSignal.WaitOne(50);
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
        lock (_imageLock)
        {
            if (_movieFrames.TryGetValue(resId, out var movie))
                return (movie.Image, movie.Name, movie.AssetId, true);
            // Movie payloads use the same .AGF extension as still images. While the decoder is opening
            // (or before its first frame arrives), keep the already-created surface blank
            // instead of falling through to AgfDecoder and misclassifying the MPEG program stream.
            if (_movieBySurface.Values.Contains(resId)) return null;
        }
        var asset = _res.ResolveTexture(resId);
        var image = asset != null ? Decode(asset) : null;
        return asset != null && image != null ? (image, asset.Name, asset.PackedId, false) : null;
    }

    public (RgbaImage Image, string Name, int AssetId, bool IsDynamic)? ResolveSurfaceTexture(
        int surfaceSlot, long fallbackResourceId)
    {
        lock (_imageLock)
            if (_surfaceImages.TryGetValue(surfaceSlot, out var surface))
                return (surface, $"<surface:{surfaceSlot}>", int.MinValue + surfaceSlot, true);
        return fallbackResourceId != 0 ? ResolveResIdTexture(fallbackResourceId) : null;
    }

    public long? PlayMovieToSurface(long resourceId, int surfaceSlot, long movieFlags, long syncMask)
    {
        string scene = CurrentScene;
        var asset = _res.ResolveMovie(resourceId);
        if (asset == null) { Godot.GD.Print($"movie unresolved {scene}:0x{resourceId:x}"); return null; }
        StartMovie(asset, resourceId, surfaceSlot, movieFlags, syncMask, modal: false,
                   out long? stopTimeMs);
        return stopTimeMs ?? 0;
    }

    public bool IsMovieSurfaceActive(int surfaceSlot)
    {
        lock (_imageLock)
            return _movieBySurface.TryGetValue(surfaceSlot, out long resourceId)
                && !_completedMovies.Contains(resourceId);
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
        try
        {
            if (!StartMovie(asset, resourceId, surfaceSlot, movieFlags, 0, modal: true,
                            out _)) return;
            _timeline?.State("modal-movie-wait", new()
            {
                ["resource"] = resourceId, ["surface"] = surfaceSlot, ["file"] = asset.Name,
            });
            while (!_stopping && !_modalMovieCancelled)
            {
                lock (_imageLock)
                    if (_completedMovies.Contains(resourceId)) break;
                _frameSignal.WaitOne(50);
            }

            // Cancellation is a completed modal presentation from the script's perspective. The
            // wrapper's following surface-release opcode performs the ordinary decoder teardown.
            if (_modalMovieCancelled)
                lock (_imageLock) _completedMovies.Add(resourceId);
            _timeline?.State("running", new()
            {
                ["modal_movie_complete"] = !_modalMovieCancelled,
                ["modal_movie_cancelled"] = _modalMovieCancelled,
            });
        }
        finally
        {
            _modalMovieWaiting = false;
            _modalMovieCancelled = false;
        }
    }

    private bool StartMovie(AssetEntry asset, long resourceId, int surfaceSlot, long movieFlags,
                            long syncMask, bool modal, out long? stopTimeMs)
    {
        stopTimeMs = null;
        // Publish the movie identity before the potentially long VFS read and synchronous decoder setup.
        // The compositor can therefore distinguish a legitimate blank pre-roll surface from a still AGF.
        ReleaseSurface(surfaceSlot);
        lock (_imageLock)
        {
            _movieBySurface[surfaceSlot] = resourceId;
            _completedMovies.Remove(resourceId);
        }
        _slotDims[surfaceSlot] = (800, 600); // SC0000 creates this native-sized surface immediately beforehand.
        try
        {
            var movie = _res.ReadMovie(asset);
            _timeline?.Event("movie-start", new()
            {
                ["resource"] = resourceId, ["surface"] = surfaceSlot, ["file"] = movie.Name,
                ["flags"] = movieFlags, ["sync_mask"] = syncMask, ["modal"] = modal,
            });
            bool started = _main.TryPlayMovie(movie.Bytes, movie.Name, resourceId, asset.PackedId,
                                              out stopTimeMs);
            if (!started)
            {
                stopTimeMs = 0;
                NotifyMovieCompleted(resourceId);
            }
            return started;
        }
        catch (System.Exception e)
        {
            lock (_imageLock)
            {
                if (_movieBySurface.TryGetValue(surfaceSlot, out long registered) && registered == resourceId)
                    _movieBySurface.Remove(surfaceSlot);
                _movieFrames.Remove(resourceId);
                _completedMovies.Remove(resourceId);
            }
            _slotDims.Remove(surfaceSlot);
            stopTimeMs = 0;
            NotifyMovieCompleted(resourceId);
            Godot.GD.Print($"movie read failed {asset.Name}: {e.Message}");
            return false;
        }
    }

    public void ReleaseSurface(int slot)
    {
        lock (_screenTransitionLock) _renderTargetSnapshots.Remove(slot);
        long resourceId;
        lock (_imageLock)
        {
            if (!_movieBySurface.Remove(slot, out resourceId))
            {
                _surfaceImages.Remove(slot);
                _surfaceColorKeys.Remove(slot);
                lock (_textLock)
                {
                    _surfaceText.Remove(slot);
                    _surfaceResources.Remove(slot);
                }
                _slotDims.Remove(slot);
                return;
            }
            if (!_completedMovies.Contains(resourceId))
            {
                _movieBySurface[slot] = resourceId;
                return; // SC0000 prepares following static surfaces before 0x21c; the movie remains retained.
            }
            _movieFrames.Remove(resourceId);
            _completedMovies.Remove(resourceId);
            _surfaceImages.Remove(slot);
            _surfaceColorKeys.Remove(slot);
        }
        lock (_textLock)
        {
            _surfaceText.Remove(slot);
            _surfaceResources.Remove(slot);
        }
        _slotDims.Remove(slot);
        _timeline?.Event("movie-stop", new() { ["resource"] = resourceId, ["surface"] = slot });
        _main.CallDeferred("StopMovie", resourceId);
    }

    public void ClearRenderTarget(int surfaceSlot)
    {
        // The retained compositor rebuilds the backbuffer from black at the next publication boundary.
        // For an offscreen target, discard separately retained text draws so its modeled pixel contents
        // observe the native D3D clear as well.
        if (surfaceSlot >= 0)
            lock (_textLock) _surfaceText.Remove(surfaceSlot);
        _timeline?.Event("render-target-clear", new() { ["surface"] = surfaceSlot });
    }

    public void ReleaseSurfaceRange(int firstSlot, int count)
    {
        var stoppedMovies = new System.Collections.Generic.HashSet<long>();
        int end = checked(firstSlot + count);
        lock (_screenTransitionLock)
            for (int slot = firstSlot; slot < end; slot++) _renderTargetSnapshots.Remove(slot);
        lock (_imageLock)
        {
            for (int slot = firstSlot; slot < end; slot++)
            {
                if (_movieBySurface.Remove(slot, out long resourceId))
                {
                    stoppedMovies.Add(resourceId);
                    _movieFrames.Remove(resourceId);
                    _completedMovies.Remove(resourceId);
                }
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
        foreach (long resourceId in stoppedMovies)
        {
            _timeline?.Event("movie-stop", new() { ["resource"] = resourceId, ["range_release"] = true });
            _main.CallDeferred("StopMovie", resourceId);
        }
        _timeline?.Event("surface-range-release", new() { ["first"] = firstSlot, ["count"] = count });
    }

    // Main-thread decoder handoff. Replacing the newest frame mirrors the native texture renderer's
    // sample callback: the retained object keeps its surface binding while only the surface pixels change.
    public void PublishMovieFrame(long resourceId, string name, int assetId, RgbaImage frame)
    {
        lock (_imageLock) _movieFrames[resourceId] = (frame, name, assetId);
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
    }

    public void NotifyMovieCompleted(long resourceId)
    {
        lock (_imageLock)
            if (!_completedMovies.Add(resourceId)) return;
        _timeline?.Event("movie-complete", new() { ["resource"] = resourceId });
        _frameSignal.Set();
    }

    private bool HasActiveMoviePresentation()
    {
        lock (_imageLock)
            foreach (long resourceId in _movieBySurface.Values)
                if (!_completedMovies.Contains(resourceId)) return true;
        return false;
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
    {
        var asset = _res.ResolveBgm(id);
        var audio = asset != null ? LoadAudio(asset) : null;
        _timeline?.Event("bgm", new() { ["id"] = id, ["file"] = audio?.Name });
        if (audio != null) _main.CallDeferred("PlayBgm", audio.Bytes, audio.Name);
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

    public void StartSoundEffect(int channel)
    {
        if ((uint)channel >= (uint)_sfxNames.Length || _sfxNames[channel] == null) return;
        _timeline?.Event("sfx-start", new() { ["channel"] = channel,
            ["file"] = _sfxNames[channel] });
        _main.CallDeferred("StartSoundEffect", channel);
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
        while (_clock.NowMs < deadline && !_stopping) _frameSignal.WaitOne(50);
        IsSleeping = false;
        _timeline?.State("running", new() { ["bgm_fade_complete"] = true });
    }
}

public readonly record struct SurfaceTextDraw(int X, int Y, string Text, AdvTextStyle Style);
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
