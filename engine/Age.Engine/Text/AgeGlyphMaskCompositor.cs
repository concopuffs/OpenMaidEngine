using Age.Engine.Model;
using Age.Engine.Sys4;

namespace Age.Engine.Text;

/// <summary>AGE's deterministic 32-bit glyph-mask compositor, independent of mask provenance.</summary>
public static class AgeGlyphMaskCompositor
{
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
            DrawMask(destination, mask, cellX, baselineY, style.TextColor, coverageShift: 2);
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

    public static IReadOnlyList<(int X, int Y)> GetMode3OutlineOffsets(int radiusX, int radiusY)
    {
        double radius = Math.Sqrt((double)radiusX * radiusX + (double)radiusY * radiusY);
        if (radius == 0) return Array.Empty<(int, int)>();
        int sampleCount = checked((int)Math.Ceiling(radius * 8.0));
        var result = new (int X, int Y)[sampleCount];
        double step = 360.0 / (radius * 8.0);
        for (int index = 0; index < result.Length; index++)
        {
            double radians = index * step * Math.PI / 180.0;
            result[index] = (
                RoundNearest(Math.Cos(radians) * radiusX),
                RoundNearest(Math.Sin(radians) * radiusY));
        }
        return result;
    }

    private static int RoundNearest(double value)
        => checked((int)(value < 0 ? value - 0.5 : value + 0.5));

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
