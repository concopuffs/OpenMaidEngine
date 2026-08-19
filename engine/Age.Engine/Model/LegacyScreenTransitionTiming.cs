namespace Age.Engine.Model;

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
}
