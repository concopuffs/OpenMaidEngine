using Age.Engine.Model;

namespace Age.Engine.Sys4;

/// <summary>Platform-neutral pixel oracle for opcode 0x24d's type-1 movie transition.</summary>
public static class MovieMaskSurface
{
    /// <summary>Extract logical top-down green samples into the requested native mask dimensions.
    /// Missing source pixels retain <paramref name="fill"/>.</summary>
    public static byte[] ExtractGreen(RgbaImage frame, int width, int height, byte fill)
    {
        int safeWidth = System.Math.Max(0, width);
        int safeHeight = System.Math.Max(0, height);
        var mask = new byte[checked(safeWidth * safeHeight)];
        if (fill != 0) System.Array.Fill(mask, fill);
        int copyWidth = System.Math.Min(safeWidth, frame.Width);
        int copyHeight = System.Math.Min(safeHeight, frame.Height);
        for (int row = 0; row < copyHeight; row++)
        {
            int source = checked(row * frame.Width * 4 + 1);
            int destination = checked(row * safeWidth);
            for (int column = 0; column < copyWidth; column++, source += 4)
                mask[destination + column] = frame.Pixels[source];
        }
        return mask;
    }

    /// <summary>Apply AGE's exact packed-ARGB multiplication to a captured RGBA surface. RGB and
    /// pixels outside the requested rectangle are preserved. The multiplication deliberately includes
    /// carry from the packed red/green bytes; it is not conventional alpha*mask/255.</summary>
    public static RgbaImage Apply(
        RgbaImage captured, ReadOnlySpan<byte> mask, int maskWidth, int maskHeight, int x, int y)
    {
        if (maskWidth < 0 || maskHeight < 0
            || mask.Length != checked(maskWidth * maskHeight))
            throw new System.ArgumentException("mask dimensions do not match its byte count", nameof(mask));
        var output = new RgbaImage(
            captured.Width, captured.Height, (byte[])captured.Pixels.Clone());
        long left = System.Math.Max(0L, x);
        long top = System.Math.Max(0L, y);
        long right = System.Math.Min((long)captured.Width, (long)x + maskWidth);
        long bottom = System.Math.Min((long)captured.Height, (long)y + maskHeight);
        if (right <= left || bottom <= top) return output;

        for (int destinationY = (int)top; destinationY < (int)bottom; destinationY++)
        {
            int maskY = destinationY - y;
            for (int destinationX = (int)left; destinationX < (int)right; destinationX++)
            {
                int pixel = checked((destinationY * captured.Width + destinationX) * 4);
                byte red = captured.Pixels[pixel];
                byte green = captured.Pixels[pixel + 1];
                byte blue = captured.Pixels[pixel + 2];
                byte alpha = captured.Pixels[pixel + 3];
                uint argb = (uint)alpha << 24
                            | (uint)red << 16
                            | (uint)green << 8
                            | blue;
                uint product = unchecked((argb >> 8)
                    * mask[checked(maskY * maskWidth + destinationX - x)]);
                output.Pixels[pixel + 3] = (byte)(product >> 24);
            }
        }
        return output;
    }
}
