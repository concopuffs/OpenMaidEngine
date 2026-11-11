using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;

namespace Ome.Engine.Text;

/// <summary>Where a text's ink runs, in pixels: from the left of its first glyph that is not a space to the right of
/// the cell of its last one (localization spec §7.2).</summary>
public readonly record struct TextExtent(int Left, int Right)
{
    public int Width => Right - Left;
}

/// <summary>An ADV run's presented text placed for its entry (spec §7.2): the glyphs to draw, narrowed by
/// <see cref="WidthPercent"/> for <c>fit = "shrink"</c>, their rows, and each row's shift for <c>align</c> (null when
/// no row moves).</summary>
public sealed record PresentedRunPlacement(PresentedTextPlan Plan, int WidthPercent, IReadOnlyList<int>? RowShiftsX);

/// <summary>
/// Alignment and fit of presented text against its source text's extent (localization spec §7.2), and the rows an
/// overflowing run cuts (§7.3). The source extent is what the source text would have covered: the source layout pass
/// of an ADV run (§8), or the source string measured for a <c>draw-string</c>.
/// <list type="bullet">
/// <item><c>fit = "shrink"</c> narrows every glyph of the text by one factor so that it takes no more room than the
/// source text, but never below the language's <c>min_fit_scale</c>. Source text on one row gives the width: the
/// widest line (lines end at <c>\n</c>) is made no wider than the source extent. An ADV run whose source took several
/// rows gives the rows: the text is narrowed only as far as it needs to wrap into no more rows than the source. Text
/// that still does not fit wraps as it would have.</item>
/// <item><c>align = "center"</c> or <c>"end"</c> moves each row so its centre or its end is the source extent's. A row
/// stays inside its bounds, the left one winning when it cannot keep both: in an ADV layout, its line origin (or where
/// the run starts, when that is further left) and its right bound; for a <c>draw-string</c>, its surface.</item>
/// </list>
/// Spaces at either end of a line count for neither extent.
/// </summary>
public static class PresentedTextPlacement
{
    public static bool IsSpace(int scalar) => scalar is ' ' or '\t' or 0x3000;

    /// <summary>The extent of the glyphs <paramref name="records"/> laid out for <paramref name="text"/>, one record
    /// per code point (an ADV run's source layout), over every row; null when every glyph is a space.</summary>
    public static TextExtent? Extent(string text, IReadOnlyList<AdvRetainedGlyphRecord> records)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(records);
        int left = int.MaxValue, right = int.MinValue, index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (index >= records.Count) break;
            AdvRetainedGlyphRecord record = records[index++];
            if (IsSpace(rune.Value) || rune.Value < 0x20) continue;
            left = System.Math.Min(left, record.Left);
            right = System.Math.Max(right, record.Right);
        }
        return left <= right ? new TextExtent(left, right) : null;
    }

    /// <summary>The extent of <paramref name="glyphs"/> drawn in one row from <paramref name="x"/>, each advancing the
    /// pen by its cell (presented text draws without aspect mode); null when every glyph is a space or a control
    /// character.</summary>
    public static TextExtent? Extent(IEnumerable<GlyphRasterRequest> glyphs, IGlyphMaskRasterizer rasterizer, int x)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(rasterizer);
        int? left = null;
        int right = x, pen = x;
        foreach (GlyphRasterRequest glyph in glyphs)
        {
            // A control character draws nothing (PresentedTextLayout drops it).
            if (glyph.UnicodeScalar < 0x20) continue;
            GlyphMask mask = rasterizer.Rasterize(glyph);
            if (!IsSpace(glyph.UnicodeScalar))
            {
                left ??= pen;
                right = checked(pen + mask.CellWidth);
            }
            pen = checked(pen + mask.CellAdvanceX);
        }
        return left is { } start ? new TextExtent(start, right) : null;
    }

    /// <summary>The narrowing <c>fit = "shrink"</c> gives <paramref name="lines"/> against a source extent
    /// <paramref name="width"/> pixels wide, in percent: 100 when the widest line fits, else the widest narrowing that
    /// makes it fit, but no narrower than <paramref name="minScale"/>.</summary>
    public static int FitPercent(IReadOnlyList<IReadOnlyList<GlyphRasterRequest>> lines, IGlyphMaskRasterizer rasterizer,
                                 int width, double minScale)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int Widest(int percent) => lines
            .Select(line => Extent(line.Select(glyph => glyph with { WidthPercent = percent }), rasterizer, 0)?.Width ?? 0)
            .DefaultIfEmpty(0).Max();
        int natural = Widest(100);
        if (natural <= width || width <= 0) return 100;
        int floor = FloorPercent(minScale);
        int percent = System.Math.Clamp((int)System.Math.Floor(100.0 * width / natural), floor, 100);
        while (percent > floor && Widest(percent) > width) percent--;
        return percent;
    }

    /// <summary>How far <paramref name="row"/> moves for <paramref name="align"/> against <paramref name="source"/>,
    /// kept between <paramref name="minLeft"/> and <paramref name="maxRight"/>.</summary>
    public static int Shift(TextAlign align, TextExtent source, TextExtent row, int minLeft, int maxRight)
    {
        int shift = align switch
        {
            TextAlign.Center => (int)System.Math.Floor((source.Left + source.Right - row.Left - row.Right) / 2.0),
            TextAlign.End => source.Right - row.Right,
            _ => 0,
        };
        if (row.Right + shift > maxRight) shift = maxRight - row.Right;
        if (row.Left + shift < minLeft) shift = minLeft - row.Left;
        return shift;
    }

    /// <summary>
    /// Places an ADV run's presented text (spec §7.1, §7.2): narrowed for <c>fit</c>, broken into rows as
    /// <see cref="PresentedTextLayout.Plan"/> breaks it, and each row shifted for <c>align</c>. Without a source extent
    /// the text is placed as written.
    /// </summary>
    /// <param name="requests">One request per code point of <paramref name="text"/>.</param>
    /// <param name="sourceRows">The rows the source text took: one fits by width, more by rows.</param>
    public static PresentedRunPlacement Place(
        string text, bool[] opportunities, IReadOnlyList<GlyphRasterRequest> requests, IGlyphMaskRasterizer rasterizer,
        int cursorX, int lineOriginX, int rightBound, TextAlign align, TextFit fit, TextExtent? source, int sourceRows,
        double minFitScale)
    {
        ArgumentNullException.ThrowIfNull(requests);
        PresentedTextPlan PlanAt(int percent) => PresentedTextLayout.Plan(
            text, opportunities, percent == 100 ? requests : requests.Select(glyph => glyph with { WidthPercent = percent }).ToArray(),
            rasterizer, cursorX, lineOriginX, rightBound);
        int percent = 100;
        PresentedTextPlan plan = PlanAt(100);
        if (fit == TextFit.Shrink && source is { } fitted)
        {
            if (sourceRows <= 1)
            {
                var lines = new List<IReadOnlyList<GlyphRasterRequest>> { new List<GlyphRasterRequest>() };
                foreach (GlyphRasterRequest glyph in requests)
                    if (glyph.UnicodeScalar == '\n') lines.Add(new List<GlyphRasterRequest>());
                    else ((List<GlyphRasterRequest>)lines[^1]).Add(glyph);
                percent = FitPercent(lines, rasterizer, fitted.Width, minFitScale);
            }
            else
            {
                int floor = FloorPercent(minFitScale);
                while (percent > floor && RowCount(PlanAt(percent)) > sourceRows) percent--;
            }
            if (percent < 100) plan = PlanAt(percent);
        }
        if (align == TextAlign.Start || source is not { } target) return new PresentedRunPlacement(plan, percent, null);
        var shifts = new int[plan.BreaksBefore.Count + 1];
        for (int row = 0; row < shifts.Length; row++)
        {
            (int first, int end) = Row(plan, row);
            int start = row == 0 ? cursorX : lineOriginX;
            if (Extent(plan.Glyphs.Skip(first).Take(end - first), rasterizer, start) is { } extent)
                shifts[row] = Shift(align, target, extent, System.Math.Min(start, lineOriginX), rightBound);
        }
        return new PresentedRunPlacement(plan, percent, shifts.Any(shift => shift != 0) ? shifts : null);
    }

    /// <summary>The narrowest <paramref name="minScale"/> allows, in whole percent.</summary>
    private static int FloorPercent(double minScale) => System.Math.Clamp((int)System.Math.Ceiling(minScale * 100 - 1e-9), 1, 100);

    /// <summary>The rows <paramref name="plan"/> takes, blank rows between included: a break after its last glyph only
    /// moves the cursor.</summary>
    public static int RowCount(PresentedTextPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return 1 + plan.BreaksBefore.Count(index => index < plan.Glyphs.Count);
    }

    /// <summary>How many rows of <paramref name="plan"/> that hold glyphs a run cut when it drew only the first
    /// <paramref name="drawnGlyphs"/> of them (spec §7.3): the row it stopped in and every later one.</summary>
    public static int CutRows(PresentedTextPlan plan, int drawnGlyphs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (drawnGlyphs >= plan.Glyphs.Count) return 0;
        int cut = 0;
        for (int row = 0; row <= plan.BreaksBefore.Count; row++)
        {
            (int first, int end) = Row(plan, row);
            if (end > first && end > drawnGlyphs) cut++;
        }
        return cut;
    }

    /// <summary>Row <paramref name="row"/>'s glyphs: [first, end) in <see cref="PresentedTextPlan.Glyphs"/>.</summary>
    private static (int First, int End) Row(PresentedTextPlan plan, int row)
        => (row == 0 ? 0 : plan.BreaksBefore[row - 1],
            row < plan.BreaksBefore.Count ? plan.BreaksBefore[row] : plan.Glyphs.Count);
}
