using System;
using System.Diagnostics;
using Age.Engine.Sys4;

/// <summary>Presentation-side ownership for one decoder plus its fail-safe completion deadline.</summary>
internal sealed record MovieRuntime(string Name, int AssetId, long ResourceId, IMovieDecoder Decoder,
                                    long StartedAtTimestamp, long WatchdogMs)
{
    public static MovieRuntime Open(string name, int assetId, long resourceId, MoviePayload payload,
                                    IMovieDecoderFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        IMovieDecoder decoder = factory.Open(payload);
        return new MovieRuntime(name, assetId, resourceId, decoder, Stopwatch.GetTimestamp(),
            decoder.StopTimeMs is >= 0 and var stopTime
                ? Math.Clamp(stopTime + 2000, 5000, 300000)
                : 30000);
    }
}
