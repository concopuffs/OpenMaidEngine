using Ome.Engine.Sys4;

namespace Ome.Engine.Natives;

/// <summary>One playback's decoder as the movie native runs it (<see cref="Movies"/>): when it started, how long it
/// waits before its first frame, and the fail-safe deadline after which it counts as complete.</summary>
internal sealed record MovieRuntime(string Name, int AssetId, long ResourceId, IMovieDecoder Decoder,
                                    long MovieFlags, long InitialPositionMs,
                                    long StartedAtTimestamp, long StartDelayMs,
                                    long? PresentationDurationMs, long WatchdogMs)
{
    /// <summary>Open the decoder now, on the caller's thread, so its stop time is known at once (op 0x23f).
    /// <see cref="StartedAtTimestamp"/> is <paramref name="time"/>'s timestamp at the open.</summary>
    public static MovieRuntime Open(string name, int assetId, long resourceId, MoviePayload payload,
                                    IMovieDecoderFactory factory, long movieFlags = 0,
                                    long initialPositionMs = 0, long startDelayMs = 0,
                                    long? presentationDurationMs = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        long safeDelayMs = Math.Max(0, startDelayMs);
        long? safeDurationMs = presentationDurationMs is >= 0
            ? Math.Max(0, presentationDurationMs.Value)
            : null;
        IMovieDecoder decoder = factory.Open(
            payload.Name, payload.Bytes, Math.Max(0, initialPositionMs), safeDurationMs);
        long remainingMs = decoder.StopTimeMs is >= 0 and var stopTime
            ? Math.Max(0, stopTime - decoder.InitialPositionMs)
            : -1;
        long watchdogBasis = safeDurationMs ?? remainingMs;
        if (watchdogBasis >= 0) watchdogBasis = checked(watchdogBasis + safeDelayMs);
        return new MovieRuntime(name, assetId, resourceId, decoder, movieFlags,
            decoder.InitialPositionMs, (time ?? TimeProvider.System).GetTimestamp(),
            safeDelayMs, safeDurationMs,
            watchdogBasis >= 0
                ? Math.Clamp(watchdogBasis + 2000, 5000, 300000)
                : 30000);
    }
}
