using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Devices;
using Ome.Engine.Diagnostics;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;

/// <summary>A decoder a test scripts: its stop time, the frame it hands out next, its sound, and when it ends. It
/// records the rates, clock advances and end-of-sound marks the movie native gives it.</summary>
internal sealed class FakeMovieDecoder : IMovieDecoder
{
    public long? StopTimeMs { get; init; }
    public long InitialPositionMs { get; init; }
    public bool IsCompleted { get; set; }
    public string? Failure { get; set; }
    public bool Disposed { get; private set; }
    public RgbaImage? Frame { get; set; }
    public MovieAudioInfo? AudioInfo { get; init; }
    public bool AudioDecodingCompleted { get; set; } = true;
    public readonly Queue<MovieAudioChunk> AudioChunks = new();
    public readonly List<double> Rates = new();
    public readonly List<long> ClockAdvances = new();
    public int AudioSubmittedMarks { get; private set; }

    public bool TryTakeFrame(out RgbaImage frame)
    {
        if (Frame == null) { frame = default!; return false; }
        frame = Frame;
        Frame = null;
        return true;
    }

    public bool TryTakeAudioChunk(out MovieAudioChunk chunk) => AudioChunks.TryDequeue(out chunk!);

    public void AdvancePlaybackClock(long elapsedMilliseconds) => ClockAdvances.Add(elapsedMilliseconds);

    public void MarkAudioSubmitted() => AudioSubmittedMarks++;

    public void SetPlaybackRate(double rate) => Rates.Add(rate);

    public void Dispose() => Disposed = true;
}

/// <summary>The front end's decoder for a test: each open makes the next decoder <paramref name="next"/> gives, and
/// records what it was asked to open.</summary>
internal sealed class FakeMovieDecoderFactory(Func<string, FakeMovieDecoder> next) : IMovieDecoderFactory
{
    public FakeMovieDecoderFactory(FakeMovieDecoder decoder) : this(_ => decoder) { }

    public readonly List<FakeMovieDecoder> Decoders = new();
    public string? OpenedName { get; private set; }
    public byte[]? OpenedBytes { get; private set; }
    public long OpenedInitialPositionMs { get; private set; }
    public long? OpenedPresentationDurationMs { get; private set; }
    /// <summary>Thrown by the next open, as a decoder that cannot read the file does.</summary>
    public Exception? Failure { get; set; }
    /// <summary>Called before each open returns.</summary>
    public Action<string, long>? OnOpen { get; set; }

    public IMovieDecoder Open(
        string name, byte[] bytes, long initialPositionMs = 0, long? presentationDurationMs = null)
    {
        if (Failure != null) throw Failure;
        OpenedName = name;
        OpenedBytes = bytes;
        OpenedInitialPositionMs = initialPositionMs;
        OpenedPresentationDurationMs = presentationDurationMs;
        FakeMovieDecoder decoder = next(name);
        Decoders.Add(decoder);
        OnOpen?.Invoke(name, initialPositionMs);
        return decoder;
    }
}

/// <summary>Every packed id names a movie <c>Mxxxx.AGF</c> whose file is an MPEG pack header, except the ids in
/// <see cref="Unresolved"/>.</summary>
internal sealed class FakeMovieAssets : IMovieAssets
{
    public readonly HashSet<long> Unresolved = new();

    public static string NameOf(long resourceId) => $"M{resourceId:X4}.AGF";

    public AssetEntry? Resolve(long resourceId) => Unresolved.Contains(resourceId)
        ? null
        : new AssetEntry(NameOf(resourceId), "TEST.ALF", 0, 4, RawIndex: (int)(resourceId & 0xffffff));

    public MoviePayload Read(AssetEntry movie) => new(movie.Name, [0, 0, 1, 0xba]);
}

/// <summary>A stream that records what is queued on it; a test sets what has been heard.</summary>
internal sealed class FakeAudioStream(int sampleRate, AudioBus bus, bool muted) : IAudioStream
{
    public int SampleRate { get; } = sampleRate;
    public AudioBus Bus { get; } = bus;
    public bool Muted { get; } = muted;
    public readonly List<string> Calls = new();
    public long QueuedFrames { get; private set; }
    public int FramesFree { get; set; } = 1 << 20;
    public double HeardSeconds { get; set; }
    public int Underruns { get; set; }
    public bool Held { get; private set; }
    public bool Disposed { get; private set; }

    public void StartHeld() { Held = true; Calls.Add("start-held"); }

    public void Resume() { Held = false; Calls.Add("resume"); }

    public bool Push(float[] interleaved, int offsetFrames, int frames)
    {
        Calls.Add($"push {offsetFrames}+{frames}");
        QueuedFrames += frames;
        return true;
    }

    public bool PushSilence(int frames)
    {
        Calls.Add($"silence {frames}");
        QueuedFrames += frames;
        return true;
    }

    public void SetRate(double rate) => Calls.Add($"rate {rate}");

    public void Dispose() => Disposed = true;
}

internal sealed class FakeAudioStreamDevice : IAudioStreamDevice
{
    public readonly List<FakeAudioStream> Streams = new();

    public IAudioStream Open(int sampleRate, AudioBus bus, bool muted)
    {
        var stream = new FakeAudioStream(sampleRate, bus, muted);
        Streams.Add(stream);
        return stream;
    }
}

/// <summary>A movie native over fakes, on an 800x600 graphics native: a manual clock, movie files for every id, a
/// scripted decoder and a stream device. <see cref="ServiceFrame"/> runs the front end's frame service.</summary>
internal sealed class MovieRig
{
    /// <summary>One movie the native opened a decoder for, as an op asked for it.</summary>
    public readonly record struct Start(long Resource, int Surface, long Flags, long StartDelayMs, long PositionMs);

    private readonly List<(long PlaybackId, int Surface, long PositionMs)> _opens = new();
    private long _renderFrame;

    public ManualPlayClock Time { get; } = new();
    public FakeMovieAssets Assets { get; } = new();
    public FakeMovieDecoderFactory Decoders { get; }
    public FakeAudioStreamDevice Sound { get; } = new();
    public Graphics Graphics { get; }
    public Movies Movies { get; }
    public List<string> Log { get; } = new();
    public int Wakes { get; private set; }

    /// <param name="decoder">The decoder each open makes; by default one with a 1000 ms stop time.</param>
    /// <param name="dependMovieSound">SYS4INI DEPENDMOVIESOUND.</param>
    public MovieRig(Func<string, FakeMovieDecoder>? decoder = null, int dependMovieSound = 0)
    {
        Decoders = new FakeMovieDecoderFactory(decoder ?? (_ => new FakeMovieDecoder { StopTimeMs = 1000 }));
        Graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(800, 600));
        Movies = new Movies(Graphics, Assets, Decoders, Sound, dependMovieSound, Time, print: Log.Add);
        Movies.WakeWaitsWith(() => Wakes++);
        // The binding exists before the decoder opens (Movies.StartMovie), so the newest one is this open's.
        Decoders.OnOpen = (_, positionMs) =>
        {
            MovieSurfaceDiagnostic bound = Movies.SurfaceDiagnostics().MaxBy(surface => surface.PlaybackId)!;
            _opens.Add((bound.PlaybackId, bound.SurfaceSlot, positionMs));
        };
    }

    /// <summary>A VM whose ops reach this rig's graphics and movie natives.</summary>
    public VirtualMachine Vm(Script script, OpcodeTable table, IHost host, VmOptions? options = null,
                             IScriptProvider? provider = null, ITraceSink? sink = null)
        => new(script, table, host, options, provider, sink, graphics: Graphics, movies: Movies);

    /// <summary>The movies opened so far, in order, with the flags and delay the native retained.</summary>
    public IReadOnlyList<Start> Starts()
    {
        MoviePlaybackDiagnostics diagnostics = Movies.PlaybackDiagnostics();
        var retained = diagnostics.Opened.Select(m => (m.PlaybackId, m.ResourceId, m.MovieFlags, m.StartDelayMs))
            .Concat(diagnostics.Playing.Select(m => (m.PlaybackId, m.ResourceId, m.MovieFlags, m.StartDelayMs)))
            .ToDictionary(m => m.PlaybackId);
        return _opens.Select(open =>
        {
            var movie = retained[open.PlaybackId];
            return new Start(movie.ResourceId, open.Surface, movie.MovieFlags, movie.StartDelayMs, open.PositionMs);
        }).ToArray();
    }

    /// <summary>Run the front end's frame service once.</summary>
    public void ServiceFrame() => Movies.ServiceFrame(++_renderFrame);

    /// <summary>Move the clock by <paramref name="ms"/>, then run a frame service.</summary>
    public void Advance(long ms)
    {
        Time.NowMs += ms;
        ServiceFrame();
    }
}
