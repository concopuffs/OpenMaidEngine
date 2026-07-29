using System;
using System.Diagnostics;
using Age.Engine.Sys4;

/// <summary>Presentation-side ownership for one decoder plus its fail-safe completion deadline.</summary>
internal sealed record MovieRuntime(string Name, int AssetId, long ResourceId, IMovieDecoder Decoder,
                                    long MovieFlags, long InitialPositionMs,
                                    long StartedAtTimestamp, long WatchdogMs)
{
    public static MovieRuntime Open(string name, int assetId, long resourceId, MoviePayload payload,
                                    IMovieDecoderFactory factory, long movieFlags = 0,
                                    long initialPositionMs = 0)
    {
        ArgumentNullException.ThrowIfNull(factory);
        IMovieDecoder decoder = factory.Open(payload, Math.Max(0, initialPositionMs));
        long remainingMs = decoder.StopTimeMs is >= 0 and var stopTime
            ? Math.Max(0, stopTime - decoder.InitialPositionMs)
            : -1;
        return new MovieRuntime(name, assetId, resourceId, decoder, movieFlags,
            decoder.InitialPositionMs, Stopwatch.GetTimestamp(),
            remainingMs >= 0
                ? Math.Clamp(remainingMs + 2000, 5000, 300000)
                : 30000);
    }
}
