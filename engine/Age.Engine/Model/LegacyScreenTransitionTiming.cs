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
}
