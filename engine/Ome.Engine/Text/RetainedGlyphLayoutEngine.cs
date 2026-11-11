using Ome.Engine.Model;

namespace Ome.Engine.Text;

/// <param name="LineBreaksBefore">Glyph indices, ascending (repeats allowed), before which the run starts a new
/// row: x returns to <paramref name="LineOriginX"/> and y advances by the font height plus line spacing. Native
/// History (text_history_render_records@0x4526c0) breaks only at retained line-break records this way.</param>
/// <param name="FontRealization">The installed-font list behind aspect mode's glyph placement
/// (<see cref="AspectGlyphPlacement"/>). Without it, aspect-mode glyphs draw at the pen.</param>
/// <param name="OverflowAtFontHeight">Test the bottom bound against the font's height, the box rows advance by,
/// instead of the glyph cell. Presented text sets it (localization spec §7): its Unicode font's cell holds the full
/// ascent and descent, often taller than the source font's, and a layout sized for the source font (Kamidori's
/// 25-pixel name plate) would otherwise stop at its first glyph.</param>
public readonly record struct GlyphTextLayoutOptions(
    int CursorX,
    int CursorY,
    int LineOriginX,
    int RightBound,
    int BottomBound,
    bool WrapHorizontally,
    AdvTextStyle Style,
    IReadOnlyList<int>? LineBreaksBefore = null,
    AspectFontRealization? FontRealization = null,
    bool OverflowAtFontHeight = false);

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
    private readonly GlyphCoverageAlpha _coverageAlpha;

    public RetainedGlyphLayoutEngine(
        IGlyphMaskRasterizer rasterizer, GlyphCoverageAlpha coverageAlpha = GlyphCoverageAlpha.Sixteenths)
    {
        _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
        _coverageAlpha = coverageAlpha;
    }

    /// <summary>Lay out <paramref name="glyphs"/> and draw them into <paramref name="destination"/>; a null destination
    /// only measures, as the source layout pass of presented text does (localization spec §8).</summary>
    public GlyphTextLayoutResult Render(
        RgbaImage? destination,
        GlyphTextLayoutOptions options,
        IReadOnlyList<GlyphRasterRequest> glyphs)
    {
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
        GlyphRasterRequest? referenceRequest = options.Style.AspectMode != 0 && glyphs.Count > 0
            ? ImmediateSurfaceTextRenderer.CreateRequests(
                "激", options.Style with { FontFace = AspectFontRealization.ReferenceFace }, glyphs[0].Policy,
                glyphs[0].Antialias)[0]
            : null;
        GlyphMask? aspectReference = referenceRequest != null ? _rasterizer.Rasterize(referenceRequest) : null;
        // The realized font's 激, whose ink top the aspect-mode raise compares (+0x31430).
        GlyphMask? realizedReference = aspectReference != null
            ? _rasterizer.Rasterize(new GlyphRasterRequest(
                glyphs[0].FontFace, glyphs[0].PixelHeight, glyphs[0].RequestedWidth, glyphs[0].Weight,
                '激', AspectFontRealization.ReferenceCode, glyphs[0].Policy, glyphs[0].Antialias))
            : null;
        bool placesInCell = aspectReference != null
                            && options.FontRealization?.PlacesGlyphsInCell(glyphs[0].FontFace) == true;
        // An opening bracket ends at the reference's GGO_METRICS black box (+0x313d8), not the raster's.
        int referenceBlackBoxWidth = placesInCell
            ? options.FontRealization!.ReferenceBlackBoxWidth(referenceRequest!.PixelHeight, referenceRequest.Weight)
            : 0;
        int fontSize = options.Style.PrimaryFontSize > 0 ? options.Style.PrimaryFontSize
            : glyphs.Count > 0 ? glyphs[0].PixelHeight : 0;

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
            int overflowBottom = aspectReference != null ? checked(cursorY + aspectReference.CellAdvanceY)
                : options.OverflowAtFontHeight ? checked(cursorY + request.PixelHeight)
                : bottom;
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
            int raise = aspectReference != null
                ? AspectGlyphRaise(mask, aspectReference, realizedReference!, request.PixelHeight)
                : 0;
            int drawX = placesInCell
                ? checked(cursorX + AspectGlyphPlacement.OffsetX(
                    mask, referenceBlackBoxWidth, fontSize, request.Cp932Code, options.Style.AspectMode))
                : cursorX;
            if (destination != null)
                AgeGlyphMaskCompositor.DrawGlyph(
                    destination, mask,
                    shiftByEffect ? checked(drawX + options.Style.EffectOffsetX) : drawX,
                    checked((shiftByEffect ? cursorY + options.Style.EffectOffsetY : cursorY) - raise),
                    AgeGlyphMaskCompositor.CellTopToBaseline(mask, request.PixelHeight), options.Style, _coverageAlpha);
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
    /// How far SYS4433's aspect mode draws a glyph above its cell (adv_text_append_horizontal_glyph_records@0x45b710
    /// at 0x45be16, and History's renderer @0x456490): with <c>d = trunc(tmAscent - ref.tmAscent - 0.5)</c> against the
    /// reference ＭＳ ゴシック and <c>e = trunc(tmAscent - 激.gmptGlyphOrigin.y - 1)</c> for the realized font's 激, the
    /// raise is 0 when d is 0 and the larger of the two otherwise. A face as tall as the reference is not raised;
    /// Kamidori's メイリオ bold at 22 pixels is raised 3. The record keeps the cell.
    /// </summary>
    public static int AspectGlyphRaise(GlyphMask glyph, GlyphMask reference, GlyphMask realizedReference, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(realizedReference);
        int ascent = AgeGlyphMaskCompositor.CellTopToBaseline(glyph, pixelHeight);
        int d = (int)(ascent - AgeGlyphMaskCompositor.CellTopToBaseline(reference, pixelHeight) - 0.5);
        int e = (int)(ascent - realizedReference.OriginY - 1.0);
        return d == 0 ? 0 : Math.Max(d, e);
    }

    /// <summary>
    /// Draw op 0x196's ruby over the rows <see cref="Render"/> drew for its base text, each row taking its share
    /// (<see cref="RubyTextLayout.SplitAcrossRows"/>), as adv_text_build_glyph_records@0x4576c0 does after each row.
    /// The cursor is not moved: AGE restores it to the end of the base text.
    /// </summary>
    public IReadOnlyList<AdvRubyGlyph> RenderRuby(
        RgbaImage? destination,
        string baseText,
        IReadOnlyList<AdvTextRow> rows,
        string ruby,
        AdvTextStyle style,
        GlyphRasterPolicy policy,
        bool antialias,
        RubyLayoutRules rules)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var glyphs = new List<AdvRubyGlyph>();
        IReadOnlyList<string> shares = RubyTextLayout.SplitAcrossRows(baseText, rows, ruby);
        for (int index = 0; index < rows.Count; index++)
        {
            AdvTextRow row = rows[index];
            IReadOnlyList<AdvRetainedGlyphRecord> records = RenderRubyOver(
                destination, row.Left, row.Top, row.Right - row.Left, shares[index], style, policy, antialias, rules);
            int at = 0;
            foreach (System.Text.Rune rune in shares[index].EnumerateRunes())
            {
                if (at >= records.Count) break;
                glyphs.Add(new AdvRubyGlyph(index, rune.ToString(), records[at++]));
            }
        }
        return glyphs;
    }

    /// <summary>Draw a ruby over one base (<see cref="RubyTextLayout.Draw"/>) with this engine's rasterizer;
    /// <paramref name="history"/> selects History's top offset.</summary>
    public IReadOnlyList<AdvRetainedGlyphRecord> RenderRubyOver(
        RgbaImage? destination,
        int baseLeft,
        int baseTop,
        int baseWidth,
        string ruby,
        AdvTextStyle style,
        GlyphRasterPolicy policy,
        bool antialias,
        RubyLayoutRules rules,
        bool history = false)
        => RubyTextLayout.Draw(
            destination, _rasterizer, baseLeft, baseTop, baseWidth, ruby, style, policy, antialias, rules, history,
            _coverageAlpha);

    /// <summary>Draw ruby characters from the cell at (<paramref name="x"/>, <paramref name="y"/>)
    /// (<see cref="RubyTextLayout.DrawAt"/>) with this engine's rasterizer.</summary>
    public IReadOnlyList<AdvRetainedGlyphRecord> RenderRubyAt(
        RgbaImage destination,
        int x,
        int y,
        string ruby,
        AdvTextStyle style,
        GlyphRasterPolicy policy,
        bool antialias,
        RubyLayoutRules rules)
        => RubyTextLayout.DrawAt(destination, _rasterizer, x, y, ruby, style, policy, antialias, rules, _coverageAlpha);

    /// <summary>
    /// Draw op 0x82's records into <paramref name="destination"/> (text_redraw_retained_group@0x451530; SYS4433
    /// @0x4553c0): each text record's text from the cell at its rectangle's top-left, on one row, and each ruby record
    /// with the ruby font. Returns the drawn cells in record order, each marked as ruby or not.
    /// </summary>
    public IReadOnlyList<(AdvRetainedGlyphRecord Cell, bool IsRuby)> RenderRedraw(
        RgbaImage destination,
        IReadOnlyList<AdvTextRedrawRecord> records,
        GlyphRasterPolicy policy,
        bool antialias,
        RubyLayoutRules rules,
        AspectFontRealization? fontRealization = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        var cells = new List<(AdvRetainedGlyphRecord, bool)>();
        foreach (AdvTextRedrawRecord record in records)
        {
            if (record.IsRuby)
            {
                foreach (AdvRetainedGlyphRecord cell in RenderRubyAt(
                             destination, record.Rect.Left, record.Rect.Top, record.Text, record.Style, policy, antialias,
                             rules))
                    cells.Add((cell, true));
                continue;
            }
            GlyphTextLayoutResult drawn = Render(
                destination,
                new GlyphTextLayoutOptions(
                    record.Rect.Left, record.Rect.Top, record.Rect.Left, int.MaxValue, int.MaxValue,
                    WrapHorizontally: false, record.Style, FontRealization: fontRealization),
                ImmediateSurfaceTextRenderer.CreateRequests(
                    record.Text, record.Style, policy, antialias, fontRealization));
            foreach (AdvRetainedGlyphRecord cell in drawn.Records) cells.Add((cell, false));
        }
        return cells;
    }
}
