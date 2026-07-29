using System.Collections.Generic;
using System.Diagnostics;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class MovieOpcodeTests
{
    private sealed class FakeMovieDecoder : IMovieDecoder
    {
        public long? StopTimeMs { get; init; }
        public long InitialPositionMs { get; init; }
        public bool IsCompleted { get; set; }
        public string? Failure { get; set; }
        public bool Disposed { get; private set; }
        public RgbaImage? Frame { get; set; }

        public bool TryTakeFrame(out RgbaImage frame)
        {
            if (Frame == null) { frame = default!; return false; }
            frame = Frame;
            Frame = null;
            return true;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeMovieDecoderFactory(FakeMovieDecoder decoder) : IMovieDecoderFactory
    {
        public MoviePayload? OpenedPayload { get; private set; }
        public long OpenedInitialPositionMs { get; private set; }
        public long? OpenedPresentationDurationMs { get; private set; }
        public IMovieDecoder Open(
            MoviePayload movie, long initialPositionMs = 0, long? presentationDurationMs = null)
        {
            OpenedPayload = movie;
            OpenedInitialPositionMs = initialPositionMs;
            OpenedPresentationDurationMs = presentationDurationMs;
            return decoder;
        }
    }

    private sealed class FakeFfmpegFrameSource(FfmpegMovieInfo info,
                                                params FfmpegVideoFrame[] frames) : IFfmpegFrameSource
    {
        private readonly Queue<FfmpegVideoFrame> _frames = new(frames);
        private readonly Queue<FfmpegAudioChunk> _audio = new();
        private int _decodeCalls;
        public FfmpegMovieInfo Info { get; } = info;
        public int FailOnDecodeCall { get; init; } = -1;
        public int DecodeCalls => Volatile.Read(ref _decodeCalls);
        public long? SeekPositionMs { get; private set; }
        public bool Disposed { get; private set; }

        public void Seek(long positionMs) => SeekPositionMs = positionMs;

        public bool TryDecodeNextVideoFrame(out FfmpegVideoFrame frame)
        {
            int call = Interlocked.Increment(ref _decodeCalls) - 1;
            if (call == FailOnDecodeCall)
                throw new InvalidDataException("synthetic decode failure");
            return _frames.TryDequeue(out frame!);
        }
        public void EnqueueAudio(params FfmpegAudioChunk[] chunks)
        {
            foreach (FfmpegAudioChunk chunk in chunks) _audio.Enqueue(chunk);
        }
        public bool TryDecodeNextAudioChunk(out FfmpegAudioChunk chunk)
            => _audio.TryDequeue(out chunk!);

        public void Dispose() => Disposed = true;
    }

    private sealed class ManualMoviePacingClock : IMoviePacingClock, IDisposable
    {
        private readonly AutoResetEvent _advanced = new(false);
        private long _now;
        private long _waitingFor = -1;

        public bool WaitUntil(long elapsedMilliseconds, WaitHandle cancellation)
        {
            Interlocked.Exchange(ref _waitingFor, elapsedMilliseconds);
            while (Interlocked.Read(ref _now) < elapsedMilliseconds)
            {
                int signalled = WaitHandle.WaitAny(new[] { cancellation, _advanced });
                if (signalled == 0) return false;
            }
            return true;
        }

        public void AdvanceTo(long elapsedMilliseconds)
        {
            Interlocked.Exchange(ref _now, elapsedMilliseconds);
            _advanced.Set();
        }

        public bool WaitForDeadline(long elapsedMilliseconds) =>
            SpinWait.SpinUntil(() => Interlocked.Read(ref _waitingFor) == elapsedMilliseconds, 1000);

        public void Dispose() => _advanced.Dispose();
    }

    private static FfmpegVideoFrame SyntheticMovieFrame(byte value, long timestamp) =>
        new(new RgbaImage(1, 1, new[] { value, value, value, (byte)255 }), timestamp);

    [Fact]
    public void MovieRuntimeUsesInjectedDecoderAndRetainsSynchronousMetadata()
    {
        var decoder = new FakeMovieDecoder
        {
            StopTimeMs = 1876,
            Frame = new RgbaImage(1, 1, new byte[] { 1, 2, 3, 4 }),
        };
        var factory = new FakeMovieDecoderFactory(decoder);
        var payload = new MoviePayload("TEST.AGF", new byte[] { 0, 0, 1, 0xba });

        var runtime = MovieRuntime.Open("TEST.AGF", 7, 0x123, payload, factory);

        Assert.Same(payload, factory.OpenedPayload);
        Assert.Equal(0, factory.OpenedInitialPositionMs);
        Assert.Same(decoder, runtime.Decoder);
        Assert.Equal(0x123, runtime.ResourceId);
        Assert.Equal(0, runtime.InitialPositionMs);
        Assert.Equal(1876, runtime.Decoder.StopTimeMs);
        Assert.Equal(5000, runtime.WatchdogMs);
        Assert.True(runtime.Decoder.TryTakeFrame(out var frame));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, frame.Pixels);
        runtime.Decoder.Dispose();
        Assert.True(decoder.Disposed);
    }

    [Fact]
    public void MovieRuntimePassesInitialPositionAndBasesWatchdogOnRemainingDuration()
    {
        var decoder = new FakeMovieDecoder { StopTimeMs = 10000, InitialPositionMs = 9000 };
        var factory = new FakeMovieDecoderFactory(decoder);

        var runtime = MovieRuntime.Open("TEST.AGF", 7, 0x123,
            new MoviePayload("TEST.AGF", new byte[] { 0, 0, 1, 0xba }),
            factory, initialPositionMs: 9000);

        Assert.Equal(9000, factory.OpenedInitialPositionMs);
        Assert.Equal(9000, runtime.InitialPositionMs);
        Assert.Equal(5000, runtime.WatchdogMs);
        runtime.Decoder.Dispose();
    }

    [Theory]
    [InlineData(null, 30000)]
    [InlineData(-1L, 30000L)]
    [InlineData(10000L, 12000L)]
    [InlineData(500000L, 300000L)]
    public void MovieRuntimeComputesBoundedWatchdogFromDecoderMetadata(long? stopTimeMs, long expected)
    {
        var decoder = new FakeMovieDecoder { StopTimeMs = stopTimeMs };
        var runtime = MovieRuntime.Open("TEST.AGF", 7, 0x123,
            new MoviePayload("TEST.AGF", new byte[] { 0, 0, 1, 0xba }),
            new FakeMovieDecoderFactory(decoder));

        Assert.Equal(expected, runtime.WatchdogMs);
        runtime.Decoder.Dispose();
    }

    [Fact]
    public void FfmpegDecoderPublishesOnlyDueFramesAndCompletesAfterFinalInterval()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 100, 20, 1, false),
            SyntheticMovieFrame(1, 0), SyntheticMovieFrame(2, 50));
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(source, clock);

        RgbaImage? first = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            if (!decoder.TryTakeFrame(out var frame)) return false;
            first = frame;
            return true;
        }, 1000));
        Assert.Equal((byte)1, first!.Pixels[0]);
        Assert.True(clock.WaitForDeadline(50));
        Assert.False(decoder.TryTakeFrame(out _));

        clock.AdvanceTo(49);
        Assert.False(decoder.TryTakeFrame(out _));
        clock.AdvanceTo(50);
        RgbaImage? second = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            if (!decoder.TryTakeFrame(out var frame)) return false;
            second = frame;
            return true;
        }, 1000));
        Assert.Equal((byte)2, second!.Pixels[0]);

        Assert.True(clock.WaitForDeadline(100));
        clock.AdvanceTo(99);
        Assert.False(decoder.IsCompleted);
        clock.AdvanceTo(100);
        Assert.True(SpinWait.SpinUntil(() => decoder.IsCompleted, 1000));
        Assert.Null(decoder.Failure);
    }

    [Fact]
    public void FfmpegDecoderRetimeUsesRequestedDurationAndHoldsTerminalFrameToEndpoint()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 1000, 30, 1, false),
            SyntheticMovieFrame(1, 600), SyntheticMovieFrame(2, 900));
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(
            source, clock, presentationDurationMs: 500);

        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
        Assert.True(clock.WaitForDeadline(150)); // (900 - 600) * 500 / 1000
        clock.AdvanceTo(150);
        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
        Assert.True(clock.WaitForDeadline(500));
        clock.AdvanceTo(499);
        Assert.False(decoder.IsCompleted);
        clock.AdvanceTo(500);
        Assert.True(SpinWait.SpinUntil(() => decoder.IsCompleted, 1000));
    }

    [Fact]
    public void FfmpegDecoderPublishesAndRetainsFirstDecodedFrameBeforePacing()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 700, 30, 1, true, 1000, 2, 50),
            SyntheticMovieFrame(1, 600), SyntheticMovieFrame(2, 634));
        using var clock = new ManualMoviePacingClock();
        clock.AdvanceTo(1000);
        using var decoder = new FfmpegMovieDecoder(source, clock);

        Assert.True(SpinWait.SpinUntil(() => source.DecodeCalls == 1, 1000));
        Thread.Sleep(20);
        Assert.Equal(1, source.DecodeCalls);
        Assert.Equal(600, decoder.FirstFramePresentationTimeMs);
        Assert.True(decoder.TryTakeFrame(out var first));
        Assert.Equal((byte)1, first.Pixels[0]);

        Assert.True(SpinWait.SpinUntil(() => source.DecodeCalls >= 2, 1000));
        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
    }

    [Fact]
    public void FfmpegAudioBearingDecoderRebasesVideoPacingToFirstSourceTimestamp()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 700, 30, 1, true, 1000, 2, 50),
            SyntheticMovieFrame(1, 600), SyntheticMovieFrame(2, 634));
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(source, clock);

        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
        Assert.True(clock.WaitForDeadline(34));
        clock.AdvanceTo(33);
        Assert.False(decoder.TryTakeFrame(out _));
        clock.AdvanceTo(34);
        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
    }

    [Fact]
    public void FfmpegVideoOnlyDecoderRebasesPacingToFirstSourceTimestamp()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 700, 30, 1, false),
            SyntheticMovieFrame(1, 600), SyntheticMovieFrame(2, 634));
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(source, clock);

        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
        Assert.True(clock.WaitForDeadline(34));
        clock.AdvanceTo(33);
        Assert.False(decoder.TryTakeFrame(out _));
        clock.AdvanceTo(34);
        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));

        Assert.True(clock.WaitForDeadline(100));
        clock.AdvanceTo(100);
        Assert.True(SpinWait.SpinUntil(() => decoder.IsCompleted, 1000));
    }

    [Fact]
    public void FfmpegPositionedDecoderPublishesFrameActiveAtSeekThenContinuesPacing()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 120, 25, 1, false),
            SyntheticMovieFrame(1, 0),
            SyntheticMovieFrame(2, 40),
            SyntheticMovieFrame(3, 80));
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(source, clock, initialPositionMs: 60);

        Assert.Equal(60, source.SeekPositionMs);
        Assert.Equal(60, decoder.InitialPositionMs);
        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
        Assert.Equal(40, decoder.FirstFramePresentationTimeMs);
        Assert.True(clock.WaitForDeadline(40));
        clock.AdvanceTo(39);
        Assert.False(decoder.TryTakeFrame(out _));
        clock.AdvanceTo(40);
        RgbaImage? next = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            if (!decoder.TryTakeFrame(out var frame)) return false;
            next = frame;
            return true;
        }, 1000));
        Assert.Equal((byte)3, next!.Pixels[0]);
    }

    [Fact]
    public void FfmpegPositionedDecoderRetainsTerminalFrameAtStopTimeMinusOne()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 120, 25, 1, false),
            SyntheticMovieFrame(1, 0),
            SyntheticMovieFrame(2, 40),
            SyntheticMovieFrame(3, 80));
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(source, clock, initialPositionMs: 119);

        RgbaImage? terminal = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            if (!decoder.TryTakeFrame(out var frame)) return false;
            terminal = frame;
            return true;
        }, 1000));

        Assert.Equal((byte)3, terminal!.Pixels[0]);
        Assert.Equal(80, decoder.FirstFramePresentationTimeMs);
        Assert.True(clock.WaitForDeadline(40));
    }

    [Fact]
    public void FfmpegPositionedDecoderClampsInitialPositionToGraphBounds()
    {
        var belowStart = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 120, 25, 1, false),
            SyntheticMovieFrame(1, 0));
        using (var decoder = new FfmpegMovieDecoder(belowStart, null, initialPositionMs: -50))
        {
            Assert.Equal(0, decoder.InitialPositionMs);
            Assert.Null(belowStart.SeekPositionMs);
        }

        var beyondStop = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 120, 25, 1, false),
            SyntheticMovieFrame(1, 80));
        using (var decoder = new FfmpegMovieDecoder(beyondStop, null, initialPositionMs: 500))
        {
            Assert.Equal(119, decoder.InitialPositionMs);
            Assert.Equal(119, beyondStop.SeekPositionMs);
        }
    }

    [Fact]
    public void FfmpegPositionedDecoderTrimsAndRebasesAudioAtTheSamePosition()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 150, 10, 1, true, 1000, 2, 50),
            SyntheticMovieFrame(1, 0), SyntheticMovieFrame(2, 100));
        source.EnqueueAudio(
            new FfmpegAudioChunk(Enumerable.Range(0, 100).Select(value => (float)value).ToArray(),
                                 50, 50),
            new FfmpegAudioChunk(Enumerable.Repeat(0.25f, 100).ToArray(), 50, 100));
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(source, clock, initialPositionMs: 75);

        var chunks = new List<MovieAudioChunk>();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            while (decoder.TryTakeAudioChunk(out var chunk)) chunks.Add(chunk);
            return decoder.AudioDecodingCompleted && chunks.Count == 2;
        }, 1000));

        Assert.Equal(new[] { 25, 50 }, chunks.Select(chunk => chunk.FrameCount));
        Assert.Equal(new long[] { 0, 25 }, chunks.Select(chunk => chunk.PresentationTimeMs));
        Assert.Equal(50f, chunks[0].InterleavedStereo[0]);
    }

    [Fact]
    public void FfmpegDecoderDisposalInterruptsFutureFrameWait()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 60040, 25, 1, false),
            SyntheticMovieFrame(1, 0), SyntheticMovieFrame(2, 60000));
        using var clock = new ManualMoviePacingClock();
        var decoder = new FfmpegMovieDecoder(source, clock);
        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
        Assert.True(clock.WaitForDeadline(60000));

        var elapsed = Stopwatch.StartNew();
        decoder.Dispose();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        Assert.True(source.Disposed);
    }

    [Fact]
    public void FfmpegDecoderFailureCompletesInsteadOfStrandingMovieWait()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 100, 20, 1, false), SyntheticMovieFrame(1, 0))
        {
            FailOnDecodeCall = 1,
        };
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(source, clock);

        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
        Assert.True(SpinWait.SpinUntil(() => decoder.IsCompleted, 1000));
        Assert.Equal("synthetic decode failure", decoder.Failure);
    }

    [Fact]
    public void FfmpegAudioDecoderQueuesTimestampedPcmAndCompletesAfterSubmissionAndClock()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 100, 20, 1, true, 1000, 2, 50),
            SyntheticMovieFrame(1, 0), SyntheticMovieFrame(2, 50));
        source.EnqueueAudio(
            new FfmpegAudioChunk(new float[100], 50, 0),
            new FfmpegAudioChunk(Enumerable.Repeat(0.25f, 100).ToArray(), 50, 50));
        using var clock = new ManualMoviePacingClock();
        using var decoder = new FfmpegMovieDecoder(source, clock);

        var chunks = new List<MovieAudioChunk>();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            while (decoder.TryTakeAudioChunk(out var chunk)) chunks.Add(chunk);
            return decoder.AudioDecodingCompleted && chunks.Count == 2;
        }, 1000));
        Assert.Equal(new long[] { 0, 50 }, chunks.Select(chunk => chunk.PresentationTimeMs));
        Assert.Equal(100, chunks.Sum(chunk => chunk.FrameCount));

        Assert.True(SpinWait.SpinUntil(() => decoder.TryTakeFrame(out _), 1000));
        decoder.MarkAudioSubmitted();
        clock.AdvanceTo(99);
        Assert.False(decoder.IsCompleted);
        clock.AdvanceTo(100);
        Assert.True(SpinWait.SpinUntil(() => decoder.IsCompleted, 1000));
        Assert.Null(decoder.Failure);
    }

    [Fact]
    public void MovieAudioTimelineDoesNotSpliceContinuousMpegBlocksAtRoundedMillisecondTimestamps()
    {
        const int sampleRate = 44100;
        const int blockFrames = 1152;
        const int blockCount = 4093; // OP.AGF's installed MPEG audio block count
        long submittedFrames = 0;
        bool anchored = false;

        for (int block = 0; block < blockCount; block++)
        {
            long exactStartFrame = block * (long)blockFrames;
            long roundedDownTimestampMs = exactStartFrame * 1000 / sampleRate;
            long targetFrame = MovieAudioTimeline.PresentationFrame(
                roundedDownTimestampMs, sampleRate);
            MovieAudioAdjustment adjustment = MovieAudioTimeline.Align(
                submittedFrames, targetFrame, blockFrames, sampleRate, anchored);

            Assert.Equal(default, adjustment);
            submittedFrames += blockFrames;
            anchored = true;
        }

        Assert.Equal(blockCount * (long)blockFrames, submittedFrames);
    }

    [Fact]
    public void MovieAudioTimelineStillPreservesMaterialGapsAndOverlaps()
    {
        const int sampleRate = 44100;

        var initialGap = MovieAudioTimeline.Align(
            submittedFrames: 0,
            targetFrame: MovieAudioTimeline.PresentationFrame(100, sampleRate),
            chunkFrames: 1152,
            sampleRate: sampleRate,
            timelineAnchored: false);
        Assert.Equal(new MovieAudioAdjustment(4410, 0), initialGap);

        var laterGap = MovieAudioTimeline.Align(
            submittedFrames: 2304,
            targetFrame: MovieAudioTimeline.PresentationFrame(100, sampleRate),
            chunkFrames: 1152,
            sampleRate: sampleRate,
            timelineAnchored: true);
        Assert.Equal(new MovieAudioAdjustment(2106, 0), laterGap);

        var overlap = MovieAudioTimeline.Align(
            submittedFrames: 2304,
            targetFrame: MovieAudioTimeline.PresentationFrame(40, sampleRate),
            chunkFrames: 1152,
            sampleRate: sampleRate,
            timelineAnchored: true);
        Assert.Equal(new MovieAudioAdjustment(0, 540), overlap);
    }

    [Fact]
    public void InitialRootFlagStartsSetAndClearsWhenExitScriptRuns()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var root = ScriptAssembler.Assemble(table, "ROOT", new List<(int, Operand[])>
        {
            (0x130, new[] { new Operand(3, 0x100) }),
            (0x9, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var reloadedRoot = ScriptAssembler.Assemble(table, "SYSTEM4", new List<(int, Operand[])>
        {
            (0x130, new[] { new Operand(3, 0x101) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(root, table, host,
            provider: new MapProvider(new Dictionary<long, Script> { [0] = reloadedRoot }));

        vm.Run();
        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(0, vm.Globals[0x101]);
        Assert.Equal(1, host.SceneContextResets);
    }

    [Fact]
    public void ModalMovieDispatchesRawResourceSurfaceAndFlags()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MODAL", new List<(int, Operand[])>
        {
            (0x1f8, new[] { new Operand(0, 42), new Operand(0, 800), new Operand(0, 600), new Operand(0, 0) }),
            (0x20f, new[] { new Operand(0, 0x335f), new Operand(0, 42), new Operand(0, 4) }),
            (0x55, new[] { new Operand(3, 0x1234), new Operand(0, 0x5678) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(new[] { (0x335fL, 42, 4L) }, host.ModalMovies);
        Assert.Equal(0x5678, vm.Globals[0x1234]);
        vm.Gfx.BindDraw(1, 42, 0, 0, 1, 1, 0, 0);
        var visible = vm.Gfx.SnapshotVisibleObjects().Single();
        Assert.Equal(0, visible.SurfaceResId);
        Assert.Equal(-1, visible.ColorKey);
    }

    [Fact]
    public void Sc0000ResumesImmediatelyAfterMovieOpcodeAtBytecodeOffset13d1()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var provider = Sys4ScriptProvider.Load(table);
        var session = new GameSession();
        foreach (var name in new[] { "INITCONFIG.BIN", "INIT2.BIN", "INIT.BIN" })
            session.RunScene(Sys4Loader.Load(Paths.Scripts()[name], table), table, new CaptureHost(), provider: provider);
        var script = Sys4Loader.Load(Paths.Scripts()["SC0000.BIN"], table);
        var host = new RecordingHost();
        var trace = new RecordingTraceSink { TracingSteps = true };
        var vm = new VirtualMachine(script, table, host, new VmOptions(MaxSteps: 20_000_000), provider, trace);
        foreach (var kv in session.Globals) vm.Globals[kv.Key] = kv.Value;
        foreach (var kv in session.GlobalStrings) vm.GlobalStrings[kv.Key] = kv.Value;

        vm.Run();

        int movieStep = trace.Events.FindIndex(e => e.Kind == TraceEventKind.Step && e.Opcode == 0x236);
        Assert.True(movieStep >= 0);
        var nextStep = trace.Events.Skip(movieStep + 1).First(e => e.Kind == TraceEventKind.Step);
        Assert.Equal(0x13c8, trace.Events[movieStep].Ins!.Offset);
        Assert.Equal(0x13d1, nextStep.Ins!.Offset);
        Assert.Contains((0x33L, 0, 2L, 0L), host.Movies);
    }

    [Fact]
    public void PlayMovieDispatchesExactOperandsAndResumesAtNextInstruction()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MOVIE", new List<(int, Operand[])>
        {
            (0x236, new[] { new Operand(0, 0x33), new Operand(0, 5), new Operand(0, 2), new Operand(0, 0) }),
            (0x55, new[] { new Operand(3, 0x1234), new Operand(0, 0x5678) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0x5678, vm.Globals[0x1234]);
        Assert.Equal(new[] { (0x33L, 5, 2L, 0L) }, host.Movies);
        vm.Gfx.BindDraw(1, 5, 0, 0, 1, 1, 0, 0);
        var visible = vm.Gfx.SnapshotVisibleObjects().Single();
        Assert.Equal(0, visible.SurfaceResId);
        Assert.Equal(-1, visible.ColorKey);
    }

    [Fact]
    public void PositionedMovieDispatchesExactOperandsAndRetainsGraphStopTime()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "POSITIONED-MOVIE",
        [
            (0x241,
            [
                new Operand(0, 0x33), new Operand(0, 5), new Operand(0, 2),
                new Operand(0, 0), new Operand(0, 1875),
            ]),
            (0x23f, [new Operand(3, 0x1234), new Operand(0, 5)]),
            (0x55, [new Operand(3, 0x1235), new Operand(0, 0x5678)]),
            (0x2, Array.Empty<Operand>()),
        ], []);
        var host = new RecordingHost { MovieStopTimeMs = 1876 };
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(new[] { (0x33L, 5, 2L, 0L, 1875L) }, host.PositionedMovies);
        Assert.Empty(host.Movies);
        Assert.Equal(1876, vm.Globals[0x1234]);
        Assert.Equal(0x5678, vm.Globals[0x1235]);
    }

    [Fact]
    public void MovieMaskTransitionDispatchesAllTwelveOperandsAndResumes()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MOVIE-MASK",
        [
            (0x24d,
            [
                new Operand(0, 11), new Operand(0, 45),
                new Operand(0, 10), new Operand(0, 1),
                new Operand(0, unchecked((uint)-184)), new Operand(0, 0),
                new Operand(0, 800), new Operand(0, 600),
                new Operand(0, 0), new Operand(0, 0x325e),
                new Operand(0, 0), new Operand(0, 1000),
            ]),
            (0x55, [new Operand(3, 0x1234), new Operand(0, 0x5678)]),
            (0x2, Array.Empty<Operand>()),
        ], []);
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0x5678, vm.Globals[0x1234]);
        Assert.Equal(new MovieMaskTransitionRequest(
            11, 45, 10, 1, -184, 0, 800, 600, 0, 0x325e, 0, 1000),
            host.MovieMaskTransitions.Single());
        Assert.True(vm.Gfx.SnapshotMovieMaskTransitions().Single().Completed);
    }

    [Fact]
    public void PlayMovieKeepsCreatedSurfaceBlankDuringSynchronousHostSetup()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MOVIE-PREROLL", new List<(int, Operand[])>
        {
            (0x1f8, new[] { new Operand(0, 5), new Operand(0, 800), new Operand(0, 600), new Operand(0, 0) }),
            (0x1fb, new[]
            {
                new Operand(0, 1), new Operand(0, 5), new Operand(0, 0), new Operand(0, 0),
                new Operand(0, 800), new Operand(0, 600), new Operand(0, 0), new Operand(0, 0),
            }),
            (0x236, new[] { new Operand(0, 0x33), new Operand(0, 5), new Operand(0, 2), new Operand(0, 0) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost();
        VirtualMachine? vm = null;
        long resourceDuringSetup = -1;
        host.OnPlayMovie = () => resourceDuringSetup = vm!.Gfx.SnapshotVisibleObjects().Single().SurfaceResId;
        vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(0, resourceDuringSetup);
        var visible = vm.Gfx.SnapshotVisibleObjects().Single();
        Assert.Equal(0, visible.SurfaceResId);
        Assert.Equal(-1, visible.ColorKey);
    }

    [Fact]
    public void QueryMovieStopTimeReturnsTheValueRetainedByPlayMovie()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MOVIE-TIME", new List<(int, Operand[])>
        {
            (0x236, new[] { new Operand(0, 0x33), new Operand(0, 5), new Operand(0, 2), new Operand(0, 0) }),
            (0x23f, new[] { new Operand(3, 0x1234), new Operand(0, 5) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost { MovieStopTimeMs = 1876 };
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(1876, vm.Globals[0x1234]);
        Assert.True(vm.Gfx.TryGetMovieStopTime(5, out long? retained));
        Assert.Equal(1876, retained);
        Assert.Empty(host.Warnings);

        vm.Gfx.SetSurface(5, 0x34, 0); // replacing the native surface tears down its movie metadata
        Assert.False(vm.Gfx.TryGetMovieStopTime(5, out _));
    }

    [Fact]
    public void QueryMovieStopTimeReturnsMinusOneWithoutWarningForAnEmptyMovieSlot()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "EMPTY-MOVIE-TIME", new List<(int, Operand[])>
        {
            (0x23f, new[] { new Operand(3, 0x1234), new Operand(0, 5) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);
        vm.Gfx.GetOrCreate(5); // retained-object existence is unrelated to the native movie-object slot

        vm.Run();

        Assert.Equal(-1, vm.Globals[0x1234]);
        Assert.Empty(host.Warnings);
    }

    [Fact]
    public void LoadedMovieWithoutBackendMetadataUsesImmediateZeroDuration()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "MISSING-MOVIE-TIME", new List<(int, Operand[])>
        {
            (0x236, new[] { new Operand(0, 0x33), new Operand(0, 5), new Operand(0, 2), new Operand(0, 0) }),
            (0x23f, new[] { new Operand(3, 0x1234), new Operand(0, 5) }),
            (0x23a, new[] { new Operand(3, 0x1235), new Operand(0, 5) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost();
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(0, vm.Globals[0x1234]);
        Assert.True(vm.Gfx.TryGetMovieStopTime(5, out long? retained));
        Assert.Equal(0, retained);
        Assert.Equal(0, vm.Globals[0x1235]);
        Assert.Empty(host.Warnings);
    }

    [Fact]
    public void Sc0000MoviePayloadReadsFromArchiveVfsAndIsMpegProgramStream()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var entry = resources.ResolveMovie(0x33);

        Assert.Equal("CHAPTER.AGF", entry?.Name);
        var movie = resources.ReadMovie(entry!);
        Assert.Equal(new byte[] { 0, 0, 1, 0xba }, movie.Bytes[..4]);
        Assert.Equal(8_194_052, movie.Bytes.Length);
    }

    [Theory]
    [InlineData(0x335f, "LOGO.AGF")]
    [InlineData(0x3364, "OP.AGF")]
    public void ModalMoviePayloadResolvesFromUniversalPackedCatalog(int resourceId, string expectedName)
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var entry = resources.ResolveMovie(resourceId);

        Assert.Equal(expectedName, entry?.Name);
        var movie = resources.ReadMovie(entry!);
        Assert.Equal(new byte[] { 0, 0, 1, 0xba }, movie.Bytes[..4]);
    }

    [Fact]
    public void DebugTestMovieDecodesToExactGreenMaskDimensions()
    {
        if (!OperatingSystem.IsWindows()) return;
        ConfigureFfmpegNativeProbe();
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var payload = resources.ReadMovie(resources.ResolveMovie(0x325e)!);

        using var movie = new FfmpegMovieSession(payload);
        Assert.Equal("TEST.AGF", payload.Name);
        Assert.Equal(800, movie.Info.Width);
        Assert.Equal(600, movie.Info.Height);
        Assert.Equal(1000, movie.Info.StopTimeMs);
        Assert.False(movie.Info.HasAudio);
        Assert.True(movie.TryDecodeNextVideoFrame(out var frame));

        byte[] mask = MovieMaskSurface.ExtractGreen(frame.Image, 800, 600, 255);
        Assert.Equal(800 * 600, mask.Length);
        Assert.True(mask.Distinct().Skip(1).Any(), "TEST.AGF should publish a nonuniform green mask");
    }

    [Theory]
    [InlineData(0x335f, "LOGO.AGF")]
    [InlineData(0x3364, "OP.AGF")]
    public void ModalMovieOpeningFramesAreOpaqueAndHaveContinuousCadence(
        int resourceId, string expectedName)
    {
        if (!OperatingSystem.IsWindows()) return;
        ConfigureFfmpegNativeProbe();
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var payload = resources.ReadMovie(resources.ResolveMovie(resourceId)!);

        using var movie = new FfmpegMovieSession(payload);
        Assert.Equal(expectedName, payload.Name);
        long priorTimestamp = -1;
        byte[]? firstPixels = null;
        bool openingChanged = false;
        for (int frameIndex = 0; frameIndex < 20; frameIndex++)
        {
            Assert.True(movie.TryDecodeNextVideoFrame(out var frame),
                $"{expectedName} ended before opening frame {frameIndex}");
            if (priorTimestamp >= 0)
                Assert.InRange(frame.PresentationTimeMs - priorTimestamp, 33, 34);
            priorTimestamp = frame.PresentationTimeMs;
            firstPixels ??= frame.Image.Pixels;
            openingChanged |= !firstPixels.AsSpan().SequenceEqual(frame.Image.Pixels);
            for (int alpha = 3; alpha < frame.Image.Pixels.Length; alpha += 4)
                Assert.Equal((byte)255, frame.Image.Pixels[alpha]);
        }
        Assert.True(openingChanged, $"{expectedName} should change within its first 20 decoded frames");
    }

    [Theory]
    [InlineData(0x2be3, "MVB961.AGF", 280, 352, 500)]
    [InlineData(0x2b94, "MVB238.AGF", 280, 352, 866)]
    [InlineData(0x2bc2, "MVB908.AGF", 400, 400, 333)]
    [InlineData(0x33, "CHAPTER.AGF", 800, 600, 12016)]
    public void FfmpegShimDecodesRepresentativeVfsMovie(int resourceId, string expectedName,
                                                        int expectedWidth, int expectedHeight,
                                                        long expectedStopTimeMs)
    {
        if (!OperatingSystem.IsWindows()) return;
        ConfigureFfmpegNativeProbe();
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var payload = resources.ReadMovie(resources.ResolveMovie(resourceId)!);

        using var movie = new FfmpegMovieSession(payload);

        Assert.Equal(expectedName, payload.Name);
        Assert.Equal(expectedWidth, movie.Info.Width);
        Assert.Equal(expectedHeight, movie.Info.Height);
        Assert.Equal(expectedStopTimeMs, movie.Info.StopTimeMs);
        Assert.True(movie.TryDecodeNextVideoFrame(out var first));
        Assert.Equal(expectedWidth * expectedHeight * 4, first.Image.Pixels.Length);
        Assert.True(first.PresentationTimeMs >= 0);
        bool changed = false;
        long priorTimestamp = first.PresentationTimeMs;
        for (int frameIndex = 0; frameIndex < 30 && movie.TryDecodeNextVideoFrame(out var later); frameIndex++)
        {
            Assert.True(later.PresentationTimeMs >= priorTimestamp);
            priorTimestamp = later.PresentationTimeMs;
            if (!first.Image.Pixels.AsSpan().SequenceEqual(later.Image.Pixels))
            {
                changed = true;
                break;
            }
        }
        Assert.True(changed, $"{expectedName} should deliver changing decoded frames");
    }

    [Theory]
    [InlineData(0x33, "CHAPTER.AGF")]
    [InlineData(0x2bf1, "MVS001.AGF")]
    [InlineData(0x335f, "LOGO.AGF")]
    public void FfmpegShimDecodesRepresentativeMpegAudio(int resourceId, string expectedName)
    {
        if (!OperatingSystem.IsWindows()) return;
        ConfigureFfmpegNativeProbe();
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var payload = resources.ReadMovie(resources.ResolveMovie(resourceId)!);

        using var movie = new FfmpegMovieSession(payload);

        Assert.Equal(expectedName, payload.Name);
        Assert.True(movie.Info.HasAudio);
        Assert.Equal(44100, movie.Info.AudioSampleRate);
        Assert.Equal(2, movie.Info.AudioChannels);
        Assert.True(movie.TryDecodeNextAudioChunk(out var first));
        Assert.True(first.FrameCount > 0);
        Assert.Equal(first.FrameCount * 2, first.InterleavedStereo.Length);
        Assert.True(first.PresentationTimeMs >= 0);
        Assert.All(first.InterleavedStereo, sample => Assert.True(float.IsFinite(sample)));
        bool heardSignal = first.InterleavedStereo.Any(sample => Math.Abs(sample) > 0.00001f);
        long priorTimestamp = first.PresentationTimeMs;
        for (int block = 0; block < 500 && !heardSignal
             && movie.TryDecodeNextAudioChunk(out var later); block++)
        {
            Assert.True(later.PresentationTimeMs >= priorTimestamp);
            Assert.All(later.InterleavedStereo, sample => Assert.True(float.IsFinite(sample)));
            priorTimestamp = later.PresentationTimeMs;
            heardSignal = later.InterleavedStereo.Any(sample => Math.Abs(sample) > 0.00001f);
        }
        Assert.True(heardSignal, $"{expectedName} should contain non-silent MPEG audio");
    }

    [Fact]
    public void FfmpegShimSeeksBothVideoAndAudioNearRequestedPosition()
    {
        if (!OperatingSystem.IsWindows()) return;
        ConfigureFfmpegNativeProbe();
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var payload = resources.ReadMovie(resources.ResolveMovie(0x33)!); // CHAPTER.AGF
        const long targetMs = 11000;

        using var movie = new FfmpegMovieSession(payload);
        movie.Seek(targetMs);

        long videoTimestamp = -1;
        for (int frame = 0; frame < 120 && videoTimestamp < targetMs; frame++)
        {
            Assert.True(movie.TryDecodeNextVideoFrame(out var decoded));
            videoTimestamp = decoded.PresentationTimeMs;
        }
        Assert.InRange(videoTimestamp, targetMs, movie.Info.StopTimeMs);

        long audioEndTimestamp = -1;
        for (int chunk = 0; chunk < 100 && audioEndTimestamp < targetMs; chunk++)
        {
            Assert.True(movie.TryDecodeNextAudioChunk(out var decoded));
            audioEndTimestamp = decoded.PresentationTimeMs
                                + decoded.FrameCount * 1000L / movie.Info.AudioSampleRate;
        }
        Assert.InRange(audioEndTimestamp, targetMs, movie.Info.StopTimeMs + 100);
    }

    [Fact]
    public void FfmpegShimRejectsTruncatedMovieWithBoundedDiagnostic()
    {
        if (!OperatingSystem.IsWindows()) return;
        ConfigureFfmpegNativeProbe();
        var payload = new MoviePayload("TRUNCATED.AGF", new byte[] { 0, 0, 1, 0xba, 0x21, 0, 1, 0 });

        var error = Assert.Throws<InvalidDataException>(() => new FfmpegMovieSession(payload));

        Assert.Contains("TRUNCATED.AGF", error.Message);
        Assert.Contains("FFmpeg", error.Message);
    }

    [Fact]
    public void FfmpegShimSupportsRepeatedOpenAndClose()
    {
        if (!OperatingSystem.IsWindows()) return;
        ConfigureFfmpegNativeProbe();
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var payload = resources.ReadMovie(resources.ResolveMovie(0x2bc2)!);

        for (int iteration = 0; iteration < 10; iteration++)
        {
            using var movie = new FfmpegMovieSession(payload);
            Assert.True(movie.TryDecodeNextVideoFrame(out _));
        }
    }

    [Theory]
    [InlineData(0x2be3, 280, 500, 450)]
    [InlineData(0x2bc2, 400, 333, 300)]
    public void FfmpegPacedDecoderKeepsRealMovieAliveThroughItsStopTime(
        int resourceId, int expectedWidth, long expectedStopTimeMs, long minimumElapsedMs)
    {
        if (!OperatingSystem.IsWindows()) return;
        ConfigureFfmpegNativeProbe();
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var payload = resources.ReadMovie(resources.ResolveMovie(resourceId)!);
        var elapsed = Stopwatch.StartNew();
        using var decoder = new FfmpegMovieDecoder(payload);

        RgbaImage? first = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            if (!decoder.TryTakeFrame(out var frame)) return false;
            first = frame;
            return true;
        }, 2000));
        Assert.Equal(expectedWidth, first!.Width);
        Assert.Equal(expectedStopTimeMs, decoder.StopTimeMs);
        Assert.False(decoder.IsCompleted);
        Assert.True(SpinWait.SpinUntil(() => decoder.IsCompleted, 2000));
        Assert.True(elapsed.ElapsedMilliseconds >= minimumElapsedMs,
            $"{expectedStopTimeMs} ms movie completed after only {elapsed.ElapsedMilliseconds} ms");
        Assert.Null(decoder.Failure);
        Assert.True(decoder.TryTakeFrame(out var final));
        Assert.Equal(expectedWidth, final.Width);
    }

    private static void ConfigureFfmpegNativeProbe()
    {
        string nativeDirectory = Environment.GetEnvironmentVariable("AGE_FFMPEG_NATIVE_DIR")
            ?? Path.Combine(Paths.Build, "native", "win-x64");
        Assert.True(File.Exists(Path.Combine(nativeDirectory, "age_movie_ffmpeg.dll")),
            $"Build the FFmpeg shim first: native/age_movie_ffmpeg/build-win64.ps1 (expected {nativeDirectory})");
        Environment.SetEnvironmentVariable("AGE_FFMPEG_NATIVE_DIR", nativeDirectory);
    }
}
