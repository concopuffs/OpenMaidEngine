using System;
using System.Runtime.Versioning;
using Age.Engine.Sys4;

/// <summary>
/// Platform-neutral movie playback boundary. Construction is synchronous so metadata needed by the VM is
/// available before op 0x236 returns; frame delivery and completion remain asynchronous.
/// </summary>
internal interface IMovieDecoder : IDisposable
{
    long? StopTimeMs { get; }
    bool IsCompleted { get; }
    bool TryTakeFrame(out RgbaImage frame);
}

internal interface IMovieDecoderFactory
{
    IMovieDecoder Open(MoviePayload movie);
}

[SupportedOSPlatform("windows")]
internal sealed class DirectShowMovieDecoderFactory : IMovieDecoderFactory
{
    public IMovieDecoder Open(MoviePayload movie) => new DirectShowMovieDecoder(movie);
}
