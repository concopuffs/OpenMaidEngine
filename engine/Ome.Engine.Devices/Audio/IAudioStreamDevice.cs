namespace Ome.Engine.Devices;

/// <summary>
/// Streamed sound output: a sound track the engine feeds in blocks of PCM as it decodes them, such as a movie's
/// (engine layering spec §6.1, milestone 8).
/// <para>Unlike <see cref="IAudioDevice"/>, a stream is fed and read on the front end's thread, in the frame service
/// that calls it (the movie native's <c>ServiceFrame</c>), and its reads answer at once: how much room the stream has
/// and how far its sound has been heard. Milestone 11 decides the threads.</para>
/// </summary>
public interface IAudioStreamDevice
{
    /// <summary>A stopped stream of interleaved stereo at <paramref name="sampleRate"/> through
    /// <paramref name="bus"/>; a muted one plays silently and keeps its clock.</summary>
    IAudioStream Open(int sampleRate, AudioBus bus, bool muted);
}

/// <summary>One sound track's output, from <see cref="IAudioStreamDevice.Open"/>. Disposing it stops it.</summary>
public interface IAudioStream : System.IDisposable
{
    /// <summary>Start the stream held: it takes frames but plays none until <see cref="Resume"/>.</summary>
    void StartHeld();

    /// <summary>Play the frames pushed so far and from now on.</summary>
    void Resume();

    /// <summary>Stereo frames the stream can take now.</summary>
    int FramesFree { get; }

    /// <summary>Queue <paramref name="frames"/> frames of <paramref name="interleaved"/> from frame
    /// <paramref name="offsetFrames"/>; false when the stream has no room for them.</summary>
    bool Push(float[] interleaved, int offsetFrames, int frames);

    /// <summary>Queue <paramref name="frames"/> frames of silence; false when the stream has no room.</summary>
    bool PushSilence(int frames);

    /// <summary>Seconds of the stream heard so far: the output's position less its latency; 0 before it starts.</summary>
    double HeardSeconds { get; }

    /// <summary>Times the output ran out of frames.</summary>
    int Underruns { get; }

    /// <summary>Play at <paramref name="rate"/> times the stream's rate (pitch follows).</summary>
    void SetRate(double rate);
}
