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
        // The periods the tick counts before it reports the reveal finished: the wipes take one a division, the strips
        // one a pixel of a strip, and the staggered strips start one period apart.
        long span = LegacyPatternReveal.Span(width, height, divisions, mode);
        if (span == 0) return 0;
        return System.Math.Clamp(span * PatternPeriodMilliseconds(intervalMilliseconds), 1, 60_000);
    }

    /// <summary>The interval timer's period for a pattern reveal or the mosaic transition: sleep_timer_arm takes 1 for
    /// an interval of 0.</summary>
    public static long PatternPeriodMilliseconds(long intervalMilliseconds) => System.Math.Max(1, intervalMilliseconds);

    /// <summary>Op 0x36's transition (screen_transition_begin mode 9) runs for this many timer periods: the parameter
    /// 0xa0 its handler passes.</summary>
    public const int MosaicPeriods = 0xa0;

    public static long MosaicDurationMilliseconds(long intervalMilliseconds)
        => System.Math.Clamp(MosaicPeriods * PatternPeriodMilliseconds(intervalMilliseconds), 1, 60_000);

    /// <summary>
    /// What mode 9's tick draws once <paramref name="periods"/> timer periods (1 or more) have passed: the source
    /// surface in blocks that grow by a pixel a period over the first half, then the target surface in blocks that
    /// shrink again. Its counter is one behind the periods, since the tick draws before it counts the period it runs
    /// in. A dot of 0, which only the first period gives, is a size MosaicTexture refuses; a dot of 1 is the surface
    /// as it is.
    /// </summary>
    public static (bool ShowsTarget, int Dot) MosaicAt(long periods)
    {
        long counter = System.Math.Clamp(periods - 1, 0, MosaicPeriods);
        return counter < MosaicPeriods / 2
            ? (false, (int)counter)
            : (true, (int)(MosaicPeriods - counter) + 1);
    }

    private const string WidthTrace = "関数：SetLineFade エラー：分割サイズが不正です． 画面の幅で割り切れるようにしてください．";
    private const string HeightTrace = "関数：SetLineFade エラー：分割サイズが不正です． 画面の高さで割り切れるようにしてください．";
    private const string ModeTrace = "関数：SetLineFade エラー：タイプが不正です．";

    /// <summary>
    /// The test <c>pattern_surface_transition_begin</c> (/v2 <c>0x43c4a0</c>, 4.33G <c>0x43cdd0</c>) makes in the
    /// shipped draw mode before it begins (ops 0x26 to 0x28): the screen's width (modes 0, 1, 4, 5, 8 and 9) or height
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
