namespace Ome.Engine.Text;

/// <summary>
/// Narrows a glyph mask horizontally (localization spec §7.2, <c>fit = "shrink"</c>): each column of the narrowed
/// mask averages the source columns it covers, weighted by how much of each it covers, so the glyph keeps its weight
/// and antialiasing at any scale. The bearing, the advance and the cell scale with it; the height, the vertical origin
/// and the ascent do not. A monochrome mask stays monochrome (a column at least half covered is set).
/// </summary>
public static class GlyphMaskNarrowing
{
    public static GlyphMask Narrow(GlyphMask mask, int widthPercent)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (widthPercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(widthPercent), widthPercent, "A glyph's width is 1 to 100 percent.");
        if (widthPercent == 100) return mask;
        double scale = widthPercent / 100.0;
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
        return new GlyphMask(
            width, mask.Height, width,
            (int)System.Math.Round(mask.OriginX * scale), mask.OriginY,
            (int)System.Math.Round(mask.CellAdvanceX * scale), mask.CellAdvanceY,
            System.Math.Max(width, (int)System.Math.Round(mask.CellWidth * scale)), mask.CellHeight,
            coverage, mask.Ascent, mask.Monochrome);
    }
}
