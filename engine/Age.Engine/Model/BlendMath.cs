namespace Age.Engine.Model;

/// <summary>Pure blend helpers shared by the engine resolution and the host blit. Colorkey semantics come
/// from the native loader (docs/engine-re.md §"Blend & transparency"): op arg 3 &lt; 0 = no key; otherwise
/// the operand is 0xRRGGBB and matching (R,G,B) texels are transparent (operand 0 = key black).</summary>
public static class BlendMath
{
    /// <summary>A colorkey operand &gt;= 0 is an active key; a negative operand means "no colorkey".</summary>
    public static bool HasColorKey(long colorKey) => colorKey >= 0;

    /// <summary>True when (r,g,b) exactly equals the key's 0xRRGGBB bytes. Always false when there is no key.</summary>
    public static bool ColorKeyMatches(byte r, byte g, byte b, long colorKey)
    {
        if (!HasColorKey(colorKey)) return false;
        return r == ((colorKey >> 16) & 0xff) && g == ((colorKey >> 8) & 0xff) && b == (colorKey & 0xff);
    }

    /// <summary>Split a 0xAARRGGBB packed color (see GfxState.PackColor) into its byte channels.</summary>
    public static (int A, int R, int G, int B) UnpackArgb(long packed)
        => ((int)((packed >> 24) & 0xff), (int)((packed >> 16) & 0xff),
            (int)((packed >> 8) & 0xff), (int)(packed & 0xff));

    /// <summary>Ping-pong (triangle-wave) progress of gfx_object_anim_interpolate: fold (now-start) mod
    /// period at period/2 so the value ramps to the peak at half-period then back. Returns u in
    /// [0, period/2]; the interpolation weight toward the target is u / (period/2).</summary>
    public static long PingPong(long now, long start, long period)
    {
        if (period <= 0) return 0;
        long u = ((now - start) % period + period) % period;
        if (u >= period / 2) u = period - u;
        return u;
    }
}
