using Age.Engine.Model;

namespace Age.Engine.Text;

/// <param name="LineBreaksBefore">Glyph indices, ascending (repeats allowed), before which the run starts a new
/// row: x returns to <paramref name="LineOriginX"/> and y advances by the font height plus line spacing. Native
/// History (text_history_render_records@0x4526c0) breaks only at retained line-break records this way.</param>
public readonly record struct GlyphTextLayoutOptions(
    int CursorX,
    int CursorY,
    int LineOriginX,
    int RightBound,
    int BottomBound,
    bool WrapHorizontally,
    AdvTextStyle Style,
    IReadOnlyList<int>? LineBreaksBefore = null);

public sealed record GlyphTextLayoutResult(
    IReadOnlyList<AdvRetainedGlyphRecord> Records,
    int CursorX,
    int CursorY,
    int ConsumedGlyphs,
    int WrappedLines,
    AdvTextOverflowFlags ObservedOverflow,
    bool StoppedOnVerticalOverflow)
{
    /// <summary>The drawn rows, split at horizontal wraps, as AGE's history records them.</summary>
    public IReadOnlyList<AdvTextRow> Rows { get; init; } = [];
}

/// <summary>
/// Builds native-edge retained records while rasterizing masks into the layout's RGBA surface.
/// This is deliberately presentation-neutral: binding records to GfxState belongs to a later slice.
/// </summary>
public sealed class RetainedGlyphLayoutEngine
{
    private readonly IGlyphMaskRasterizer _rasterizer;

    public RetainedGlyphLayoutEngine(IGlyphMaskRasterizer rasterizer)
        => _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));

    public GlyphTextLayoutResult Render(
        RgbaImage destination,
        GlyphTextLayoutOptions options,
        IReadOnlyList<GlyphRasterRequest> glyphs)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(glyphs);
        var records = new List<AdvRetainedGlyphRecord>(glyphs.Count);
        int cursorX = options.CursorX;
        int cursorY = options.CursorY;
        int wrappedLines = 0;
        var rows = new List<AdvTextRow>();
        int rowFirst = 0, rowLeft = 0, rowTop = 0, rowRight = 0, rowBottom = 0;
        var observed = AdvTextOverflowFlags.None;
        bool stopped = false;

        // SYS4433 0x2db: layout uses the reference MS Gothic glyph's advance,
        // independently of the selected face's ink/text extent (native 0x45b710).
        GlyphMask? aspectReference = options.Style.AspectMode != 0 && glyphs.Count > 0
            ? _rasterizer.Rasterize(ImmediateSurfaceTextRenderer.CreateRequests(
                "激", options.Style with { FontFace = "ＭＳ ゴシック" }, glyphs[0].Policy, glyphs[0].Antialias)[0])
            : null;

        IReadOnlyList<int> breaks = options.LineBreaksBefore ?? [];
        int nextBreak = 0;
        int LineAdvance(int pixelHeight)
            => (options.Style.PrimaryFontSize > 0 ? options.Style.PrimaryFontSize : pixelHeight)
               + options.Style.LineSpacing;
        void BreakLine(int pixelHeight)
        {
            if (records.Count > rowFirst)
                rows.Add(new AdvTextRow(rowFirst, records.Count - rowFirst,
                    rowLeft, rowTop, rowRight, rowBottom, WrapsAfter: true));
            rowFirst = records.Count;
            cursorX = options.LineOriginX;
            cursorY = checked(cursorY + LineAdvance(pixelHeight));
        }

        for (int index = 0; index < glyphs.Count; index++)
        {
            GlyphRasterRequest request = glyphs[index];
            ArgumentNullException.ThrowIfNull(request);
            for (; nextBreak < breaks.Count && breaks[nextBreak] <= index; nextBreak++)
                BreakLine(request.PixelHeight);
            GlyphMask mask = _rasterizer.Rasterize(request);
            int advanceX = aspectReference == null ? mask.CellAdvanceX
                : aspectReference.CellAdvanceX / (request.Cp932Code is <= 0xff ? 2 : 1);
            int cellWidth = aspectReference == null ? mask.CellWidth : advanceX;
            int cellHeight = aspectReference == null ? mask.CellHeight
                : (options.Style.PrimaryFontSize > 0 ? options.Style.PrimaryFontSize : request.PixelHeight)
                  + (options.Style.RenderMode != 0 ? options.Style.EffectOffsetY : 0);
            int right = checked(cursorX + cellWidth);
            int bottom = checked(cursorY + cellHeight);
            int overflowBottom = aspectReference == null ? bottom
                : checked(cursorY + aspectReference.CellAdvanceY);
            AdvTextOverflowFlags overflow = AdvRetainedTextContract.CheckOverflow(
                options.RightBound, options.BottomBound, right, overflowBottom);
            observed |= overflow;

            if (overflow == AdvTextOverflowFlags.Vertical)
            {
                stopped = true;
                break;
            }

            if (overflow.HasFlag(AdvTextOverflowFlags.Horizontal)
                && options.WrapHorizontally
                && (request.Cp932Code == null
                    || !AdvRetainedTextContract.PreventsHorizontalWrapBefore(
                        request.Cp932Code.Value)))
            {
                cursorX = options.LineOriginX;
                cursorY = checked(cursorY + LineAdvance(request.PixelHeight));
                wrappedLines++;
                right = checked(cursorX + cellWidth);
                bottom = checked(cursorY + cellHeight);
                if (records.Count > rowFirst)
                    rows.Add(new AdvTextRow(rowFirst, records.Count - rowFirst,
                        rowLeft, rowTop, rowRight, rowBottom, WrapsAfter: true));
                rowFirst = records.Count;
            }
            if (records.Count == rowFirst)
            {
                rowLeft = cursorX;
                rowTop = cursorY;
            }
            rowRight = right;
            rowBottom = bottom;

            // With AntiFont off, AGE's ADV builders, retained redraw and History draw each glyph through
            // text_draw_string_primary_or_ruby@0x44fac0, which starts an effect string (mode 1-3) at the effect
            // offset (+0x564/+0x568) before text_raster_string_cached@0x45b600 adds its own. The ink moves; the record
            // keeps the measured cell (Himegari's mode-3 (1,1) text lands one pixel right and down).
            bool shiftByEffect = !request.Antialias && options.Style.RenderMode != 0;
            AgeGlyphMaskCompositor.DrawGlyph(
                destination, mask,
                shiftByEffect ? checked(cursorX + options.Style.EffectOffsetX) : cursorX,
                shiftByEffect ? checked(cursorY + options.Style.EffectOffsetY) : cursorY,
                AgeGlyphMaskCompositor.CellTopToBaseline(mask, request.PixelHeight), options.Style);
            records.Add(new AdvRetainedGlyphRecord(0, cursorX, cursorY, right, bottom));
            cursorX = checked(cursorX + advanceX);
            if (aspectReference == null) cursorY = checked(cursorY + mask.CellAdvanceY);
        }

        // Breaks after the last glyph move only the cursor.
        if (!stopped)
            for (; nextBreak < breaks.Count; nextBreak++)
                BreakLine(glyphs.Count > 0 ? glyphs[^1].PixelHeight : 24);
        if (records.Count > rowFirst)
            rows.Add(new AdvTextRow(rowFirst, records.Count - rowFirst,
                rowLeft, rowTop, rowRight, rowBottom, WrapsAfter: false));
        return new GlyphTextLayoutResult(
            records, cursorX, cursorY, records.Count,
            wrappedLines, observed, stopped) { Rows = rows };
    }

    /// <summary>
    /// Draw op 0x196's ruby over the rows <see cref="Render"/> drew for its base text, each row taking its share
    /// (<see cref="RubyTextLayout.SplitAcrossRows"/>), as adv_text_build_glyph_records@0x4576c0 does after each row.
    /// The cursor is not moved: AGE restores it to the end of the base text.
    /// </summary>
    public IReadOnlyList<AdvRubyGlyph> RenderRuby(
        RgbaImage destination,
        string baseText,
        IReadOnlyList<AdvTextRow> rows,
        string ruby,
        AdvTextStyle style,
        GlyphRasterPolicy policy,
        bool antialias)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var glyphs = new List<AdvRubyGlyph>();
        IReadOnlyList<string> shares = RubyTextLayout.SplitAcrossRows(baseText, rows, ruby);
        for (int index = 0; index < rows.Count; index++)
        {
            AdvTextRow row = rows[index];
            IReadOnlyList<AdvRetainedGlyphRecord> records = RenderRubyOver(
                destination, row.Left, row.Top, row.Right - row.Left, shares[index], style, policy, antialias);
            int at = 0;
            foreach (System.Text.Rune rune in shares[index].EnumerateRunes())
            {
                if (at >= records.Count) break;
                glyphs.Add(new AdvRubyGlyph(index, rune.ToString(), records[at++]));
            }
        }
        return glyphs;
    }

    /// <summary>Draw a ruby over one base (<see cref="RubyTextLayout.Draw"/>) with this engine's rasterizer.</summary>
    public IReadOnlyList<AdvRetainedGlyphRecord> RenderRubyOver(
        RgbaImage destination,
        int baseLeft,
        int baseTop,
        int baseWidth,
        string ruby,
        AdvTextStyle style,
        GlyphRasterPolicy policy,
        bool antialias)
        => RubyTextLayout.Draw(
            destination, _rasterizer, baseLeft, baseTop, baseWidth, ruby, style, policy, antialias);
}
