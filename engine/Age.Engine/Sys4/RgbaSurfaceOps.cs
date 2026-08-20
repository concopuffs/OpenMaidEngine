using Age.Engine.Model;

namespace Age.Engine.Sys4;

/// <summary>Platform-neutral mutation helpers for AGE's software-modeled RGBA surfaces.</summary>
public static class RgbaSurfaceOps
{
    public static bool FillRect(RgbaImage destination, int x, int y, int width, int height,
                                byte alpha, long rgb)
    {
        long left = x, top = y, right = left + width, bottom = top + height;
        if (width <= 0 || height <= 0) return false;
        left = System.Math.Max(0, left);
        top = System.Math.Max(0, top);
        right = System.Math.Min(destination.Width, right);
        bottom = System.Math.Min(destination.Height, bottom);
        if (right <= left || bottom <= top) return false;

        byte red = (byte)((rgb >> 16) & 0xff);
        byte green = (byte)((rgb >> 8) & 0xff);
        byte blue = (byte)(rgb & 0xff);
        for (int row = (int)top; row < (int)bottom; row++)
        {
            int offset = checked((row * destination.Width + (int)left) * 4);
            for (int column = (int)left; column < (int)right; column++, offset += 4)
            {
                destination.Pixels[offset] = red;
                destination.Pixels[offset + 1] = green;
                destination.Pixels[offset + 2] = blue;
                destination.Pixels[offset + 3] = alpha;
            }
        }
        return true;
    }

    public static RgbaImage WithColorKey(RgbaImage source, long colorKey)
    {
        if (!Age.Engine.Model.BlendMath.HasColorKey(colorKey)) return source;
        byte[] pixels = (byte[])source.Pixels.Clone();
        for (int index = 0; index < pixels.Length; index += 4)
            if (Age.Engine.Model.BlendMath.ColorKeyMatches(
                    pixels[index], pixels[index + 1], pixels[index + 2], colorKey))
                pixels[index + 3] = 0;
        return new RgbaImage(source.Width, source.Height, pixels);
    }

    /// <summary>Copy a rectangle while clipping source and destination together. A temporary buffer
    /// preserves native blit behavior when the two rectangles overlap in the same surface.</summary>
    public static bool CopyRect(RgbaImage source, RgbaImage destination,
                                int sourceX, int sourceY, int width, int height,
                                int destinationX, int destinationY)
    {
        long sx = sourceX, sy = sourceY, dx = destinationX, dy = destinationY;
        long w = width, h = height;
        if (w <= 0 || h <= 0) return false;

        if (sx < 0) { long n = -sx; sx = 0; dx += n; w -= n; }
        if (sy < 0) { long n = -sy; sy = 0; dy += n; h -= n; }
        if (dx < 0) { long n = -dx; dx = 0; sx += n; w -= n; }
        if (dy < 0) { long n = -dy; dy = 0; sy += n; h -= n; }
        w = System.Math.Min(w, source.Width - sx);
        h = System.Math.Min(h, source.Height - sy);
        w = System.Math.Min(w, destination.Width - dx);
        h = System.Math.Min(h, destination.Height - dy);
        if (w <= 0 || h <= 0) return false;

        int clippedWidth = checked((int)w);
        int clippedHeight = checked((int)h);
        int rowBytes = checked(clippedWidth * 4);
        byte[] pixels = new byte[checked(rowBytes * clippedHeight)];
        for (int row = 0; row < clippedHeight; row++)
        {
            int sourceOffset = checked(((int)sy + row) * source.Width * 4 + (int)sx * 4);
            source.Pixels.AsSpan(sourceOffset, rowBytes).CopyTo(pixels.AsSpan(row * rowBytes, rowBytes));
        }
        for (int row = 0; row < clippedHeight; row++)
        {
            int destinationOffset = checked(((int)dy + row) * destination.Width * 4 + (int)dx * 4);
            pixels.AsSpan(row * rowBytes, rowBytes)
                .CopyTo(destination.Pixels.AsSpan(destinationOffset, rowBytes));
        }
        return true;
    }

    /// <summary>
    /// Scale one RGBA rectangle into another with area-weighted sampling. This matches AGE's shrink path,
    /// including the 1024x576 to 384x216 map/save thumbnail operation. Source pixels are sampled from a
    /// stable image while writes are clipped to the destination surface.
    /// </summary>
    public static bool ScaleCopyRect(RgbaImage source, RgbaImage destination,
                                     int sourceX, int sourceY, int sourceWidth, int sourceHeight,
                                     int destinationX, int destinationY,
                                     int destinationWidth, int destinationHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0
            || destinationWidth <= 0 || destinationHeight <= 0) return false;

        long left = System.Math.Max(0L, destinationX);
        long top = System.Math.Max(0L, destinationY);
        long right = System.Math.Min((long)destination.Width, (long)destinationX + destinationWidth);
        long bottom = System.Math.Min((long)destination.Height, (long)destinationY + destinationHeight);
        if (right <= left || bottom <= top) return false;

        bool wrote = false;
        for (int y = (int)top; y < (int)bottom; y++)
        {
            double sy0 = sourceY + (double)(y - destinationY) * sourceHeight / destinationHeight;
            double sy1 = sourceY + (double)(y + 1 - destinationY) * sourceHeight / destinationHeight;
            for (int x = (int)left; x < (int)right; x++)
            {
                double sx0 = sourceX + (double)(x - destinationX) * sourceWidth / destinationWidth;
                double sx1 = sourceX + (double)(x + 1 - destinationX) * sourceWidth / destinationWidth;
                double clippedSx0 = System.Math.Max(0.0, sx0);
                double clippedSy0 = System.Math.Max(0.0, sy0);
                double clippedSx1 = System.Math.Min(source.Width, sx1);
                double clippedSy1 = System.Math.Min(source.Height, sy1);
                if (clippedSx1 <= clippedSx0 || clippedSy1 <= clippedSy0) continue;

                double red = 0, green = 0, blue = 0, alpha = 0, total = 0;
                int firstSourceY = (int)System.Math.Floor(clippedSy0);
                int lastSourceY = (int)System.Math.Ceiling(clippedSy1);
                int firstSourceX = (int)System.Math.Floor(clippedSx0);
                int lastSourceX = (int)System.Math.Ceiling(clippedSx1);
                for (int sourceRow = firstSourceY; sourceRow < lastSourceY; sourceRow++)
                {
                    double yWeight = System.Math.Min(clippedSy1, sourceRow + 1.0)
                                     - System.Math.Max(clippedSy0, sourceRow);
                    if (yWeight <= 0) continue;
                    for (int sourceColumn = firstSourceX; sourceColumn < lastSourceX; sourceColumn++)
                    {
                        double xWeight = System.Math.Min(clippedSx1, sourceColumn + 1.0)
                                         - System.Math.Max(clippedSx0, sourceColumn);
                        double weight = xWeight * yWeight;
                        if (weight <= 0) continue;
                        int sourceOffset = checked((sourceRow * source.Width + sourceColumn) * 4);
                        red += source.Pixels[sourceOffset] * weight;
                        green += source.Pixels[sourceOffset + 1] * weight;
                        blue += source.Pixels[sourceOffset + 2] * weight;
                        alpha += source.Pixels[sourceOffset + 3] * weight;
                        total += weight;
                    }
                }
                if (total <= 0) continue;

                int destinationOffset = checked((y * destination.Width + x) * 4);
                destination.Pixels[destinationOffset] = ClampByte(red / total);
                destination.Pixels[destinationOffset + 1] = ClampByte(green / total);
                destination.Pixels[destinationOffset + 2] = ClampByte(blue / total);
                destination.Pixels[destinationOffset + 3] = ClampByte(alpha / total);
                wrote = true;
            }
        }
        return wrote;
    }

    private static byte ClampByte(double value)
        => (byte)System.Math.Clamp((int)System.Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
