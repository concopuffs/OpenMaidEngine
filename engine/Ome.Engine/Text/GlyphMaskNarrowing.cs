namespace Ome.Engine.Text;

/// <summary>
/// Scales a glyph mask horizontally: the one narrowing the port draws with, for presented text an entry fits
/// (localization spec §7.2, <c>fit = "shrink"</c>) and for a font width a rasterizer without one of its own honours
/// (the portable rasterizer's <c>lfWidth</c>, §7.4). Each column of the scaled mask averages the source columns it
/// covers, weighted by how much of each it covers, so the glyph keeps its weight and antialiasing at any scale. The
/// bearing, the advance and the cell scale with it; the height, the vertical origin and the ascent do not. A
/// monochrome mask stays monochrome (a column at least half covered is set).
/// </summary>
public static class GlyphMaskNarrowing
{
    public static GlyphMask Narrow(GlyphMask mask, int widthPercent)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (widthPercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(widthPercent), widthPercent, "A glyph's width is 1 to 100 percent.");
        return widthPercent == 100 ? mask : Resize(mask, widthPercent / 100.0);
    }

    /// <summary><paramref name="mask"/> scaled horizontally by <paramref name="scale"/> (narrower below 1, wider above).
    /// </summary>
    public static GlyphMask Resize(GlyphMask mask, double scale)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "A glyph's horizontal scale is positive.");
        if (scale == 1) return mask;
        int width = mask.Width == 0 ? 0 : System.Math.Max(1, (int)System.Math.Ceiling(mask.Width * scale));
        ReadOnlySpan<byte> source = mask.Coverage.Span;
        var coverage = new byte[checked(width * mask.Height)];
        double span = 1 / scale;   // source columns per narrowed column
        for (int column = 0; column < width; column++)
        {
            double from = column * span, to = System.Math.Min(mask.Width, from + span);
            for (int row = 0; row < mask.Height; row++)
            {
                double sum = 0;
                for (int x = (int)from; x < to; x++)
                {
                    double overlap = System.Math.Min(to, x + 1) - System.Math.Max(from, x);
                    sum += source[row * mask.Stride + x] * overlap;
                }
                double value = sum / span;
                coverage[row * width + column] = mask.Monochrome
                    ? (byte)(value >= 8 ? 16 : 0)
                    : (byte)System.Math.Clamp((int)System.Math.Round(value), 0, 16);
            }
        }
        int Length(int length) => (int)System.Math.Round(length * scale);
        return new GlyphMask(
            width, mask.Height, width,
            Length(mask.OriginX), mask.OriginY,
            Length(mask.CellAdvanceX), mask.CellAdvanceY,
            System.Math.Max(width, Length(mask.CellWidth)), mask.CellHeight,
            coverage, mask.Ascent, mask.Monochrome);
    }

    /// <summary>A horizontal length (an advance, a cell, a bearing) narrowed to <paramref name="widthPercent"/>, rounded
    /// to the nearest pixel: what a narrowed glyph takes in any layout.</summary>
    public static int Scale(int length, int widthPercent)
        => widthPercent >= 100 ? length : (int)System.Math.Round(length * (widthPercent / 100.0));
}
