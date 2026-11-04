namespace Age.Engine.Model;

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
}
