using System;
using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Text;

namespace Ome.Engine.Natives;

// Laying runs out and drawing them: a show-text run into its layout's surface, a hidden run's source pass, op 0x204's
// strings, op 0x1d1's History groups and op 0x82's redraws. Without devices nothing is drawn.
public sealed partial class TextManager
{
    private readonly HashSet<int> _retainedHistoryLayouts = new();
    private readonly Dictionary<int, RetainedAdvTextLayoutPresentation> _retainedTextLayouts = new();
    // The layout of the last run shown: releasing or resetting it stops that run's reveal.
    private int _activeRunLayout;
    // Each layout's pending record (left, top): AGE draws into the layout's last 20-byte record and pushes the next one
    // at the cursor, which op 0x7a, op 0x6f and a reset move. The follow-last-glyph wait indicator anchors here.
    private readonly Dictionary<int, (int X, int Y)> _layoutCursors = new();
    private int _currentAdvLayout = 1; // SYSTEM4's ordinary SC0000 ADV layout

    /// <summary>The page map's view of the run being shown (<see cref="PageLocatorState"/>): its source layout's
    /// geometry, set by the pass that laid the source text out, and whether the drawn run stopped at its layout's
    /// bottom. The engine thread sets and reads them around one run.</summary>
    private PageTextGeometry? _runGeometry;
    private bool _runCut;

    /// <summary>
    /// A later run of a presented message (localization spec §6.2): the message's first run drew the whole presented
    /// sentence, so this run draws nothing. The page map and the locator keep its source text (§14), and its source
    /// layout (§8) is what history records and the source cursor follows. Null without devices: history then records
    /// one row at the cursor.
    /// </summary>
    private AdvTextSourceLayout? LayoutHiddenTextRun(AdvTextLayoutPresentationBinding binding, AdvLiveTextRun run)
    {
        if (_sourceText == null) return null;
        // A hidden run draws nothing: its message's first run drew the presented text.
        if (run.Layout.Width <= 0 || run.Layout.Height <= 0)
        {
            _locator?.Text(run.SourceOffset, run.Text, "", null);
            return null;
        }
        _runGeometry = null;
        AdvTextSourceLayout source = LayoutSourceText(binding, run, out IReadOnlyList<AdvRetainedGlyphRecord> glyphs, out _);
        _locator?.Text(run.SourceOffset, run.Text, "", _runGeometry);
        if (binding.LayoutSlot <= 0 || binding.LayoutSlot != run.Layout.Slot || binding.SourceSurfaceSlot < 0
            || binding.FirstObjectHandle < 0 || binding.ObjectCapacity <= 0)
            return source;
        lock (_textLock)
        {
            if (!_retainedTextLayouts.TryGetValue(binding.LayoutSlot, out RetainedAdvTextLayoutPresentation? presentation))
            {
                presentation = new RetainedAdvTextLayoutPresentation(binding);
                _retainedTextLayouts.Add(binding.LayoutSlot, presentation);
            }
            // Drawn by the message's first run; a save binds what the source-language run would have drawn.
            if (presentation.Binding == binding)
                presentation.AppendSourceOnly(glyphs, run.Layout.OriginX, run.Layout.OriginY);
        }
        return source;
    }

    /// <summary>The layouts whose bound glyphs are not what the source-language run would have bound, with its glyphs
    /// (localization spec §8). A numbered save writes those in their place.</summary>
    public IReadOnlyList<AdvTextSourceObjects> SourceTextObjects
    {
        get
        {
            lock (_textLock)
                return _retainedTextLayouts.Values
                    .Where(presentation => presentation.SourceDiffers)
                    .Select(presentation => new AdvTextSourceObjects(
                        presentation.Binding.FirstObjectHandle, presentation.Binding.ObjectCapacity,
                        presentation.Binding.SourceSurfaceSlot, presentation.PublishedSourceGlyphs()))
                    .ToArray();
        }
    }

    /// <summary>
    /// The source layout pass (localization spec §8): <paramref name="run"/>'s source text and ruby laid out with the
    /// gameplay rasterizer from the source cursor, as the run would draw with no language selected, without drawing.
    /// History records its rows, so a save holds what the source-language run would have recorded, and
    /// <paramref name="glyphs"/> are its records in drawing order, which a save binds in place of the drawn glyphs.
    /// <paramref name="extent"/> is where its ink runs, which an entry's <c>align</c> and <c>fit</c> place presented
    /// text against (§7.2).
    /// </summary>
    private AdvTextSourceLayout LayoutSourceText(AdvTextLayoutPresentationBinding binding, AdvLiveTextRun run,
                                                 out IReadOnlyList<AdvRetainedGlyphRecord> glyphs, out TextExtent? extent)
    {
        TextPipeline text = _sourceText!;
        AspectFontRealization? realization = RealizationFor(text);
        (int x, int y) = run.SourceCursor ?? (run.Layout.CursorX, run.Layout.CursorY);
        GlyphTextLayoutResult laid = text.Layout.Render(
            null,
            new GlyphTextLayoutOptions(
                x, y, binding.ResetCursorX, run.Layout.Right, run.Layout.Bottom, WrapHorizontally: true, run.Style,
                FontRealization: realization),
            ImmediateSurfaceTextRenderer.CreateRequests(run.Text, run.Style, text.Policy, _textAntialias, realization));
        IReadOnlyList<AdvRubyGlyph> ruby = !string.IsNullOrEmpty(run.SourceRuby) && laid.Rows.Count > 0
            ? text.Layout.RenderRuby(
                null, run.Text, laid.Rows, run.SourceRuby, run.Style, text.Policy, _textAntialias, _rubyRules)
            : [];
        glyphs = OrderedGlyphs(laid, ruby, byRow: !string.IsNullOrEmpty(run.SourceRuby) && laid.Rows.Count > 0)
            .Select(item => item.Record).ToArray();
        extent = PresentedTextPlacement.Extent(run.Text, laid.Records);
        _runGeometry = PageGeometry(run, laid, text, realization);
        return new AdvTextSourceLayout(laid.Rows, ruby, laid.CursorX, laid.CursorY);
    }

    /// <summary>The page map's geometry of <paramref name="run"/>'s source text laid out as <paramref name="laid"/>
    /// with <paramref name="text"/>'s rasterizer; null unless the locator writes a map.</summary>
    private PageTextGeometry? PageGeometry(AdvLiveTextRun run, GlyphTextLayoutResult laid, TextPipeline text,
                                           AspectFontRealization? realization)
    {
        if (_locator is not { WritesMap: true } || laid.Rows.Count == 0) return null;
        int space = text.Layout.Render(
            null,
            new GlyphTextLayoutOptions(0, 0, 0, int.MaxValue, int.MaxValue, WrapHorizontally: false, run.Style,
                                       FontRealization: realization),
            ImmediateSurfaceTextRenderer.CreateRequests(" ", run.Style, text.Policy, _textAntialias, realization)).CursorX;
        int height = run.Style.PrimaryFontSize > 0 ? run.Style.PrimaryFontSize : laid.Rows[0].Bottom - laid.Rows[0].Top;
        return PageTextGeometry.Measure(run.Text, laid.Rows, laid.Records, run.Layout.Slot, run.Layout.Right,
                                        height + run.Style.LineSpacing, space);
    }

    /// <summary>A run's records in AGE's drawing order: with ruby (<paramref name="byRow"/>), op 0x196's ruby follows
    /// each base row.</summary>
    private static IEnumerable<(AdvRetainedGlyphRecord Record, bool IsRuby)> OrderedGlyphs(
        GlyphTextLayoutResult rendered, IReadOnlyList<AdvRubyGlyph> ruby, bool byRow)
    {
        if (!byRow)
        {
            foreach (AdvRetainedGlyphRecord record in rendered.Records) yield return (record, false);
            yield break;
        }
        for (int row = 0; row < rendered.Rows.Count; row++)
        {
            AdvTextRow drawn = rendered.Rows[row];
            for (int glyph = drawn.FirstGlyph; glyph < drawn.FirstGlyph + drawn.GlyphCount; glyph++)
                yield return (rendered.Records[glyph], false);
            foreach (AdvRubyGlyph character in ruby)
                if (character.Row == row) yield return (character.Record, true);
        }
    }

    /// <param name="recordedLineBreaks">History's retained rows (op 0x1d1): break only at these glyph
    /// indices, never at the layout's edges. Native text_history_render_records@0x4526c0 checks no bounds;
    /// the surface clips what falls outside.</param>
    /// <param name="historyRuby">History's ruby runs (op 0x1d1), each drawn over its base glyphs.</param>
    /// <param name="history">Op 0x1d1's group: no bottom bound, as native checks none, and no source layout pass.</param>
    private void PrepareRetainedTextRun(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvLiveTextRun run,
        out RetainedAdvTextLayoutPresentation presentation,
        out AdvRetainedTextRunResult result,
        IReadOnlyList<int>? recordedLineBreaks = null,
        IReadOnlyList<AdvTextRubyAnnotation>? historyRuby = null,
        bool history = false)
    {
        presentation = null!;
        result = default;
        if (run.Layout.Width <= 0 || run.Layout.Height <= 0)
            throw new InvalidOperationException(
                $"ADV layout {run.Layout.Slot} has invalid dimensions " +
                $"{run.Layout.Width}x{run.Layout.Height}.");

        RgbaImage destination = _graphics.SurfacePixels(binding.SourceSurfaceSlot)
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
        // A presented run always draws with the Unicode rasterizer, even if the language changed meanwhile.
        TextPipeline text = run.Presented != null ? _presentedText!.Value : ActiveText;
        string drawnText = run.Presented?.Text ?? run.Text;
        AdvTextStyle style = run.Presented != null ? PresentedStyle(run.Style) : run.Style;
        AspectFontRealization? realization = run.Presented != null ? null : RealizationFor(text);
        IReadOnlyList<GlyphRasterRequest> requests =
            ImmediateSurfaceTextRenderer.CreateRequests(drawnText, style, text.Policy, _textAntialias, realization);
        IReadOnlyList<int>? breaks = recordedLineBreaks;
        int rightBound = recordedLineBreaks == null ? run.Layout.Right : int.MaxValue;
        bool wrap = recordedLineBreaks == null;

        // What history records and a save binds (spec §8): the drawn rows when the run drew its source text with the
        // gameplay rasterizer from the source cursor, else a separate source layout pass, laid out first: presented text
        // aligns and fits against its extent (§7.2).
        IReadOnlyList<AdvRetainedGlyphRecord>? sourceGlyphs = null;
        TextExtent? sourceExtent = null;
        AdvTextSourceLayout? source = !history
                                      && (run.Presented != null || run.SourceCursor != null
                                          || !ReferenceEquals(text, _sourceText))
            ? LayoutSourceText(binding, run, out sourceGlyphs, out sourceExtent)
            : null;

        GlyphTextLayoutOptions LayoutOptions(int right, bool wrapRows, IReadOnlyList<int>? lineBreaks,
                                             IReadOnlyList<int>? rowShifts)
            => new(run.Layout.CursorX, run.Layout.CursorY, binding.ResetCursorX, right,
                   history ? int.MaxValue : run.Layout.Bottom, WrapHorizontally: wrapRows, style, lineBreaks, realization,
                   OverflowAtFontHeight: run.Presented != null, RowShiftsX: rowShifts);

        PresentedRunPlacement? placement = null;
        if (run.Presented is { } presented && recordedLineBreaks == null)
        {
            // Presented text breaks where the presenter allows (§7.1), planned before drawing, and is narrowed and
            // aligned as its entry asks (§7.2); history has no source pass and draws it as written. A language that
            // shrinks overflow tries each width with the layout that draws it, without drawing (§7.3).
            Func<PresentedRunPlacement, bool>? fitsWindow =
                presented.Overflow == Ome.Engine.Localization.TextOverflow.Shrink && !history
                    ? candidate => !text.Layout.Render(
                        null, LayoutOptions(int.MaxValue, false, candidate.Plan.BreaksBefore, candidate.RowShiftsX),
                        candidate.Plan.Glyphs).StoppedOnVerticalOverflow
                    : null;
            placement = PresentedTextPlacement.Place(
                drawnText, Ome.Engine.Localization.PresentedLineBreaks.Opportunities(drawnText, presented.LineBreak), requests,
                text.Cache, run.Layout.CursorX, binding.ResetCursorX, run.Layout.Right,
                presented.Entry?.Align ?? Ome.Engine.Localization.TextAlign.Start,
                presented.Entry?.Fit ?? Ome.Engine.Localization.TextFit.None, sourceExtent, source?.Rows.Count ?? 0,
                presented.MinFitScale, fitsWindow);
            requests = placement.Plan.Glyphs;
            breaks = placement.Plan.BreaksBefore;
            rightBound = int.MaxValue;
            wrap = false;
        }
        GlyphTextLayoutResult rendered = text.Layout.Render(
            updated, LayoutOptions(rightBound, wrap, breaks, placement?.RowShiftsX), requests);
        // Presented rows below the layout's bottom are cut and reported (§7.3); history has no bottom.
        int cutRows = placement != null && !history && rendered.StoppedOnVerticalOverflow
            ? PresentedTextPlacement.CutRows(placement.Plan, rendered.ConsumedGlyphs)
            : 0;

        // Op 0x196's ruby follows each base row in AGE's drawing order; History draws its ruby runs after the text.
        IReadOnlyList<AdvRubyGlyph> ruby = [];
        var ordered = new List<AdvRetainedGlyphRecord>(rendered.Records.Count);
        var orderedIsRuby = new List<bool>(rendered.Records.Count);
        void Add(AdvRetainedGlyphRecord record, bool isRuby)
        {
            ordered.Add(record);
            orderedIsRuby.Add(isRuby);
        }
        if (!string.IsNullOrEmpty(run.Ruby) && rendered.Rows.Count > 0)
        {
            ruby = text.Layout.RenderRuby(
                updated, run.Text, rendered.Rows, run.Ruby, run.Style,
                text.Policy, _textAntialias, _rubyRules);
            foreach ((AdvRetainedGlyphRecord record, bool isRuby) in OrderedGlyphs(rendered, ruby, byRow: true))
                Add(record, isRuby);
        }
        else
        {
            foreach (AdvRetainedGlyphRecord record in rendered.Records) Add(record, false);
            foreach (AdvTextRubyAnnotation annotation in historyRuby ?? [])
            {
                int firstBase = annotation.BaseFirstGlyph;
                int lastBase = firstBase + annotation.BaseGlyphCount - 1;
                if (annotation.BaseGlyphCount <= 0 || lastBase >= rendered.Records.Count) continue;
                AdvRetainedGlyphRecord left = rendered.Records[firstBase], right = rendered.Records[lastBase];
                foreach (AdvRetainedGlyphRecord record in text.Layout.RenderRubyOver(
                             updated, left.Left, left.Top, right.Right - left.Left, annotation.Text, run.Style,
                             text.Policy, _textAntialias, _rubyRules, history: true))
                    Add(record, true);
            }
        }

        if (!history)
        {
            if (source == null) _runGeometry = PageGeometry(run, rendered, text, realization);
            _runCut = rendered.StoppedOnVerticalOverflow;
        }

        _timeline?.Event("adv-text-layout-result", new()
        {
            ["layout"] = binding.LayoutSlot,
            ["text"] = run.Text,
            ["presented"] = run.Presented?.Text,
            ["backend"] = text.Info.Id,
            ["cursor_x"] = run.Layout.CursorX,
            ["cursor_y"] = run.Layout.CursorY,
            ["right"] = run.Layout.Right,
            ["bottom"] = run.Layout.Bottom,
            ["font"] = run.Style.FontFace,
            ["font_size"] = run.Style.PrimaryFontSize,
            ["line_spacing"] = run.Style.LineSpacing,
            ["requested_glyphs"] = requests.Count,
            ["rendered_glyphs"] = rendered.ConsumedGlyphs,
            ["ruby"] = run.Ruby ?? "",
            ["ruby_glyphs"] = orderedIsRuby.Count(isRuby => isRuby),
            ["wrapped_lines"] = rendered.WrappedLines,
            ["vertical_overflow"] = rendered.StoppedOnVerticalOverflow,
            ["result_cursor_x"] = rendered.CursorX,
            ["result_cursor_y"] = rendered.CursorY,
            ["source_cursor_x"] = source?.CursorX,
            ["source_cursor_y"] = source?.CursorY,
            ["width_percent"] = placement?.WidthPercent,
            ["narrowed_to_fit"] = placement?.NarrowedToFit,
            ["row_shifts"] = placement?.RowShiftsX is { } shifts ? string.Join(",", shifts) : null,
            ["cut_rows"] = cutRows,
        });

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
            int first = presentation.AppendRun(
                ordered,
                orderedIsRuby,
                run.Layout.OriginX,
                run.Layout.OriginY,
                _rubyRules.ChainEveryRubyRecord,
                sourceGlyphs);
            _layoutCursors[binding.LayoutSlot] = (rendered.CursorX, rendered.CursorY);
            RebuildRetainedWaitIndicator(binding.LayoutSlot);
            result = new AdvRetainedTextRunResult(
                binding.LayoutSlot,
                first,
                ordered.Count,
                rendered.CursorX,
                rendered.CursorY)
            {
                Rows = rendered.Rows, Ruby = ruby, Source = source, CutRows = cutRows,
                NarrowedToFitPercent = placement is { NarrowedToFit: true } ? placement.WidthPercent : 0,
            };
        }

        _graphics.PublishSurfacePixels(binding.SourceSurfaceSlot, updated);
        gfx.CreateSurface(binding.SourceSurfaceSlot);
    }

    /// <summary>Ops 0x7a, 0x6f and 0x71 move a layout's pending record; slot 0 is the current layout, and a nonzero
    /// slot becomes current. The follow-last-glyph wait indicator anchors there.</summary>
    public void SetAdvTextCursor(int layoutSlot, int x, int y)
    {
        lock (_textLock)
        {
            if (layoutSlot != 0) _currentAdvLayout = layoutSlot;
            int slot = layoutSlot != 0 ? layoutSlot : _currentAdvLayout;
            _layoutCursors[slot] = (x, y);
            RebuildRetainedWaitIndicator(slot);
        }
        _timeline?.Event("text-cursor", new() { ["slot"] = layoutSlot, ["x"] = x, ["y"] = y });
        Tell(listener => listener.CursorMoved(layoutSlot, x, y));
    }

    /// <summary>Op 0x204/0x205 (adv_text_draw_string_to_surface): rasterize <paramref name="text"/> into a numbered
    /// surface before op 0x1fb binds it. A string the presenter replaced draws <paramref name="presented"/>'s text, a
    /// "\n" in it starting a row at the same x (localization spec §5.2, §7.1).</summary>
    public void DrawStringToSurface(int surfaceSlot, int x, int y, string text, AdvTextStyle style,
                                    Ome.Engine.Localization.PresentedText? presented = null)
    {
        Tell(listener => listener.StringDrawn(surfaceSlot, x, y, text, style, presented));
        if (_sourceText == null) return;
        TextPipeline pipeline = presented != null ? _presentedText!.Value : ActiveText;
        string? placed = DrawStringPixels(surfaceSlot, x, y, text, style, pipeline, presented);
        _timeline?.Event("draw-string", new()
        {
            ["surface"] = surfaceSlot,
            ["x"] = x,
            ["y"] = y,
            ["text"] = text,
            ["presented"] = presented?.Text,
            ["placement"] = placed,
            ["presentation"] = "rgba-glyph-mask",
            ["backend"] = pipeline.Info.Id,
        });
    }

    /// <summary>The gap between the rows of a presented <c>draw-string</c> when its entry sets no <c>row_gap</c>
    /// (localization spec §7.1).</summary>
    private const int PresentedRowGap = 2;

    /// <returns>For an entry that aligns or fits, how its rows were placed (§7.2): the source string's extent, the
    /// glyph width in percent and each row's shift; else null.</returns>
    private string? DrawStringPixels(
        int surfaceSlot, int x, int y, string text, AdvTextStyle style, TextPipeline pipeline,
        Ome.Engine.Localization.PresentedText? presented)
    {
        RgbaImage? destination = _graphics.SurfacePixels(surfaceSlot);
        if (destination == null
            && _graphics.TryGetSurfaceSize(surfaceSlot, out var dimensions)
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
        string? placed = null;
        if (presented == null)
            pipeline.Pixels.Render(updated, x, y, text, style, _textAntialias, RealizationFor(pipeline), _blankExtentMode);
        else
        {
            // Each "\n" of the presented text starts a row at the same x, lower by the font's height and a gap.
            AdvTextStyle presentedStyle = PresentedStyle(style);
            int height = style.PrimaryFontSize > 0 ? style.PrimaryFontSize : 24;
            int step = height + (presented.Entry?.RowGap ?? PresentedRowGap);
            IReadOnlyList<GlyphRasterRequest>[] rows = presented.Text.Split('\n')
                .Select(row => ImmediateSurfaceTextRenderer.CreateRequests(
                    new string(row.Where(c => c >= ' ').ToArray()), presentedStyle, pipeline.Policy, _textAntialias))
                .ToArray();
            // An entry's align and fit place its rows against what the source string would have covered (§7.2).
            Ome.Engine.Localization.TextAlign align = presented.Entry?.Align ?? Ome.Engine.Localization.TextAlign.Start;
            bool fit = presented.Entry?.Fit == Ome.Engine.Localization.TextFit.Shrink;
            TextExtent? source = align != Ome.Engine.Localization.TextAlign.Start || fit
                ? _sourceText!.Pixels.Render(null, x, y, text, style, _textAntialias, RealizationFor(_sourceText),
                                             _blankExtentMode).Ink
                : null;
            int percent = fit && source is { } fitted
                ? PresentedTextPlacement.FitPercent(rows, pipeline.Cache, fitted.Width, presented.MinFitScale)
                : 100;
            if (percent < 100)
                rows = rows.Select(row => (IReadOnlyList<GlyphRasterRequest>)row
                    .Select(glyph => glyph with { WidthPercent = percent }).ToArray()).ToArray();
            var shifts = new int[rows.Length];
            for (int row = 0; row < rows.Length; row++)
            {
                if (rows[row].Count == 0) continue;
                shifts[row] = source is { } target && align != Ome.Engine.Localization.TextAlign.Start
                              && PresentedTextPlacement.Extent(rows[row], pipeline.Cache, x) is { } extent
                    ? PresentedTextPlacement.Shift(align, target, extent, 0, updated.Width)
                    : 0;
                pipeline.Pixels.RenderGlyphs(updated, checked(x + shifts[row]), checked(y + row * step), rows[row],
                                             presentedStyle);
            }
            if (source is { } placedAgainst)
                placed = $"source {placedAgainst.Left}..{placedAgainst.Right}, width {percent}%, shifts {string.Join(",", shifts)}";
        }

        _graphics.PublishSurfacePixels(surfaceSlot, updated, updateSize: false);
        _graphics.RequestPresentation();
        return placed;
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
                return InputWaiting && _activeWaitLayout != 0 ? _activeWaitLayout : _currentAdvLayout;
        }
    }

    /// <summary>Op 0x1fb's release of a layout's live text (no retained presentation): the run being revealed there
    /// stops.</summary>
    public void ClearRenderedAdvTextLayout(int layoutSlot)
    {
        lock (_textLock)
        {
            int slot = layoutSlot == 0 ? _currentAdvLayout : layoutSlot;
            if (!_retainedTextLayouts.ContainsKey(slot) && _activeRunLayout == slot)
            {
                _activeRunLayout = 0;
                _advTextForceComplete = false;
                IsRevealing = false;
            }
        }
        Tell(listener => listener.LayoutCleared(layoutSlot));
    }

    /// <summary>Op 0x71: a layout's retained glyphs and History binding go, and its surface is cleared.</summary>
    public void ResetRenderedAdvTextLayout(GfxState gfx, AdvTextLayoutPresentationBinding binding)
    {
        lock (_textLock)
        {
            _retainedHistoryLayouts.Remove(binding.LayoutSlot);
            _retainedTextLayouts.Remove(binding.LayoutSlot);
            if (_activeRunLayout == binding.LayoutSlot)
            {
                _activeRunLayout = 0;
                _advTextForceComplete = false;
                IsRevealing = false;
            }
            if (_suspendedRetainedTextLayoutSlot == binding.LayoutSlot)
                _suspendedRetainedTextLayoutSlot = 0;
        }
        Tell(listener => listener.LayoutCleared(binding.LayoutSlot));
        if (_sourceText == null) return;

        if (_graphics.TryGetSurfaceSize(
                binding.SourceSurfaceSlot, out var dimensions)
            && dimensions.W > 0
            && dimensions.H > 0)
        {
            _graphics.PublishSurfacePixels(
                binding.SourceSurfaceSlot,
                new RgbaImage(dimensions.W, dimensions.H, new byte[checked(dimensions.W * dimensions.H * 4)]),
                updateSize: false);
        }
        _graphics.RequestPresentation();
        _timeline?.Event("adv-text-layout-reset", new()
        {
            ["layout"] = binding.LayoutSlot,
            ["surface"] = binding.SourceSurfaceSlot,
            ["first_handle"] = binding.FirstObjectHandle,
            ["capacity"] = binding.ObjectCapacity,
        });
    }

    /// <summary>Op 0x1d1: draw one History group into its target layout and bind it. False without devices. Public for
    /// the Godot self-test, which draws a batch it builds; the op goes through <see cref="RenderHistoryGroup"/>.</summary>
    public bool RenderTextHistory(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvTextHistoryRenderBatch batch)
    {
        Tell(listener => listener.HistoryGroupDrawn(binding, batch));
        if (_sourceText == null) return false;
        // A presented group (localization spec §8) wraps in the target layout as live presented text does.
        var run = new AdvLiveTextRun(
            batch.FirstRecordIndex,
            batch.Layout,
            batch.Style,
            batch.Text,
            Array.Empty<string>()) { Presented = batch.Presented };
        if (binding.LayoutSlot != batch.LayoutSlot
            || binding.FirstObjectHandle < 0
            || binding.ObjectCapacity <= 0)
            throw new InvalidOperationException(
                $"History layout {batch.LayoutSlot} has no retained presentation binding.");
        PrepareRetainedTextRun(
            gfx, binding, run,
            out RetainedAdvTextLayoutPresentation? presentation,
            out AdvRetainedTextRunResult result,
            batch.Presented == null ? batch.RecordedLineBreaks : null,
            batch.Presented == null ? batch.Ruby : null,
            history: true);

        presentation.PublishThrough(
            gfx,
            checked(result.FirstGlyphIndex + result.GlyphCount));
        lock (_textLock)
        {
            _retainedHistoryLayouts.Add(batch.LayoutSlot);
        }
        _graphics.RequestPresentation();
        _timeline?.Event("history-render", new()
        {
            ["layout"] = batch.LayoutSlot,
            ["record"] = batch.FirstRecordIndex,
            ["x"] = batch.Layout.OriginX + batch.Layout.CursorX,
            ["y"] = batch.Layout.OriginY + batch.Layout.CursorY,
            ["text"] = batch.Text,
            ["presented"] = batch.Presented?.Text,
            ["glyphs"] = result.GlyphCount,
            ["presentation"] = "retained-glyphs",
        });
        return true;
    }

    /// <summary>
    /// Op 0x82 (text_redraw_retained_group@0x451530; SYS4433 @0x4553c0): release the layout's objects, clear its surface
    /// and draw each record at its rectangle. Unless flag 4 is set, the published glyphs are bound again over the new
    /// pixels; native binds one object per record instead, which covers the same cells. The glyph list stays, so the
    /// wait indicator keeps its anchor and later text continues the page. This is not a History presentation, which
    /// History's exit would erase.
    /// </summary>
    private void RedrawRetainedTextGroup(
        GfxState gfx,
        AdvTextLayoutPresentationBinding binding,
        AdvTextRedrawBatch batch)
    {
        Tell(listener => listener.GroupRedrawn(binding, batch));
        if (_sourceText == null) return;
        if (binding.LayoutSlot != batch.LayoutSlot
            || binding.FirstObjectHandle < 0
            || binding.ObjectCapacity <= 0)
            return;
        (int width, int height) = _graphics.TryGetSurfaceSize(binding.SourceSurfaceSlot, out var dimensions)
                                  && dimensions.W > 0 && dimensions.H > 0
            ? (dimensions.W, dimensions.H)
            : (batch.Layout.Width, batch.Layout.Height);
        if (width <= 0 || height <= 0) return;

        var updated = new RgbaImage(width, height, new byte[checked(width * height * 4)]);
        TextPipeline text = batch.Presented != null ? _presentedText!.Value : ActiveText;
        IReadOnlyList<(AdvRetainedGlyphRecord Cell, bool IsRuby)> cells = batch.Presented is { } presented
            ? RedrawPresented(updated, binding, batch, presented, text)
            : text.Layout.RenderRedraw(
                updated, batch.Records, text.Policy, _textAntialias, _rubyRules, RealizationFor(text));

        bool bind = (batch.Flags & 4) == 0;
        RetainedAdvTextLayoutPresentation? presentation;
        lock (_textLock)
        {
            // A layout with no live text yet takes the drawn cells as its glyphs.
            if (!_retainedTextLayouts.TryGetValue(binding.LayoutSlot, out presentation) && bind)
            {
                presentation = new RetainedAdvTextLayoutPresentation(binding);
                _retainedTextLayouts.Add(binding.LayoutSlot, presentation);
                presentation.AppendRun(
                    cells.Select(cell => cell.Cell).ToArray(),
                    cells.Select(cell => cell.IsRuby).ToArray(),
                    batch.Layout.OriginX,
                    batch.Layout.OriginY,
                    _rubyRules.ChainEveryRubyRecord);
                RebuildRetainedWaitIndicator(binding.LayoutSlot);
            }
        }

        _graphics.PublishSurfacePixels(binding.SourceSurfaceSlot, updated);
        gfx.CreateSurface(binding.SourceSurfaceSlot);
        gfx.EraseRange(binding.FirstObjectHandle, binding.ObjectCapacity);
        if (bind && presentation != null)
        {
            if (presentation.PublishedGlyphCount > 0) presentation.Republish(gfx);
            else presentation.PublishThrough(gfx, presentation.Glyphs.Count);
        }
        _graphics.RequestPresentation();
        _timeline?.Event("text-redraw", new()
        {
            ["layout"] = batch.LayoutSlot,
            ["record"] = batch.FirstRecordIndex,
            ["flags"] = batch.Flags,
            ["records"] = batch.Records.Count,
            ["cells"] = cells.Count,
        });
    }

    /// <summary>Op 0x82 for a presented group (localization spec §8): the presented text from the first text record's
    /// top-left in its style, wrapped in the layout as live presented text is.</summary>
    private IReadOnlyList<(AdvRetainedGlyphRecord Cell, bool IsRuby)> RedrawPresented(
        RgbaImage destination, AdvTextLayoutPresentationBinding binding, AdvTextRedrawBatch batch,
        Ome.Engine.Localization.PresentedText presented, TextPipeline text)
    {
        AdvTextRedrawRecord first = batch.Records.First(record => !record.IsRuby);
        AdvTextStyle style = PresentedStyle(first.Style);
        PresentedTextPlan plan = PresentedTextLayout.Plan(
            presented.Text, Ome.Engine.Localization.PresentedLineBreaks.Opportunities(presented.Text, presented.LineBreak),
            ImmediateSurfaceTextRenderer.CreateRequests(presented.Text, style, text.Policy, _textAntialias, null),
            text.Cache, first.Rect.Left, binding.ResetCursorX, batch.Layout.Right);
        GlyphTextLayoutResult drawn = text.Layout.Render(
            destination,
            new GlyphTextLayoutOptions(
                first.Rect.Left, first.Rect.Top, binding.ResetCursorX, int.MaxValue, batch.Layout.Bottom,
                WrapHorizontally: false, style, plan.BreaksBefore, OverflowAtFontHeight: true),
            plan.Glyphs);
        return drawn.Records.Select(record => (record, false)).ToArray();
    }

    /// <summary>HISTORY.BIN's recording re-enable at exit (op 0x1bb 1) ends its presentation: every layout op 0x1d1
    /// bound is erased and its surface cleared.</summary>
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
        Tell(listener => listener.HistoryPresentationEnded());
        if (_sourceText == null) return;

        foreach (RetainedAdvTextLayoutPresentation presentation in retained)
            presentation.ErasePublished(gfx);
        foreach (int surfaceSlot in surfaceSlots)
            ClearAllocatedSurfacePixels(surfaceSlot);
        _graphics.RequestPresentation();
        _timeline?.Event("history-presentation-end", new()
        {
            ["retained_layouts"] = retained.Length,
        });
    }

    private void ClearAllocatedSurfacePixels(int surfaceSlot)
    {
        if (!_graphics.TryGetSurfaceSize(surfaceSlot, out var dimensions)
            || dimensions.W <= 0
            || dimensions.H <= 0)
            return;
        _graphics.PublishSurfacePixels(
            surfaceSlot,
            new RgbaImage(dimensions.W, dimensions.H, new byte[checked(dimensions.W * dimensions.H * 4)]),
            updateSize: false);
    }
}
