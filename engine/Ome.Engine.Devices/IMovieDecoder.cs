using Ome.Engine.Model;

public readonly record struct MovieAudioInfo(int SampleRate, int Channels);
public sealed record MovieAudioChunk(float[] InterleavedStereo, int FrameCount, long PresentationTimeMs);

/// <summary>
/// Platform-neutral movie playback boundary. Construction is synchronous so metadata needed by the VM is
/// available before op 0x236 returns; frame delivery and completion remain asynchronous.
/// </summary>
public interface IMovieDecoder : IDisposable
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
    void SetPlaybackRate(double rate) { }
}

public interface IMovieDecoderFactory
{
    /// <summary>Open a movie from its file's bytes; <paramref name="name"/> names it in diagnostics.</summary>
    IMovieDecoder Open(
        string name, byte[] bytes, long initialPositionMs = 0, long? presentationDurationMs = null);
}
