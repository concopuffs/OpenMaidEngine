using Age.Engine.Model;
using Age.Engine.Sys4;

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
        int cursorX = options.CursorX;
        int cursorY = options.CursorY;
        int wrappedLines = 0;
        var observed = AdvTextOverflowFlags.None;
        bool stopped = false;

        foreach (GlyphRasterRequest request in glyphs)
        {
            ArgumentNullException.ThrowIfNull(request);
            GlyphMask mask = _rasterizer.Rasterize(request);
            int right = checked(cursorX + mask.CellWidth);
            int bottom = checked(cursorY + mask.CellHeight);
            AdvTextOverflowFlags overflow = AdvRetainedTextContract.CheckOverflow(
                options.RightBound, options.BottomBound, right, bottom);
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
                right = checked(cursorX + mask.CellWidth);
                bottom = checked(cursorY + mask.CellHeight);
            }

            AgeGlyphMaskCompositor.DrawGlyph(
                destination, mask, cursorX, cursorY, request.PixelHeight, options.Style);
            records.Add(new AdvRetainedGlyphRecord(0, cursorX, cursorY, right, bottom));
            cursorX = checked(cursorX + mask.CellAdvanceX);
            cursorY = checked(cursorY + mask.CellAdvanceY);
        }

        return new GlyphTextLayoutResult(
            records, cursorX, cursorY, records.Count, wrappedLines, observed, stopped);
    }
}
