namespace Ome.Engine.Text;

/// <summary>
/// What GDI draws and measures for a glyph request, made from the face's glyph drawn at its own width, for a
/// rasterizer that cannot ask GDI (the text server rasterizer). With the face's file known
/// (<see cref="OpenTypeFace"/>) every rule is GDI's (<see cref="GdiFontRules"/>):
/// <list type="bullet">
/// <item>the request's width scales the glyph by <see cref="GdiFontRules.HorizontalScale"/>;</item>
/// <item>a monospaced face advances by whole average widths (<see cref="GdiFontRules.MonospacedAdvance"/>);</item>
/// <item>the cell's height and baseline are <c>tmHeight</c> and <c>tmAscent</c>;</item>
/// <item>a bold request that the face answers at a regular weight is drawn with <see cref="SimulatedBold"/>;</item>
/// <item>the probe reports <c>tmAveCharWidth</c>, <c>tmHeight</c>, and the embedded bitmap's black box where GDI
/// measures one.</item>
/// </list>
/// Without the file (a face the system's fallback supplies), the width scales against <c>height / 2</c> and the
/// glyph keeps its rasterizer's metrics. A substitute for a face the system lacks (Noto Sans Mono CJK JP for
/// ＭＳ ゴシック on Linux) is realized that way too, not by its own file's rules: it stands in for the missing face,
/// whose room the scripts count. Native never meets this case, since its faces are installed.
/// </summary>
public static class GdiGlyphRealization
{
    /// <summary>The glyph GDI draws for <paramref name="request"/>, from <paramref name="natural"/>, the face's glyph
    /// at its own width (an inked box, as GDI's masks are).</summary>
    public static GlyphMask Draw(GlyphMask natural, OpenTypeFace? face, GlyphRasterRequest request, bool simulatedBold)
    {
        ArgumentNullException.ThrowIfNull(natural);
        ArgumentNullException.ThrowIfNull(request);
        int height = request.PixelHeight, width = request.RequestedWidth;
        double scale = face != null ? GdiFontRules.HorizontalScale(face, height, width) : FallbackScale(height, width);
        GlyphMask sized = scale == 1 ? natural : GlyphMaskNarrowing.Resize(natural, scale);
        if (face != null)
        {
            GdiTextMetrics metrics = GdiFontRules.TextMetrics(face, height, width, simulatedBold: false);
            int advance = GdiFontRules.MonospacedAdvance(face, height, width, natural.CellAdvanceX) ?? sized.CellAdvanceX;
            sized = new GlyphMask(sized.Width, sized.Height, sized.Stride, sized.OriginX, sized.OriginY, advance,
                                  sized.CellAdvanceY, Math.Max(sized.Width, advance), metrics.Height,
                                  sized.Coverage.Span, metrics.Ascent, sized.Monochrome);
        }
        return simulatedBold ? SimulatedBold.Apply(sized) : sized;
    }

    /// <summary>What GDI's <c>GetTextMetrics</c> and <c>GetGlyphOutline(GGO_METRICS)</c> report for
    /// <paramref name="request"/> (<see cref="IFontMetricsProbe"/>), given <paramref name="drawn"/>, the glyph
    /// <see cref="Draw"/> made for it, and <paramref name="glyph"/>, its index in <paramref name="face"/>.</summary>
    public static FontProbeMetrics Measure(GlyphMask drawn, OpenTypeFace? face, GlyphRasterRequest request,
                                           bool simulatedBold, int glyph)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(request);
        int height = request.PixelHeight, width = request.RequestedWidth;
        uint blackBox = InkedColumns(drawn);
        if (face == null)
            return new FontProbeMetrics(width != 0 ? Math.Abs(width) : height / 2, blackBox,
                                        (short)drawn.CellAdvanceX, drawn.CellHeight);
        GdiTextMetrics metrics = GdiFontRules.TextMetrics(face, height, width, simulatedBold);
        if (GdiFontRules.MeasuredBitmap(face, glyph, height, width) is { } bitmap)
            blackBox = (uint)(bitmap.InkWidth + (simulatedBold && bitmap.InkWidth > 0 ? 1 : 0));
        return new FontProbeMetrics(metrics.AverageCharWidth, blackBox, (short)drawn.CellAdvanceX, metrics.Height);
    }

    /// <summary>A face whose file is unknown is taken to average half its height.</summary>
    private static double FallbackScale(int height, int width)
    {
        int half = height / 2;
        int requested = Math.Abs(width);
        return requested == 0 || half <= 0 || requested == half ? 1 : (double)requested / half;
    }

    private static uint InkedColumns(GlyphMask mask)
    {
        int left = int.MaxValue, right = -1;
        ReadOnlySpan<byte> coverage = mask.Coverage.Span;
        for (int y = 0; y < mask.Height; y++)
        for (int x = 0; x < mask.Width; x++)
            if (coverage[y * mask.Stride + x] != 0)
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        return right >= left ? (uint)(right - left + 1) : 0;
    }
}
