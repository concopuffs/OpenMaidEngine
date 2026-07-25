using System;

internal readonly record struct MovieAudioAdjustment(long GapFrames, int SkipFrames);

/// <summary>
/// Converts coarse MPEG timestamps into PCM alignment decisions without turning sub-millisecond
/// timestamp quantization into an audible splice at every decoded block boundary.
/// </summary>
internal static class MovieAudioTimeline
{
    private const int TimestampJitterToleranceMilliseconds = 2;

    public static long PresentationFrame(long presentationTimeMs, int sampleRate)
    {
        if (presentationTimeMs < 0)
            throw new ArgumentOutOfRangeException(nameof(presentationTimeMs));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        return checked((long)Math.Round(
            presentationTimeMs * (double)sampleRate / 1000.0,
            MidpointRounding.AwayFromZero));
    }

    public static MovieAudioAdjustment Align(
        long submittedFrames,
        long targetFrame,
        int chunkFrames,
        int sampleRate,
        bool timelineAnchored)
    {
        if (submittedFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(submittedFrames));
        if (targetFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(targetFrame));
        if (chunkFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkFrames));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        long delta = targetFrame - submittedFrames;
        long tolerance = timelineAnchored
            ? Math.Max(1L, (sampleRate * (long)TimestampJitterToleranceMilliseconds + 999L) / 1000L)
            : 0L;
        if (delta > tolerance)
            return new MovieAudioAdjustment(delta, 0);
        if (delta < -tolerance)
            return new MovieAudioAdjustment(0, checked((int)Math.Min(chunkFrames, -delta)));
        return default;
    }
}
