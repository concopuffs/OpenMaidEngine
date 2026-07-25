using Age.Engine.Sys4;

public class MovieCorpusGateTests
{
    private sealed class FakeFrameSource(FfmpegMovieInfo info,
                                         params FfmpegVideoFrame[] frames) : IFfmpegFrameSource
    {
        private readonly Queue<FfmpegVideoFrame> _frames = new(frames);
        private readonly Queue<FfmpegAudioChunk> _audio = new();
        public FfmpegMovieInfo Info { get; } = info;
        public bool Disposed { get; private set; }
        public bool TryDecodeNextVideoFrame(out FfmpegVideoFrame frame)
            => _frames.TryDequeue(out frame!);
        public void EnqueueAudio(params FfmpegAudioChunk[] chunks)
        {
            foreach (FfmpegAudioChunk chunk in chunks) _audio.Enqueue(chunk);
        }
        public bool TryDecodeNextAudioChunk(out FfmpegAudioChunk chunk)
            => _audio.TryDequeue(out chunk!);
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void SequenceHeaderProbeReadsIndependentDimensions()
    {
        byte[] payload = SyntheticPayload(320, 240);

        Assert.True(MovieCorpusDiscovery.TryReadSequenceDimensions(payload, out int width, out int height));
        Assert.Equal(320, width);
        Assert.Equal(240, height);
        Assert.False(MovieCorpusDiscovery.TryReadSequenceDimensions(new byte[] { 0, 0, 1, 0xba }, out _, out _));
    }

    [Fact]
    public void GateReportsFramesMetadataTimingAndTeardown()
    {
        var source = new FakeFrameSource(new FfmpegMovieInfo(16, 16, 100, 20, 1, false),
            Frame(16, 16, 1, 0), Frame(16, 16, 2, 50));
        var input = new MovieCorpusInput(0x2bc2, "SYNTH.AGF", () =>
            new MoviePayload("SYNTH.AGF", SyntheticPayload(16, 16)));

        MovieCorpusReport report = new MovieCorpusGate(_ => source).Run([input], 1, 1000);

        Assert.True(report.Passed);
        var item = Assert.Single(report.Movies);
        Assert.True(item.Passed);
        Assert.Equal("0x2bc2", item.PackedIdHex);
        Assert.Equal(16, item.ExpectedWidth);
        Assert.Equal(2, item.FrameCount);
        Assert.Equal(0, item.FirstPresentationTimeMs);
        Assert.Equal(50, item.LastPresentationTimeMs);
        Assert.True(item.FramesChanged);
        Assert.True(source.Disposed);
    }

    [Fact]
    public void GateContinuesAfterValidationFailureAndReportsCountMismatch()
    {
        var badDimensions = new FakeFrameSource(new FfmpegMovieInfo(8, 16, 100, 20, 1, false),
            Frame(8, 16, 1, 0));
        var decreasingTimestamps = new FakeFrameSource(new FfmpegMovieInfo(16, 16, 100, 20, 1, false),
            Frame(16, 16, 1, 50), Frame(16, 16, 2, 40));
        var sources = new Queue<IFfmpegFrameSource>([badDimensions, decreasingTimestamps]);
        MovieCorpusInput[] inputs =
        [
            new(1, "BAD-DIMS.AGF", () => new MoviePayload("BAD-DIMS.AGF", SyntheticPayload(16, 16))),
            new(2, "BAD-PTS.AGF", () => new MoviePayload("BAD-PTS.AGF", SyntheticPayload(16, 16))),
        ];

        MovieCorpusReport report = new MovieCorpusGate(_ => sources.Dequeue()).Run(inputs, 3, 1000);

        Assert.False(report.Passed);
        Assert.Equal(2, report.FailedCount);
        Assert.Contains("expected 3", Assert.Single(report.SelectionErrors));
        Assert.Contains("differ", report.Movies[0].Error);
        Assert.Contains("timestamp", report.Movies[1].Error);
        Assert.True(badDimensions.Disposed);
        Assert.True(decreasingTimestamps.Disposed);
    }

    [Fact]
    public void GateRejectsInvalidFrameRateMetadata()
    {
        var source = new FakeFrameSource(new FfmpegMovieInfo(16, 16, 100, 0, 1, false),
            Frame(16, 16, 1, 0));
        var input = new MovieCorpusInput(1, "BAD-RATE.AGF", () =>
            new MoviePayload("BAD-RATE.AGF", SyntheticPayload(16, 16)));

        MovieCorpusReport report = new MovieCorpusGate(_ => source).Run([input], 1, 1000);

        Assert.False(report.Passed);
        Assert.Contains("rate 0/1", Assert.Single(report.Movies).Error);
        Assert.True(source.Disposed);
    }

    [Fact]
    public void GateValidatesAndReportsTimestampedStereoPcm()
    {
        var source = new FakeFrameSource(
            new FfmpegMovieInfo(16, 16, 100, 20, 1, true, 1000, 2, 50),
            Frame(16, 16, 1, 0), Frame(16, 16, 2, 50));
        source.EnqueueAudio(
            new FfmpegAudioChunk(new float[100], 50, 0),
            new FfmpegAudioChunk(Enumerable.Repeat(0.5f, 100).ToArray(), 50, 50));
        var input = new MovieCorpusInput(1, "AUDIO.AGF", () =>
            new MoviePayload("AUDIO.AGF", SyntheticPayload(16, 16)));

        MovieCorpusReport report = new MovieCorpusGate(_ => source).Run([input], 1, 1000);

        var item = Assert.Single(report.Movies);
        Assert.True(item.Passed);
        Assert.True(item.HasAudio);
        Assert.Equal(1000, item.AudioSampleRate);
        Assert.Equal(2, item.AudioChannels);
        Assert.Equal(2, item.AudioBlockCount);
        Assert.Equal(100, item.AudioFrameCount);
        Assert.Equal(0, item.FirstAudioPresentationTimeMs);
        Assert.Equal(50, item.LastAudioPresentationTimeMs);
        Assert.True(item.AudioHasSignal);
    }

    private static byte[] SyntheticPayload(int width, int height) =>
    [
        0, 0, 1, 0xba,
        0, 0, 1, 0xb3,
        (byte)(width >> 4),
        (byte)(((width & 0x0f) << 4) | (height >> 8)),
        (byte)height,
        0,
    ];

    private static FfmpegVideoFrame Frame(int width, int height, byte value, long timestamp)
        => new(new RgbaImage(width, height,
            Enumerable.Repeat(value, checked(width * height * 4)).ToArray()), timestamp);
}
