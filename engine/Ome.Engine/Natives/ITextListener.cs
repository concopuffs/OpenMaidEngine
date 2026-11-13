using Ome.Engine.Localization;
using Ome.Engine.Model;

namespace Ome.Engine.Natives;

/// <summary>
/// What the text native was asked to do, as it happens (<see cref="TextManager.Listen"/>), with or without devices to
/// draw it: the runs it showed, the strings and History groups it drew, its cursors, its wait indicator and the page's
/// suspension. Each call comes on the engine thread, after the native's own state changed. The interpreter attaches a
/// host that listens (a test's fake, the headless capture host), as it attaches one to the skip and auto native's
/// fast-forward changes (<see cref="IFastForwardListener"/>).
/// </summary>
public interface ITextListener
{
    /// <summary>Ops 0x6e and 0x196: a run shown, with the glyph delay its reveal runs at.</summary>
    void RunShown(AdvLiveTextRun run, int glyphDelayMilliseconds) { }

    /// <summary>A later run of a presented message, which draws nothing (localization spec §6.2).</summary>
    void HiddenRunShown(AdvLiveTextRun run) { }

    /// <summary>Ops 0x7a, 0x6f, 0x71 and end-text-line: a layout's pending record moved.</summary>
    void CursorMoved(int layoutSlot, int x, int y) { }

    /// <summary>Ops 0x204 and 0x205: a string drawn into a surface; <paramref name="presented"/> when the presenter
    /// replaced it.</summary>
    void StringDrawn(int surfaceSlot, int x, int y, string text, AdvTextStyle style, PresentedText? presented) { }

    /// <summary>Op 0x1d1: a History group drawn into its target layout.</summary>
    void HistoryGroupDrawn(AdvTextLayoutPresentationBinding binding, AdvTextHistoryRenderBatch batch) { }

    /// <summary>Op 0x82: a retained group drawn again at its records' rectangles.</summary>
    void GroupRedrawn(AdvTextLayoutPresentationBinding binding, AdvTextRedrawBatch batch) { }

    /// <summary>A layout's live text released (op 0x1fb's erase, slot 0 for the current layout) or reset (op 0x71).
    /// </summary>
    void LayoutCleared(int layoutSlot) { }

    /// <summary>HISTORY.BIN's recording re-enable (op 0x1bb 1) ended its presentation.</summary>
    void HistoryPresentationEnded() { }

    /// <summary>Ops 0x73 and 0x2bc.</summary>
    void WaitIndicatorConfigured(AdvWaitIndicatorConfig config) { }

    /// <summary>Op 0x1b1.</summary>
    void WaitIndicatorFollowsLastGlyph(bool enabled) { }

    /// <summary>Op 0x212.</summary>
    void WaitIndicatorBound(AdvTextLayoutPresentationBinding binding, AdvTextLayoutSnapshot layout) { }

    /// <summary>Op 0x1ce.</summary>
    void WaitIndicatorEnabled(bool enabled) { }

    /// <summary>Op 0x20a.</summary>
    void LayoutPublished(int layoutSlot) { }

    /// <summary>Op 0x199 suspends the page under its hide-window callback, op 0x7c restores it.</summary>
    void PageSuspended(bool suspended) { }
}
