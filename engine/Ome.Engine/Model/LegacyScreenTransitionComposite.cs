namespace Ome.Engine.Model;

/// <summary>
/// Pixel blend of the legacy full-frame screen transitions (ops 0x21/0x22/0x24/0x25; native
/// screen_transition_tick mode 4). Native draws the captured source surface opaquely, then the complete target
/// surface over it at the progress alpha. Each surface is flattened first: its objects drawn over the surface's
/// clear. So a pixel that is the same in both surfaces never changes during the transition.
/// </summary>
public static class LegacyScreenTransitionComposite
{
    /// <summary>The 8-bit alpha of a progress in [0, 1], on native's 0..0x100 accumulator scale.</summary>
    public static int Alpha(double progress) => (int)System.Math.Round(System.Math.Clamp(progress, 0.0, 1.0) * 256);

    /// <summary>Blend <paramref name="target"/> (the flattened target surface, RGBA8) over
    /// <paramref name="source"/> (the flattened source surface) in place: target = source + (target - source) ×
    /// alpha / 256. The result is opaque.</summary>
    public static void BlendTargetOverSource(byte[] target, byte[] source, int alpha)
    {
        if (target.Length != source.Length)
            throw new System.ArgumentException("Source and target surfaces differ in size.", nameof(source));
        alpha = System.Math.Clamp(alpha, 0, 256);
        int keep = 256 - alpha;
        for (int i = 0; i + 3 < target.Length; i += 4)
        {
            target[i] = (byte)((source[i] * keep + target[i] * alpha) >> 8);
            target[i + 1] = (byte)((source[i + 1] * keep + target[i + 1] * alpha) >> 8);
            target[i + 2] = (byte)((source[i + 2] * keep + target[i + 2] * alpha) >> 8);
            target[i + 3] = 255;
        }
    }

    /// <summary>A patterned reveal (ops 0x26 to 0x28; native pattern_surface_transition_tick draws the surface's
    /// rectangles over the frame that was there): <paramref name="target"/> (the flattened target surface, RGBA8)
    /// keeps its pixels inside <paramref name="rects"/> and takes <paramref name="source"/>'s everywhere else. The
    /// rectangles are clipped to the frame, the result is opaque, and <paramref name="source"/> is used as
    /// scratch.</summary>
    public static void RevealTargetOverSource(byte[] target, byte[] source, int width, int height,
                                              System.Collections.Generic.IReadOnlyList<Ome.Engine.Devices.FrameRect> rects)
    {
        if (target.Length != source.Length || target.Length != (long)width * height * 4)
            throw new System.ArgumentException("Source and target surfaces differ in size.", nameof(source));
        for (int index = 0; index < rects.Count; index++)
        {
            Ome.Engine.Devices.FrameRect rect = rects[index];
            int left = System.Math.Max(0, rect.Left), right = System.Math.Min(width, rect.Right);
            int top = System.Math.Max(0, rect.Top), bottom = System.Math.Min(height, rect.Bottom);
            if (left >= right) continue;
            for (int y = top; y < bottom; y++)
            {
                int offset = (y * width + left) * 4;
                System.Buffer.BlockCopy(target, offset, source, offset, (right - left) * 4);
            }
        }
        System.Buffer.BlockCopy(source, 0, target, 0, target.Length);
        for (int i = 3; i < target.Length; i += 4) target[i] = 255;
    }

    /// <summary>Native MosaicTexture (AGE 4.38D <c>0x48ee10</c>; op 0x36's transition draws through it): every pixel
    /// of <paramref name="pixels"/> (RGBA8) takes the pixel at the top-left corner of its
    /// <paramref name="dot"/> × <paramref name="dot"/> block, the blocks counted from the frame's top-left corner. A
    /// dot of 1 or less changes nothing.</summary>
    public static void Mosaic(byte[] pixels, int width, int height, int dot)
    {
        if (dot <= 1) return;
        if (pixels.Length != (long)width * height * 4)
            throw new System.ArgumentException("The frame's size does not match its pixels.", nameof(pixels));
        int stride = width * 4;
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            if (y % dot != 0)
            {
                System.Buffer.BlockCopy(pixels, (y - y % dot) * stride, pixels, row, stride);   // the block's first row
                continue;
            }
            for (int x = 0; x < width; x++)
            {
                int sample = x - x % dot;
                if (sample == x) continue;
                int from = row + sample * 4, to = row + x * 4;
                pixels[to] = pixels[from];
                pixels[to + 1] = pixels[from + 1];
                pixels[to + 2] = pixels[from + 2];
                pixels[to + 3] = pixels[from + 3];
            }
        }
    }
}
