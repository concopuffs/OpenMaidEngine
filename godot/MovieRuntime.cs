using System;
using System.Diagnostics;
using Age.Engine.Sys4;

/// <summary>Presentation-side ownership for one decoder plus its fail-safe completion deadline.</summary>
internal sealed record MovieRuntime(string Name, int AssetId, long ResourceId, IMovieDecoder Decoder,
                                    long MovieFlags, long InitialPositionMs,
                                    long StartedAtTimestamp, long StartDelayMs,
                                    long? PresentationDurationMs, long WatchdogMs)
{
    public static MovieRuntime Open(string name, int assetId, long resourceId, MoviePayload payload,
                                    IMovieDecoderFactory factory, long movieFlags = 0,
                                    long initialPositionMs = 0, long startDelayMs = 0,
                                    long? presentationDurationMs = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        long safeDelayMs = Math.Max(0, startDelayMs);
        long? safeDurationMs = presentationDurationMs is >= 0
            ? Math.Max(0, presentationDurationMs.Value)
            : null;
        IMovieDecoder decoder = factory.Open(
            payload, Math.Max(0, initialPositionMs), safeDurationMs);
        long remainingMs = decoder.StopTimeMs is >= 0 and var stopTime
            ? Math.Max(0, stopTime - decoder.InitialPositionMs)
            : -1;
        long watchdogBasis = safeDurationMs ?? remainingMs;
        if (watchdogBasis >= 0) watchdogBasis = checked(watchdogBasis + safeDelayMs);
        return new MovieRuntime(name, assetId, resourceId, decoder, movieFlags,
            decoder.InitialPositionMs, Stopwatch.GetTimestamp(),
            safeDelayMs, safeDurationMs,
            watchdogBasis >= 0
                ? Math.Clamp(watchdogBasis + 2000, 5000, 300000)
                : 30000);
    }
}
