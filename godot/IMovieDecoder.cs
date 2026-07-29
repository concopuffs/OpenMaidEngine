using System;
using Age.Engine.Sys4;

internal readonly record struct MovieAudioInfo(int SampleRate, int Channels);
internal sealed record MovieAudioChunk(float[] InterleavedStereo, int FrameCount, long PresentationTimeMs);

/// <summary>
/// Platform-neutral movie playback boundary. Construction is synchronous so metadata needed by the VM is
/// available before op 0x236 returns; frame delivery and completion remain asynchronous.
/// </summary>
internal interface IMovieDecoder : IDisposable
{
    long? StopTimeMs { get; }
    long InitialPositionMs => 0;
    bool IsCompleted { get; }
    string? Failure { get; }
    long? FirstFramePresentationTimeMs => null;
    bool TryTakeFrame(out RgbaImage frame);
    MovieAudioInfo? AudioInfo => null;
    bool AudioDecodingCompleted => true;
    bool TryTakeAudioChunk(out MovieAudioChunk chunk)
    {
        chunk = default!;
        return false;
    }
    void AdvancePlaybackClock(long elapsedMilliseconds) { }
    void MarkAudioSubmitted() { }
}

internal interface IMovieDecoderFactory
{
    IMovieDecoder Open(MoviePayload movie, long initialPositionMs = 0);
}
