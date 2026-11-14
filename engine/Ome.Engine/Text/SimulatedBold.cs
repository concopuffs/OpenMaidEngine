namespace Ome.Engine.Text;

/// <summary>
/// GDI's simulated bold: what <c>GetGlyphOutline</c> draws when a face with no bold style (ＭＳ ゴシック, ＭＳ 明朝, their
/// P faces) is asked for <c>lfWeight</c> 700. The glyph at regular weight has a copy of itself one column to its right
/// added in, the coverage saturating at full; an inked glyph's black box grows by one column at the same origin, and
/// every glyph's advance and cell grow by one pixel (a blank glyph stays blank). Measured against GGO_GRAY4_BITMAP's
/// regular glyph, this is every glyph GDI draws in those faces from 12 to 50 pixels, at any width; from 52 pixels GDI
/// emboldens the outline instead (a black box one or two columns wider, the advance still one pixel), which this
/// approximates. A rasterizer that draws such a face regular applies it, as GDI would.
/// </summary>
public static class SimulatedBold
{
    public static GlyphMask Apply(GlyphMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ReadOnlySpan<byte> source = mask.Coverage.Span;
        bool inked = false;
        for (int y = 0; y < mask.Height && !inked; y++)
            inked = source.Slice(y * mask.Stride, mask.Width).IndexOfAnyExcept((byte)0) >= 0;
        int width = inked ? mask.Width + 1 : mask.Width;
        var coverage = new byte[width * mask.Height];
        for (int y = 0; y < mask.Height; y++)
        for (int x = 0; x < width; x++)
        {
            int own = x < mask.Width ? source[y * mask.Stride + x] : 0;
            int left = inked && x > 0 ? source[y * mask.Stride + x - 1] : 0;
            coverage[y * width + x] = (byte)Math.Min(16, own + left);
        }
        return new GlyphMask(width, mask.Height, width, mask.OriginX, mask.OriginY, mask.CellAdvanceX + 1,
                             mask.CellAdvanceY, mask.CellWidth + 1, mask.CellHeight, coverage, mask.Ascent,
                             mask.Monochrome);
    }
}
