using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Model;
using Ome.Engine.Text;

namespace Ome.Engine.Natives;

// The wait indicator (ops 0x73, 0x2bc, 0x1b1, 0x212, 0x1ce and 0x20a) and the page: which layout waits, whether its marker
// shows, and the page a hide-window callback suspends. Op 0x72's handler calls BeginPageWait and StopWaitIndicator around
// the host's blocking wait, which calls RestoreWaitIndicator after each input callback.
public sealed partial class TextManager
{
    private readonly Dictionary<int, AdvWaitIndicatorConfig> _waitIndicators = new();
    private bool _waitIndicatorFollowLastGlyph;
    private readonly Dictionary<int, (
        AdvTextLayoutPresentationBinding Binding,
        AdvTextLayoutSnapshot Layout)> _waitIndicatorBindings = new();
    private readonly Dictionary<int, RetainedAdvWaitIndicatorPresentation> _retainedWaitIndicators = new();
    private int _activeWaitLayout;
    private long _waitIndicatorStartedMs;
    private bool _waitIndicatorEnabled;
    private int _suspendedRetainedTextLayoutSlot;
    private volatile bool _advPagePresentationSuspended;

    public void ConfigureAdvWaitIndicator(AdvWaitIndicatorConfig config)
    {
        _waitIndicators[config.LayoutSlot] = config;
        RebuildRetainedWaitIndicator(config.LayoutSlot);
        _timeline?.Event("wait-indicator-config", new()
        {
            ["layout"] = config.LayoutSlot, ["x"] = config.X, ["y"] = config.Y,
            ["surface"] = config.SurfaceSlot, ["cell_w"] = config.CellWidth,
            ["cell_h"] = config.CellHeight, ["atlas_columns"] = config.AtlasColumns,
            ["terminal_frame"] = config.TerminalFrame,
            ["period_ms"] = config.FramePeriodMs,
        });
        Tell(listener => listener.WaitIndicatorConfigured(config));
    }

    public void SetAdvWaitIndicatorFollowLastGlyph(bool enabled)
    {
        _waitIndicatorFollowLastGlyph = enabled;
        foreach (int layoutSlot in _waitIndicators.Keys.ToArray())
            RebuildRetainedWaitIndicator(layoutSlot);
        _timeline?.Event("wait-indicator-follow-last-glyph", new() { ["enabled"] = enabled });
        Tell(listener => listener.WaitIndicatorFollowsLastGlyph(enabled));
    }

    public void BindAdvWaitIndicator(
        AdvTextLayoutPresentationBinding binding,
        AdvTextLayoutSnapshot layout)
    {
        _waitIndicatorBindings[binding.LayoutSlot] = (binding, layout);
        RebuildRetainedWaitIndicator(binding.LayoutSlot);
        _timeline?.Event("wait-indicator-bind", new()
        {
            ["layout"] = binding.LayoutSlot,
            ["handle"] = binding.WaitIndicatorObjectHandle,
            ["origin_x"] = layout.OriginX,
            ["origin_y"] = layout.OriginY,
        });
        Tell(listener => listener.WaitIndicatorBound(binding, layout));
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

        // SYS4433's indicator draw (0x452730) in follow mode adds the layout's last 20-byte record's left/top, which is
        // the pending record at the cursor, just past the last glyph.
        int anchorX = 0;
        int anchorY = 0;
        if (_waitIndicatorFollowLastGlyph && _layoutCursors.TryGetValue(layoutSlot, out var cursor))
            (anchorX, anchorY) = cursor;
        _retainedWaitIndicators[layoutSlot] =
            new RetainedAdvWaitIndicatorPresentation(
                config,
                retained.Binding,
                retained.Layout.OriginX,
                retained.Layout.OriginY,
                anchorX,
                anchorY);
    }

    public void SetAdvWaitIndicatorEnabled(bool enabled)
    {
        if (enabled && !_waitIndicatorEnabled) _waitIndicatorStartedMs = _clock.NowMs;
        _waitIndicatorEnabled = enabled;
        _graphics.RequestPresentation();
        _timeline?.Event("wait-indicator-enabled", new() { ["enabled"] = enabled });
        Tell(listener => listener.WaitIndicatorEnabled(enabled));
    }

    private void PublishAdvTextLayout(int layoutSlot)
    {
        _graphics.RequestPresentation();
        _timeline?.Event("adv-text-layout-publish", new() { ["layout"] = layoutSlot });
        Tell(listener => listener.LayoutPublished(layoutSlot));
    }

    /// <summary>Op 0x1fb: a layout's retained glyphs and wait indicator are bound again.</summary>
    public void PublishAdvTextLayout(
        GfxState gfx, AdvTextLayoutPresentationBinding binding)
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
        PublishAdvTextLayout(binding.LayoutSlot);
    }

    private bool WaitIndicatorShouldBeVisible(int layoutSlot)
        => InputWaiting
           && !_advPagePresentationSuspended
           && _waitIndicatorEnabled
           && _activeWaitLayout == layoutSlot;

    /// <summary>Each frame turn that presents: the wait indicators advance their animation and show only on the waiting
    /// layout.</summary>
    public void UpdateWaitIndicators(GfxState gfx)
    {
        foreach ((int layoutSlot, RetainedAdvWaitIndicatorPresentation presentation)
                 in _retainedWaitIndicators)
            presentation.Update(
                gfx,
                _clock.NowMs - _waitIndicatorStartedMs,
                WaitIndicatorShouldBeVisible(layoutSlot));
    }

    // ---- the page wait (op 0x72) ----

    /// <summary>Op 0x72's page wait begins on <paramref name="layoutSlot"/> (0: the current layout): its marker starts
    /// from its first frame, and the page's later reveals animate again (op_0x72_handler clears ctx+0x55108).</summary>
    public void BeginPageWait(int layoutSlot)
    {
        _activeWaitLayout = layoutSlot == 0 ? _currentAdvLayout : layoutSlot;
        _waitIndicatorStartedMs = _clock.NowMs;
        _waitIndicatorEnabled = true;
        _revealsFinishedUntilWait = false;
    }

    /// <summary>Whether the wait indicator is enabled (op 0x1b5's switch and the page wait's).</summary>
    public bool WaitIndicatorEnabled
    {
        get { return _waitIndicatorEnabled; }
    }

    /// <summary>After an input callback returns inside the page wait: native's callback returns through the shared ADV
    /// redraw and wait path, whose op 0x72 arms a marker again that a nested HISTORY stopped. The blocking host keeps the
    /// parent wait parked, so the parent's marker comes back at the same boundary when it was enabled before.</summary>
    public void RestoreWaitIndicator(bool wasEnabled)
    {
        if (wasEnabled && !_waitIndicatorEnabled)
        {
            _waitIndicatorEnabled = true;
            _waitIndicatorStartedMs = _clock.NowMs;
        }
    }

    /// <summary>An input wait ended (op 0x72's or op 0xfa's), by input, auto, fast-forward or an error: the marker
    /// stops.</summary>
    public void StopWaitIndicator()
    {
        _waitIndicatorEnabled = false;
    }

    // ---- the page a hide-window callback suspends ----

    public bool IsAdvPagePresentationSuspended => _advPagePresentationSuspended;

    private void SetAdvPagePresentationSuspended(bool suspended)
    {
        _advPagePresentationSuspended = suspended;
        _timeline?.State(suspended ? "adv-page-suspended" : "adv-page-restored", new());
        Tell(listener => listener.PageSuspended(suspended));
    }

    /// <summary>The page's glyphs and indicator are unbound while a callback hides the window, and bound again after:
    /// the waiting layout's, else the current one's.</summary>
    public void SetAdvPagePresentationSuspended(GfxState gfx, bool suspended)
    {
        if (suspended)
        {
            int slot = InputWaiting && _activeWaitLayout != 0
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
                    InputWaiting
                    && _waitIndicatorEnabled
                    && _activeWaitLayout == _suspendedRetainedTextLayoutSlot);
            _suspendedRetainedTextLayoutSlot = 0;
        }
        _graphics.RequestPresentation();
        SetAdvPagePresentationSuspended(suspended);
    }
}
