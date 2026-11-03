using Age.Engine.Model;

namespace Age.Engine.Text;

public readonly record struct GlyphTextLayoutOptions(
    int CursorX,
    int CursorY,
    int LineOriginX,
    int RightBound,
    int BottomBound,
    bool WrapHorizontally,
    AdvTextStyle Style);

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
                "激", options.Style with { FontFace = "ＭＳ ゴシック" }, glyphs[0].Policy)[0])
            : null;

        foreach (GlyphRasterRequest request in glyphs)
        {
            ArgumentNullException.ThrowIfNull(request);
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
                int fontHeight = options.Style.PrimaryFontSize > 0
                    ? options.Style.PrimaryFontSize
                    : request.PixelHeight;
                cursorX = options.LineOriginX;
                cursorY = checked(cursorY + fontHeight + options.Style.LineSpacing);
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

            AgeGlyphMaskCompositor.DrawGlyph(
                destination, mask, cursorX, cursorY,
                AgeGlyphMaskCompositor.CellTopToBaseline(mask, request.PixelHeight), options.Style);
            records.Add(new AdvRetainedGlyphRecord(0, cursorX, cursorY, right, bottom));
            cursorX = checked(cursorX + advanceX);
            if (aspectReference == null) cursorY = checked(cursorY + mask.CellAdvanceY);
        }

        if (records.Count > rowFirst)
            rows.Add(new AdvTextRow(rowFirst, records.Count - rowFirst,
                rowLeft, rowTop, rowRight, rowBottom, WrapsAfter: false));
        return new GlyphTextLayoutResult(
            records, cursorX, cursorY, records.Count,
            wrappedLines, observed, stopped) { Rows = rows };
    }
}
