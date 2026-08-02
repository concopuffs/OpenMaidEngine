using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;

public sealed partial class GodotAdvHost
{
    private readonly object _textLock = new();
    private readonly CachedGlyphMaskRasterizer _surfaceTextMaskCache;
    private readonly ImmediateSurfaceTextRenderer _surfaceTextPixelRenderer;
    private readonly RetainedGlyphLayoutEngine _retainedGlyphLayoutEngine;
    private readonly GlyphRasterizerBackendInfo _surfaceTextBackendInfo;
    private readonly HashSet<int> _retainedHistoryLayouts = new();
    private sealed class LiveTextState
    {
        public required AdvLiveTextRun Run;
        public required long StartedMs;
        public required int GlyphDelayMilliseconds;
        public int FirstGlyphIndex;
        public int GlyphCount;
    }
    private readonly List<LiveTextState> _liveText = new();
    private readonly Dictionary<int, RetainedAdvTextLayoutPresentation>
        _retainedTextLayouts = new();
    private LiveTextState? _activeLiveText;
    private int _suspendedRetainedTextLayoutSlot;
    private string _advText = "";
    private int _advTextX = 100, _advTextY = 47;
    private int _currentAdvLayout = 1; // SYSTEM4's ordinary SC0000 ADV layout
    private long _advTextStartedMs;
    private int _activeGlyphDelayMilliseconds = 50;
    private int _messageGlyphDelayMilliseconds = 50;
    private volatile int _messageWindowAlphaSetting;
    private bool _advTextForceComplete;
    private readonly Dictionary<int, AdvWaitIndicatorConfig> _waitIndicators = new();
    private readonly Dictionary<int, (
        AdvTextLayoutPresentationBinding Binding,
        AdvTextLayoutSnapshot Layout)> _waitIndicatorBindings = new();
    private readonly Dictionary<int, RetainedAdvWaitIndicatorPresentation>
        _retainedWaitIndicators = new();
    private int _activeWaitLayout;
    private long _waitIndicatorStartedMs;
    private bool _waitIndicatorEnabled;
    public volatile bool IsTextRevealing;
    public readonly List<(int Offset, string Text)> Captured = new();

    public bool UsesSurfaceTextPixels => true;
    public GlyphRasterizerBackendInfo SurfaceTextBackendInfo => _surfaceTextBackendInfo;
    public (int Count, int Capacity, long Hits, long Misses) SurfaceTextMaskCacheStats
        => (
            _surfaceTextMaskCache.Count,
            _surfaceTextMaskCache.Capacity,
            _surfaceTextMaskCache.Hits,
            _surfaceTextMaskCache.Misses);

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
        => throw new NotSupportedException(
            "Godot gameplay text requires a retained layout binding.");

    public void ShowText(AdvLiveTextRun run, int glyphDelayMilliseconds)
        => throw new NotSupportedException(
            "Godot gameplay text requires a retained layout binding.");

    public AdvRetainedTextRunResult? ShowText(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvLiveTextRun run,
        int glyphDelayMilliseconds)
    {
        if (binding.LayoutSlot != run.Layout.Slot
            || binding.FirstObjectHandle < 0
            || binding.ObjectCapacity <= 0)
            throw new InvalidOperationException(
                $"ADV layout {run.Layout.Slot} has no retained presentation binding.");
        PrepareRetainedTextRun(
            gfx, binding, run,
            out RetainedAdvTextLayoutPresentation? presentation,
            out AdvRetainedTextRunResult result);

        Captured.Add((run.SourceOffset, run.Text));
        _locator.Text(run.SourceOffset, run.Text);
        int delay = System.Math.Max(0, glyphDelayMilliseconds);
        int revealGlyphCount = System.Math.Max(
            0, System.Math.Min(
                result.GlyphCount,
                presentation.PublishableGlyphCount - result.FirstGlyphIndex));
        var state = new LiveTextState
        {
            Run = run,
            StartedMs = _clock.NowMs,
            GlyphDelayMilliseconds = delay,
            FirstGlyphIndex = result.FirstGlyphIndex,
            GlyphCount = revealGlyphCount,
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
            IsTextRevealing =
                revealGlyphCount > 0 && delay > 0 && !_messageSkipActive;
        }
        _timeline?.State("text-reveal", new()
        {
            ["offset"] = $"0x{run.SourceOffset:x}",
            ["layout"] = run.Layout.Slot,
            ["x"] = run.Layout.OriginX + run.Layout.CursorX,
            ["y"] = run.Layout.OriginY + run.Layout.CursorY,
            ["glyphs"] = revealGlyphCount,
            ["records"] = result.GlyphCount,
            ["delay_ms"] = delay,
            ["presentation"] = "retained-glyphs",
        });

        int initiallyVisible = IsTextRevealing ? 1 : revealGlyphCount;
        presentation.PublishThrough(
            gfx, checked(result.FirstGlyphIndex + initiallyVisible));
        Interlocked.Exchange(ref _presentRequested, 1);
        if (!IsTextRevealing)
        {
            _timeline?.State("running", new() { ["text_reveal_complete"] = true });
            return result;
        }

        bool scriptSuspended = SuspendScriptForPresentation();
        try
        {
            RequestSynchronizedPresentation();
            while (IsTextRevealing && !_stopping)
            {
                int visible;
                lock (_textLock)
                {
                    visible = _advTextForceComplete
                        ? revealGlyphCount
                        : (int)System.Math.Clamp(
                            (_clock.NowMs - state.StartedMs) / delay + 1,
                            0, revealGlyphCount);
                    if (visible >= revealGlyphCount) IsTextRevealing = false;
                }
                int before = presentation.PublishedGlyphCount;
                presentation.PublishThrough(
                    gfx, checked(result.FirstGlyphIndex + visible));
                if (presentation.PublishedGlyphCount != before)
                {
                    Interlocked.Exchange(ref _presentRequested, 1);
                    RequestSynchronizedPresentation();
                }
                if (IsTextRevealing) _frameSignal.WaitOne(50);
            }
        }
        finally
        {
            ResumeScriptAfterPresentation(scriptSuspended);
        }
        _timeline?.State("running", new() { ["text_reveal_complete"] = true });
        return result;
    }

    private void PrepareRetainedTextRun(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvLiveTextRun run,
        out RetainedAdvTextLayoutPresentation presentation,
        out AdvRetainedTextRunResult result)
    {
        presentation = null!;
        result = default;
        if (run.Layout.Width <= 0 || run.Layout.Height <= 0)
            throw new InvalidOperationException(
                $"ADV layout {run.Layout.Slot} has invalid dimensions " +
                $"{run.Layout.Width}x{run.Layout.Height}.");

        RgbaImage destination = ResolveSurfacePixels(binding.SourceSurfaceSlot)
            ?? new RgbaImage(
                run.Layout.Width,
                run.Layout.Height,
                new byte[checked(run.Layout.Width * run.Layout.Height * 4)]);
        if (destination.Width != run.Layout.Width
            || destination.Height != run.Layout.Height)
            throw new InvalidOperationException(
                $"ADV layout {run.Layout.Slot} surface {binding.SourceSurfaceSlot} is " +
                $"{destination.Width}x{destination.Height}; expected " +
                $"{run.Layout.Width}x{run.Layout.Height}.");

        var updated = new RgbaImage(
            destination.Width, destination.Height, (byte[])destination.Pixels.Clone());
        IReadOnlyList<GlyphRasterRequest> requests =
            ImmediateSurfaceTextRenderer.CreateRequests(
                run.Text, run.Style, _surfaceTextBackendInfo.Policy);
        GlyphTextLayoutResult rendered = _retainedGlyphLayoutEngine.Render(
            updated,
            new GlyphTextLayoutOptions(
                run.Layout.CursorX,
                run.Layout.CursorY,
                binding.ResetCursorX,
                run.Layout.Right,
                run.Layout.Bottom,
                WrapHorizontally: true,
                run.Style),
            requests);

        lock (_textLock)
        {
            if (!_retainedTextLayouts.TryGetValue(
                    binding.LayoutSlot, out presentation!))
            {
                presentation = new RetainedAdvTextLayoutPresentation(binding);
                _retainedTextLayouts.Add(binding.LayoutSlot, presentation);
            }
            else if (presentation.Binding != binding)
                throw new InvalidOperationException(
                    $"ADV layout {binding.LayoutSlot} changed its retained binding without reset.");
            int first = presentation.Append(
                rendered.Records,
                rendered.PresentationRects,
                run.Layout.OriginX,
                run.Layout.OriginY);
            result = new AdvRetainedTextRunResult(
                binding.LayoutSlot,
                first,
                rendered.ConsumedGlyphs,
                rendered.CursorX,
                rendered.CursorY);
        }

        lock (_imageLock)
            _surfaceImages[binding.SourceSurfaceSlot] = updated;
        _slotDims[binding.SourceSurfaceSlot] = (updated.Width, updated.Height);
        gfx.CreateSurface(binding.SourceSurfaceSlot);
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
        DrawStringPixels(surfaceSlot, x, y, text, style);
        _timeline?.Event("draw-string", new()
        {
            ["surface"] = surfaceSlot,
            ["x"] = x,
            ["y"] = y,
            ["text"] = text,
            ["presentation"] = "rgba-glyph-mask",
            ["backend"] = _surfaceTextBackendInfo.Id,
        });
    }

    private void DrawStringPixels(
        int surfaceSlot, int x, int y, string text, AdvTextStyle style)
    {
        RgbaImage? destination = ResolveSurfacePixels(surfaceSlot);
        if (destination == null
            && _slotDims.TryGetValue(surfaceSlot, out var dimensions)
            && dimensions.W > 0
            && dimensions.H > 0)
            destination = new RgbaImage(
                dimensions.W, dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
        if (destination == null || destination.Width <= 0 || destination.Height <= 0)
            throw new InvalidOperationException(
                $"Surface {surfaceSlot} has no rasterizable pixel allocation.");

        var updated = new RgbaImage(
            destination.Width, destination.Height, (byte[])destination.Pixels.Clone());
        _surfaceTextPixelRenderer.Render(updated, x, y, text, style);

        lock (_imageLock) _surfaceImages[surfaceSlot] = updated;
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
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

    /// <summary>
    /// Layout that owns the ordinary retained ADV page. Nested callback scripts such as HISTORY can select
    /// and mutate other layouts while the parent wait remains parked; those transient selections must not
    /// move the parent page when its retained range is restored.
    /// </summary>
    public int AdvPageLayoutSlot
    {
        get
        {
            lock (_textLock)
                return IsWaiting && _activeWaitLayout != 0 ? _activeWaitLayout : _currentAdvLayout;
        }
    }

    public void ClearRenderedAdvTextLayout(int layoutSlot)
    {
        lock (_textLock)
        {
            int slot = layoutSlot == 0 ? _currentAdvLayout : layoutSlot;
            if (_retainedTextLayouts.ContainsKey(slot)) return;
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

    public void ResetRenderedAdvTextLayout(
        GfxState gfx, AdvTextLayoutPresentationBinding binding)
    {
        lock (_textLock)
        {
            _retainedHistoryLayouts.Remove(binding.LayoutSlot);
            _retainedTextLayouts.Remove(binding.LayoutSlot);
            _liveText.RemoveAll(
                state => state.Run.Layout.Slot == binding.LayoutSlot);
            if (_activeLiveText?.Run.Layout.Slot == binding.LayoutSlot)
            {
                _activeLiveText = null;
                _advText = "";
                _advTextForceComplete = false;
                IsTextRevealing = false;
            }
            if (_suspendedRetainedTextLayoutSlot == binding.LayoutSlot)
                _suspendedRetainedTextLayoutSlot = 0;
        }

        if (_slotDims.TryGetValue(
                binding.SourceSurfaceSlot, out var dimensions)
            && dimensions.W > 0
            && dimensions.H > 0)
        {
            lock (_imageLock)
                _surfaceImages[binding.SourceSurfaceSlot] = new RgbaImage(
                    dimensions.W,
                    dimensions.H,
                    new byte[checked(dimensions.W * dimensions.H * 4)]);
        }
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("adv-text-layout-reset", new()
        {
            ["layout"] = binding.LayoutSlot,
            ["surface"] = binding.SourceSurfaceSlot,
            ["first_handle"] = binding.FirstObjectHandle,
            ["capacity"] = binding.ObjectCapacity,
        });
    }

    public bool RenderTextHistory(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvTextHistoryRenderBatch batch)
    {
        var run = new AdvLiveTextRun(
            batch.FirstRecordIndex,
            batch.Layout,
            batch.Style,
            batch.Text,
            Array.Empty<string>());
        if (binding.LayoutSlot != batch.LayoutSlot
            || binding.FirstObjectHandle < 0
            || binding.ObjectCapacity <= 0)
            throw new InvalidOperationException(
                $"History layout {batch.LayoutSlot} has no retained presentation binding.");
        PrepareRetainedTextRun(
            gfx, binding, run,
            out RetainedAdvTextLayoutPresentation? presentation,
            out AdvRetainedTextRunResult result);

        presentation.PublishThrough(
            gfx,
            checked(result.FirstGlyphIndex + result.GlyphCount));
        lock (_textLock)
        {
            _retainedHistoryLayouts.Add(batch.LayoutSlot);
        }
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("history-render", new()
        {
            ["layout"] = batch.LayoutSlot,
            ["record"] = batch.FirstRecordIndex,
            ["x"] = batch.Layout.OriginX + batch.Layout.CursorX,
            ["y"] = batch.Layout.OriginY + batch.Layout.CursorY,
            ["text"] = batch.Text,
            ["glyphs"] = result.GlyphCount,
            ["presentation"] = "retained-glyphs",
        });
        return true;
    }

    public void EndTextHistoryPresentation(GfxState gfx)
    {
        RetainedAdvTextLayoutPresentation[] retained;
        int[] surfaceSlots;
        lock (_textLock)
        {
            retained = _retainedHistoryLayouts
                .Select(slot => _retainedTextLayouts.GetValueOrDefault(slot))
                .Where(presentation => presentation != null)
                .Cast<RetainedAdvTextLayoutPresentation>()
                .ToArray();
            surfaceSlots = retained
                .Select(presentation => presentation.Binding.SourceSurfaceSlot)
                .Distinct()
                .ToArray();
            foreach (int slot in _retainedHistoryLayouts)
                _retainedTextLayouts.Remove(slot);
            _retainedHistoryLayouts.Clear();
        }

        foreach (RetainedAdvTextLayoutPresentation presentation in retained)
            presentation.ErasePublished(gfx);
        foreach (int surfaceSlot in surfaceSlots)
            ClearAllocatedSurfacePixels(surfaceSlot);
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("history-presentation-end", new()
        {
            ["retained_layouts"] = retained.Length,
        });
    }

    private void ClearAllocatedSurfacePixels(int surfaceSlot)
    {
        if (!_slotDims.TryGetValue(surfaceSlot, out var dimensions)
            || dimensions.W <= 0
            || dimensions.H <= 0)
            return;
        lock (_imageLock)
            _surfaceImages[surfaceSlot] = new RgbaImage(
                dimensions.W,
                dimensions.H,
                new byte[checked(dimensions.W * dimensions.H * 4)]);
    }

    public int MessageWindowAlphaSetting => _messageWindowAlphaSetting;

    public void SetMessageWindowAlphaSetting(int value)
    {
        _messageWindowAlphaSetting = value;
        _timeline?.Event("message-window-alpha", new() { ["value"] = value });
    }

    public void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config)
    {
        lock (_textLock)
        {
            _waitIndicators[config.LayoutSlot] = config;
            RebuildRetainedWaitIndicator(config.LayoutSlot);
        }
        _timeline?.Event("wait-indicator-config", new()
        {
            ["layout"] = config.LayoutSlot, ["x"] = config.X, ["y"] = config.Y,
            ["surface"] = config.SurfaceSlot, ["cell_w"] = config.CellWidth,
            ["cell_h"] = config.CellHeight, ["terminal_frame"] = config.TerminalFrame,
            ["period_ms"] = config.FramePeriodMs,
        });
    }

    public void BindAdvWaitIndicator(
        AdvTextLayoutPresentationBinding binding,
        AdvTextLayoutSnapshot layout)
    {
        lock (_textLock)
        {
            _waitIndicatorBindings[binding.LayoutSlot] = (binding, layout);
            RebuildRetainedWaitIndicator(binding.LayoutSlot);
        }
        _timeline?.Event("wait-indicator-bind", new()
        {
            ["layout"] = binding.LayoutSlot,
            ["handle"] = binding.WaitIndicatorObjectHandle,
            ["origin_x"] = layout.OriginX,
            ["origin_y"] = layout.OriginY,
        });
    }

    private void RebuildRetainedWaitIndicator(int layoutSlot)
    {
        if (!_waitIndicators.TryGetValue(layoutSlot, out AdvWaitIndicatorConfig config)
            || !_waitIndicatorBindings.TryGetValue(
                layoutSlot,
                out (AdvTextLayoutPresentationBinding Binding, AdvTextLayoutSnapshot Layout) retained)
            || retained.Binding.WaitIndicatorObjectHandle < 0)
        {
            _retainedWaitIndicators.Remove(layoutSlot);
            return;
        }

        _retainedWaitIndicators[layoutSlot] =
            new RetainedAdvWaitIndicatorPresentation(
                config,
                retained.Binding,
                retained.Layout.OriginX,
                retained.Layout.OriginY);
    }

    public void SetAdvWaitIndicatorEnabled(bool enabled)
    {
        lock (_textLock)
        {
            if (enabled && !_waitIndicatorEnabled) _waitIndicatorStartedMs = _clock.NowMs;
            _waitIndicatorEnabled = enabled;
        }
        Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("wait-indicator-enabled", new() { ["enabled"] = enabled });
    }

    public void PublishAdvTextLayout(int layoutSlot)
    {
        System.Threading.Interlocked.Exchange(ref _presentRequested, 1);
        _timeline?.Event("adv-text-layout-publish", new() { ["layout"] = layoutSlot });
    }

    public void PublishAdvTextLayout(
        GfxState gfx, AdvTextLayoutPresentationBinding binding)
    {
        lock (_textLock)
        {
            if (_retainedTextLayouts.TryGetValue(
                    binding.LayoutSlot, out RetainedAdvTextLayoutPresentation? presentation)
                && presentation.Binding == binding)
                presentation.Republish(gfx);
            if (_retainedWaitIndicators.TryGetValue(
                    binding.LayoutSlot,
                    out RetainedAdvWaitIndicatorPresentation? indicator))
                indicator.Republish(
                    gfx,
                    _clock.NowMs - _waitIndicatorStartedMs,
                    WaitIndicatorShouldBeVisible(binding.LayoutSlot));
        }
        PublishAdvTextLayout(binding.LayoutSlot);
    }

    private bool WaitIndicatorShouldBeVisible(int layoutSlot)
        => IsWaiting
           && !_advPagePresentationSuspended
           && _waitIndicatorEnabled
           && _activeWaitLayout == layoutSlot;

    private void UpdateRetainedWaitIndicator(GfxState gfx)
    {
        lock (_textLock)
        {
            foreach ((int layoutSlot, RetainedAdvWaitIndicatorPresentation presentation)
                     in _retainedWaitIndicators)
                presentation.Update(
                    gfx,
                    _clock.NowMs - _waitIndicatorStartedMs,
                    WaitIndicatorShouldBeVisible(layoutSlot));
        }
    }

}
