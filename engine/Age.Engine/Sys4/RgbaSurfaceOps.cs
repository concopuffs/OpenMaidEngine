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
}
