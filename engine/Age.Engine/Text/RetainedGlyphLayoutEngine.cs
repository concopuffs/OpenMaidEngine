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
    IReadOnlyList<AdvRetainedGlyphPresentationRect> PresentationRects,
    int CursorX,
    int CursorY,
    int ConsumedGlyphs,
    int WrappedLines,
    AdvTextOverflowFlags ObservedOverflow,
    bool StoppedOnVerticalOverflow);

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
        var presentationRects =
            new List<AdvRetainedGlyphPresentationRect>(glyphs.Count);
        int cursorX = options.CursorX;
        int cursorY = options.CursorY;
        int wrappedLines = 0;
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
            }

            AgeGlyphMaskCompositor.DrawGlyph(
                destination, mask, cursorX, cursorY, request.PixelHeight, options.Style);
            records.Add(new AdvRetainedGlyphRecord(0, cursorX, cursorY, right, bottom));
            presentationRects.Add(PresentationRect(
                destination.Height,
                mask,
                cursorX,
                cursorY,
                right,
                bottom,
                request.PixelHeight,
                options.Style));
            cursorX = checked(cursorX + advanceX);
            if (aspectReference == null) cursorY = checked(cursorY + mask.CellAdvanceY);
        }

        return new GlyphTextLayoutResult(
            records, presentationRects, cursorX, cursorY, records.Count,
            wrappedLines, observed, stopped);
    }

    private static AdvRetainedGlyphPresentationRect PresentationRect(
        int surfaceHeight,
        GlyphMask mask,
        int cellLeft,
        int cellTop,
        int cellRight,
        int cellBottom,
        int topToBaseline,
        AdvTextStyle style)
    {
        int minimumEffectY = 0;
        int maximumEffectY = 0;
        if (style.RenderMode == 1)
        {
            minimumEffectY = Math.Min(0, style.EffectOffsetY);
            maximumEffectY = Math.Max(0, style.EffectOffsetY);
        }
        else if (style.RenderMode == 3)
        {
            foreach ((int _, int y) in AgeGlyphMaskCompositor.GetMode3OutlineOffsets(
                         style.EffectOffsetX, style.EffectOffsetY))
            {
                minimumEffectY = Math.Min(minimumEffectY, y);
                maximumEffectY = Math.Max(maximumEffectY, y);
            }
        }

        int inkTop = checked(
            cellTop + topToBaseline - mask.OriginY + minimumEffectY);
        int inkBottom = checked(
            cellTop + topToBaseline - mask.OriginY + mask.Height + maximumEffectY);
        return new AdvRetainedGlyphPresentationRect(
            cellLeft,
            Math.Clamp(Math.Min(cellTop, inkTop), 0, surfaceHeight),
            cellRight,
            Math.Clamp(Math.Max(cellBottom, inkBottom), 0, surfaceHeight));
    }
}
