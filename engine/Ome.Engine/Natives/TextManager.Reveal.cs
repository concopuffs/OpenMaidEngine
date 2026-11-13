using System;
using Ome.Engine.Hosting;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Text;

namespace Ome.Engine.Natives;

// Showing a run (ops 0x6e and 0x196) and its reveal (adv_text_reveal_service_tick@0x406fa0): the run is laid out, drawn
// and recorded in History, and its glyphs publish one tick at a time on the glyph timer, or at once under fast-forward,
// after a click, or when no delay is set. The input and waits native's reveal wait blocks the engine thread and calls
// ServiceReveal until it ends.
public sealed partial class TextManager
{
    // ctx+0x5f630, the reveal's glyph timer: op 0x6e arms it, op 0x196 keeps polling the previous arm.
    private readonly PeriodTimer _glyphTimer = new();
    private bool _advTextForceComplete;
    // ctx+0x55108: a click (or a held action 4) finished a reveal; every later reveal publishes at once until op 0x72.
    private bool _revealsFinishedUntilWait;

    /// <summary>The run being revealed: its presentation, its glyph range, the ticks that publish it and how many have
    /// passed.</summary>
    private sealed class RevealState
    {
        public required RetainedAdvTextLayoutPresentation Presentation;
        public int FirstGlyphIndex;
        public int GlyphCount;
        public int TickCount;
        public int Ticks;
    }
    private RevealState? _reveal;

    /// <summary>
    /// Run-state 0x20000000: a run is revealing. Set as a run that may reveal starts (a delay, no fast-forward, and the
    /// page's reveals not finished), so a click or a held skip key during its layout reaches the reveal as it reaches
    /// native's after the op; ended when the run publishes at once, by its last tick, a click, fast-forward or a layout
    /// reset.
    /// </summary>
    public bool IsRevealing
    {
        get => _isRevealing;
        private set => _isRevealing = value;
    }
    private volatile bool _isRevealing;

    /// <summary>
    /// A show-text run (ops 0x6e and 0x196): drawn into its layout's surface and bound, then published at once, or armed
    /// for the reveal (<see cref="IsRevealing"/>), which the host's wait runs through <see cref="ServiceReveal"/>.
    /// History records the run's source rows, so it holds what the source-language run would have recorded, and the
    /// layout's cursors move (localization spec §8). Without devices nothing is drawn: History records one row at the
    /// cursor, and the result is null.
    /// </summary>
    /// <param name="layoutSlot">The layout the op named (0: the current one).</param>
    /// <param name="script">The script that shows the run, for <see cref="Emitted"/>.</param>
    /// <param name="lookup">What was looked up for a run the presenter resolved: History keeps it so a later language
    /// presents the run again, and a presented run cut or narrowed to fit is reported against it (§7.3).</param>
    /// <param name="endsLookupSpan">False while a presented message has runs to come: its lookup span stays open.</param>
    public AdvRetainedTextRunResult? ShowRun(GfxState gfx, AdvLiveTextRun run, int layoutSlot, string script,
                                             TextLookup? lookup = null, bool endsLookupSpan = true)
    {
        Emitted.Add((run.SourceOffset, run.Text, script));
        TextPresenter? presenter = _presenter;
        if (lookup is { } asked && presenter != null) History.BeginLookupSpan(asked, presenter.Language.Tag);
        int delay = RevealDelay(run.Presented, presenter);
        IsRevealing = delay > 0 && !_skip.FastForwardActive && !TextRevealsFinishedUntilWait;
        Tell(listener => listener.RunShown(run, delay));
        AdvRetainedTextRunResult? retained = _sourceText != null
            ? DrawRun(gfx, History.GetPresentationBinding(run.Layout.Slot), run, delay)
            : null;
        if (retained == null) IsRevealing = false;

        // History records the source rows (split at wraps), so it is written after layout but before the cursors move:
        // the drawn rows when the run drew the source text where native would, else its source layout pass (§8).
        AdvTextSourceLayout? source = retained is { } drawn
            ? drawn.Source ?? new AdvTextSourceLayout(drawn.Rows, drawn.Ruby, drawn.CursorX, drawn.CursorY)
            : null;
        History.AppendText(layoutSlot, run.SourceOffset, run.Text, Style, rows: source?.Rows, ruby: source?.Ruby);
        if (retained is { } result)
            History.SetRunCursors(result.LayoutSlot, source!.CursorX, source.CursorY, result.CursorX, result.CursorY);
        // Presented text that needs more rows than its window has is narrowed until it fits, or cut; either is
        // reported (§7.3).
        if (lookup is { } reported && presenter != null)
        {
            if (retained is { CutRows: > 0 } cut) presenter.ReportCut(reported, cut.CutRows);
            if (retained is { NarrowedToFitPercent: > 0 } narrowed)
                presenter.ReportNarrowed(reported, narrowed.NarrowedToFitPercent);
        }
        if (endsLookupSpan) History.EndLookupSpan();
        return retained;
    }

    /// <summary>A later run of a presented message (localization spec §6.2): its first run drew the sentence, so this
    /// one draws nothing. History records its source rows, and only the source cursor moves (§8).</summary>
    public void ShowHiddenRun(AdvLiveTextRun run, int layoutSlot, string script, bool endsLookupSpan)
    {
        Emitted.Add((run.SourceOffset, run.Text, script));
        Tell(listener => listener.HiddenRunShown(run));
        AdvTextSourceLayout? source = LayoutHiddenTextRun(History.GetPresentationBinding(run.Layout.Slot), run);
        History.AppendText(layoutSlot, run.SourceOffset, run.Text, Style, rows: source?.Rows, ruby: source?.Ruby);
        if (source != null) History.SetSourceCursor(layoutSlot, source.CursorX, source.CursorY);
        if (endsLookupSpan) History.EndLookupSpan();   // the message's last run
    }

    /// <summary>The run drawn into its layout's surface and bound, then published at once or armed for the reveal.
    /// </summary>
    private AdvRetainedTextRunResult DrawRun(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvLiveTextRun run,
        int delay)
    {
        if (binding.LayoutSlot != run.Layout.Slot
            || binding.FirstObjectHandle < 0
            || binding.ObjectCapacity <= 0)
            throw new InvalidOperationException(
                $"ADV layout {run.Layout.Slot} has no retained presentation binding.");
        _runGeometry = null;
        _runCut = false;
        PrepareRetainedTextRun(
            gfx, binding, run,
            out RetainedAdvTextLayoutPresentation? presentation,
            out AdvRetainedTextRunResult result);

        _locator?.Text(run.SourceOffset, run.Text, PresentsText ? run.Presented?.Text ?? run.Text : null, _runGeometry,
                       _runCut);
        int revealGlyphCount = Math.Max(
            0, Math.Min(
                result.GlyphCount,
                presentation.PublishableGlyphCount - result.FirstGlyphIndex));
        // A tick publishes one record, or a chained group after a ruby (adv_text_publish_next_glyph@0x451220).
        int revealTicks = presentation.PublicationTicks(result.FirstGlyphIndex, revealGlyphCount);
        _activeRunLayout = run.Layout.Slot;
        _currentAdvLayout = run.Layout.Slot;
        // Read skip takes the blocking builder (no timer); a finished-until-wait reveal still arms it.
        bool incremental = revealGlyphCount > 0 && delay > 0 && !_skip.FastForwardActive;
        if (incremental && run.ArmsGlyphTimer) _glyphTimer.Arm(_clock.NowMs, delay);
        _advTextForceComplete = _skip.FastForwardActive || _revealsFinishedUntilWait;
        _reveal = new RevealState
        {
            Presentation = presentation,
            FirstGlyphIndex = result.FirstGlyphIndex,
            GlyphCount = revealGlyphCount,
            TickCount = revealTicks,
        };
        IsRevealing = incremental && !_advTextForceComplete;
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

        if (!IsRevealing)
        {
            _reveal = null;
            presentation.PublishThrough(gfx, checked(result.FirstGlyphIndex + revealGlyphCount));
            _graphics.RequestPresentation();
            _timeline?.State("running", new() { ["text_reveal_complete"] = true });
        }
        return result;
    }

    /// <summary>
    /// One engine tick of the reveal (adv_text_reveal_service_tick@0x406fa0, draw mode 1): poll the glyph timer and
    /// publish one tick when it succeeds, so the first tick also waits a full delay and a late poll drops missed
    /// boundaries; a forced reveal publishes the rest. Returns how long the waiting thread sleeps before the next tick, in
    /// real milliseconds, or null once the reveal has ended.
    /// </summary>
    public int? ServiceReveal(GfxState gfx)
    {
        if (!IsRevealing || _reveal is not { } reveal) return null;
        long? due;
        if (_advTextForceComplete) reveal.Ticks = reveal.TickCount;
        else if (_glyphTimer.Poll(_clock.NowMs) >= 0) reveal.Ticks++;
        if (reveal.Ticks >= reveal.TickCount) IsRevealing = false;
        due = _glyphTimer.NextDueMs;
        RetainedAdvTextLayoutPresentation presentation = reveal.Presentation;
        int before = presentation.PublishedGlyphCount;
        presentation.PublishThrough(
            gfx, presentation.PublicationTarget(reveal.FirstGlyphIndex, reveal.GlyphCount, reveal.Ticks));
        if (presentation.PublishedGlyphCount != before)
            _graphics.RequestPresentation();
        if (!IsRevealing)
        {
            _reveal = null;
            return null;
        }
        return due is long deadline ? _clock.RealMillisecondsUntil(deadline, 50) : 50;
    }

    /// <summary>A click while a run reveals: it publishes at once, and so does every later reveal of the page (ctx+0x55108,
    /// which adv_text_reveal_service_tick@0x406fa0 sets). False when nothing was revealing: the click is the page's.
    /// </summary>
    public bool FinishRevealOnClick()
    {
        if (!IsRevealing) return false;
        _advTextForceComplete = _revealsFinishedUntilWait = true;
        _timeline?.State("text-reveal-forced-complete", new());
        return true;
    }

    /// <summary>Fast-forward started, or the engine is stopping: the reveal in progress publishes the rest.</summary>
    public void CompleteReveal()
    {
        _advTextForceComplete = true;
    }

    /// <summary>A held action 4 (the VM's input latch): the page's reveals publish at once until op 0x72.</summary>
    public void FinishTextRevealsUntilWait()
    {
        _revealsFinishedUntilWait = true;
        _timeline?.State("text-reveal-finish-held", new());
    }

    /// <summary>Whether ctx+0x55108 is set: while it is, a reveal publishes without polling the input latch.</summary>
    public bool TextRevealsFinishedUntilWait
    {
        get { return _revealsFinishedUntilWait; }
    }
}
