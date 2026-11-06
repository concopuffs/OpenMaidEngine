using Age.Engine.Model;

namespace Age.Engine.Text;

/// <summary>AGE's deterministic 32-bit glyph-mask compositor, independent of mask provenance.</summary>
public static class AgeGlyphMaskCompositor
{
    /// <summary>
    /// Distance from the cell top to the baseline. Every AGE raster caller passes the selected font's
    /// tmAscent (text manager +0x458, TEXTMETRICA at +0x454; ruby +0x490), not its pixel height.
    /// </summary>
    public static int CellTopToBaseline(GlyphMask mask, int pixelHeight)
        => (mask ?? throw new ArgumentNullException(nameof(mask))).Ascent ?? pixelHeight;

    public static void DrawGlyph(
        RgbaImage destination,
        GlyphMask mask,
        int cellX,
        int cellY,
        int topToBaseline,
        AdvTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(mask);
        ValidateDestination(destination);
        int baselineY = checked(cellY + topToBaseline);

        if (style.RenderMode == 1)
            DrawMask(destination, mask,
                checked(cellX + style.EffectOffsetX),
                checked(baselineY + style.EffectOffsetY),
                style.EffectColor);
        else if (style.RenderMode == 3)
            foreach ((int x, int y) in GetMode3OutlineOffsets(
                         style.EffectOffsetX, style.EffectOffsetY))
                DrawMask(destination, mask, checked(cellX + x), checked(baselineY + y),
                         style.EffectColor);

        DrawMask(destination, mask, cellX, baselineY, style.TextColor);
        if (style.RenderMode == 2)
        {
            if (mask.Monochrome)
                DrawMask(destination, ModeTwoBitmapPass(mask), cellX, baselineY, style.TextColor);
            else
                DrawMask(destination, mask, cellX, baselineY, style.TextColor, coverageShift: 2);
        }
    }

    /// <summary>
    /// The glyph mode 2's second pass draws for an aliased glyph. text_raster_string_cached@0x45b600 builds that pass's
    /// buffer as <c>(signed char)byte &gt;&gt; 2</c> over the cached glyph bytes. For gray-4 coverage that is a quarter of
    /// each value; for GGO_BITMAP's packed rows it moves each byte's eight pixels two to the right and repeats the
    /// byte's first pixel into its first two, and every set bit still draws at full coverage.
    /// </summary>
    public static GlyphMask ModeTwoBitmapPass(GlyphMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (!mask.Monochrome) throw new ArgumentException("The mask is not an aliased glyph.", nameof(mask));
        ReadOnlySpan<byte> source = mask.Coverage.Span;
        var shifted = new byte[source.Length];
        for (int row = 0; row < mask.Height; row++)
        {
            int start = row * mask.Stride;
            for (int column = 0; column < mask.Width; column++)
            {
                int bit = column & 7;
                shifted[start + column] = source[start + (bit < 2 ? column - bit : column - 2)];
            }
        }
        return new GlyphMask(mask.Width, mask.Height, mask.Stride, mask.OriginX, mask.OriginY,
            mask.CellAdvanceX, mask.CellAdvanceY, mask.CellWidth, mask.CellHeight, shifted, mask.Ascent,
            monochrome: true);
    }

    /// <summary>
    /// Draw at a GDI-style baseline. mask.OriginX moves right from the pen and mask.OriginY moves
    /// upward from the baseline. coverageShift=2 reproduces AGE's mode-2 quarter-coverage pass.
    /// </summary>
    public static bool DrawMask(
        RgbaImage destination,
        GlyphMask mask,
        int baselineX,
        int baselineY,
        long rgb,
        int coverageShift = 0)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(mask);
        ValidateDestination(destination);
        if (coverageShift is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(coverageShift));

        int red = (int)((rgb >> 16) & 0xff);
        int green = (int)((rgb >> 8) & 0xff);
        int blue = (int)(rgb & 0xff);
        int left = checked(baselineX + mask.OriginX);
        int top = checked(baselineY - mask.OriginY);
        ReadOnlySpan<byte> coverage = mask.Coverage.Span;
        bool changed = false;

        for (int row = 0; row < mask.Height; row++)
        {
            int y = checked(top + row);
            if ((uint)y >= (uint)destination.Height) continue;
            int sourceRow = row * mask.Stride;
            for (int column = 0; column < mask.Width; column++)
            {
                int x = checked(left + column);
                if ((uint)x >= (uint)destination.Width) continue;
                int level = coverage[sourceRow + column] >> coverageShift;
                if (level == 0) continue;
                int alpha = level * 255 / 16;
                BlendTextPixel(destination.Pixels,
                    checked((y * destination.Width + x) * 4),
                    red, green, blue, alpha);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// The effect-glyph offsets of mode 3, in AGE's draw order. text_raster_string_cached@0x45b600 steps a float angle
    /// from 0 by <c>360 / (float)((float)sqrt((float)(ry + rx)) * 8)</c> while it is below 360 (sampling at least once),
    /// and text_compute_outline_ellipse_offset@0x459860 turns each angle into <c>cos(a)*rx</c>, <c>sin(a)*ry</c> after
    /// converting it with <c>a * 180 / pi</c>, the inverse of degrees to radians, so the samples fall at scattered
    /// directions. Each value is stored as a float, moved 0.5 away from zero and truncated. For Himegari's (1,1) the
    /// twelve samples cover six neighbours: never (+1,-1) or (-1,+1).
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> GetMode3OutlineOffsets(int radiusX, int radiusY)
    {
        float root = (float)Math.Sqrt((float)((double)radiusY + radiusX));
        double step = 360.0 / (float)(root * 8.0);
        var result = new List<(int X, int Y)>();
        float angle = 0f;
        do
        {
            double argument = angle * 180.0 / Math.PI;
            result.Add((
                RoundAwayFromZero((float)(Math.Cos(argument) * radiusX)),
                RoundAwayFromZero((float)(Math.Sin(argument) * radiusY))));
            angle = (float)(angle + step);
        } while (angle < 360f);
        return result;
    }

    private static int RoundAwayFromZero(float value)
        => value > 0 ? (int)(float)(value + 0.5) : value < 0 ? (int)(float)(value - 0.5) : 0;

    private static void BlendTextPixel(
        byte[] destination, int offset, int red, int green, int blue, int alpha)
    {
        int destinationAlpha = destination[offset + 3];
        if (alpha == 255 || destinationAlpha == 0)
        {
            destination[offset] = (byte)red;
            destination[offset + 1] = (byte)green;
            destination[offset + 2] = (byte)blue;
            destination[offset + 3] = (byte)alpha;
            return;
        }

        int inverse = 255 - alpha;
        destination[offset] =
            (byte)((destination[offset] * inverse + red * alpha) / 255);
        destination[offset + 1] =
            (byte)((destination[offset + 1] * inverse + green * alpha) / 255);
        destination[offset + 2] =
            (byte)((destination[offset + 2] * inverse + blue * alpha) / 255);
        destination[offset + 3] = (byte)Math.Max(destinationAlpha, alpha);
    }

    private static void ValidateDestination(RgbaImage destination)
    {
        if (destination.Width <= 0 || destination.Height <= 0
            || destination.Pixels.Length != checked(destination.Width * destination.Height * 4))
            throw new ArgumentException("Destination must be a non-empty tightly packed RGBA image.",
                                        nameof(destination));
    }
}
