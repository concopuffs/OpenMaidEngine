namespace Ome.Engine.Model;

/// <summary>Native timing conversion shared by the legacy full-frame screen-transition family.</summary>
public static class LegacyScreenTransitionTiming
{
    public static long DurationMilliseconds(long argument)
    {
        long interval = argument <= 64 ? argument : argument / 16;
        long step = argument <= 64 ? 16 : 1;
        interval = System.Math.Max(1, interval);
        return System.Math.Clamp(((256 + step - 1) / step) * interval, 1, 60_000);
    }

    public static long PatternDurationMilliseconds(
        int width, int height, long intervalMilliseconds, int divisions,
        SurfacePatternTransitionMode mode)
    {
        if (width <= 0 || height <= 0 || divisions <= 0) return 0;
        long span;
        switch (mode)
        {
            case SurfacePatternTransitionMode.VerticalStripsLeftToRight:
            case SurfacePatternTransitionMode.VerticalStripsRightToLeft:
                if (width % divisions != 0) return 0;
                span = width / divisions;
                break;
            case SurfacePatternTransitionMode.HorizontalStripsTopToBottom:
            case SurfacePatternTransitionMode.HorizontalStripsBottomToTop:
                if (height % divisions != 0) return 0;
                span = height / divisions;
                break;
            case SurfacePatternTransitionMode.StaggeredVerticalStripsLeftToRight:
            case SurfacePatternTransitionMode.StaggeredVerticalStripsRightToLeft:
                if (width % divisions != 0) return 0;
                span = divisions - 1L + width / divisions;
                break;
            case SurfacePatternTransitionMode.StaggeredHorizontalStripsTopToBottom:
            case SurfacePatternTransitionMode.StaggeredHorizontalStripsBottomToTop:
                if (height % divisions != 0) return 0;
                span = divisions - 1L + height / divisions;
                break;
            default:
                return 0;
        }
        long interval = System.Math.Max(1, intervalMilliseconds);
        return System.Math.Clamp(span * interval, 1, 60_000);
    }

    private const string WidthTrace = "関数：SetLineFade エラー：分割サイズが不正です． 画面の幅で割り切れるようにしてください．";
    private const string HeightTrace = "関数：SetLineFade エラー：分割サイズが不正です． 画面の高さで割り切れるようにしてください．";
    private const string ModeTrace = "関数：SetLineFade エラー：タイプが不正です．";

    /// <summary>
    /// The test <c>pattern_surface_transition_begin</c> (/v2 <c>0x43c4a0</c>, 4.33G <c>0x43cdd0</c>) makes in the
    /// shipped draw mode before it begins (ops 0x27 and 0x28): the screen's width (modes 0, 1, 4, 5, 8 and 9) or height
    /// (modes 2, 3, 6, 7, 10 and 11) must divide by <paramref name="divisions"/>, and any other mode is refused. Null
    /// when it begins; otherwise what is wrong and native's trace, after which the op starts nothing. The ops finish at
    /// once instead of beginning when <paramref name="divisions"/> is not positive.
    /// </summary>
    public static PatternTransitionRefusal? PatternBeginRefusal(
        int width, int height, int divisions, SurfacePatternTransitionMode mode)
    {
        if (divisions <= 0) throw new ArgumentOutOfRangeException(nameof(divisions));
        switch ((int)mode)
        {
            case 0: case 1: case 4: case 5: case 8: case 9:
                return width % divisions == 0
                    ? null
                    : new($"{divisions} divisions do not divide the screen's width {width}", WidthTrace);
            case 2: case 3: case 6: case 7: case 10: case 11:
                return height % divisions == 0
                    ? null
                    : new($"{divisions} divisions do not divide the screen's height {height}", HeightTrace);
            default:
                return new($"pattern mode {(int)mode} is not one of 0..11", ModeTrace);
        }
    }
}

/// <summary>Why a pattern transition does not begin, and native's trace (<see
/// cref="LegacyScreenTransitionTiming.PatternBeginRefusal"/>).</summary>
public readonly record struct PatternTransitionRefusal(string Reason, string Trace);
