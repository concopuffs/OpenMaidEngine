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
        public IMovieDecoder Open(MoviePayload movie)
        {
            OpenedPayload = movie;
            return decoder;
        }
    }

    private sealed class FakeFfmpegFrameSource(FfmpegMovieInfo info,
                                                params FfmpegVideoFrame[] frames) : IFfmpegFrameSource
    {
        private readonly Queue<FfmpegVideoFrame> _frames = new(frames);
        private int _decodeCalls;
        public FfmpegMovieInfo Info { get; } = info;
        public int FailOnDecodeCall { get; init; } = -1;
        public bool Disposed { get; private set; }

        public bool TryDecodeNextVideoFrame(out FfmpegVideoFrame frame)
        {
            if (_decodeCalls++ == FailOnDecodeCall)
                throw new InvalidDataException("synthetic decode failure");
            return _frames.TryDequeue(out frame!);
        }

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
        Assert.Same(decoder, runtime.Decoder);
        Assert.Equal(0x123, runtime.ResourceId);
        Assert.Equal(1876, runtime.Decoder.StopTimeMs);
        Assert.Equal(5000, runtime.WatchdogMs);
        Assert.True(runtime.Decoder.TryTakeFrame(out var frame));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, frame.Pixels);
        runtime.Decoder.Dispose();
        Assert.True(decoder.Disposed);
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
    public void FfmpegDecoderDisposalInterruptsFutureFrameWait()
    {
        var source = new FakeFfmpegFrameSource(
            new FfmpegMovieInfo(1, 1, 60040, 25, 1, false),
            SyntheticMovieFrame(1, 0), SyntheticMovieFrame(2, 60000));
        using var clock = new ManualMoviePacingClock();
        var decoder = new FfmpegMovieDecoder(source, clock);
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

        Assert.True(SpinWait.SpinUntil(() => decoder.IsCompleted, 1000));
        Assert.Equal("synthetic decode failure", decoder.Failure);
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
        Assert.Equal(0x335f, vm.Gfx.SnapshotVisibleObjects().Single().SurfaceResId);
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
        Assert.Equal(0x33, vm.Gfx.SnapshotVisibleObjects().Single().SurfaceResId);
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
        Assert.Equal(0x33, vm.Gfx.SnapshotVisibleObjects().Single().SurfaceResId);
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
    public void Sc0000MoviePayloadDecodesAn800By600FrameOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var entry = resources.ResolveMovie(0x33)!;
        using var decoder = new DirectShowMovieDecoder(resources.ReadMovie(entry));

        Assert.True(decoder.StopTimeMs > 0, "DirectShow should expose a positive IMediaPosition stop time");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        RgbaImage? frame = null;
        while (DateTime.UtcNow < deadline && !decoder.TryTakeFrame(out frame))
            Thread.Sleep(20);

        Assert.NotNull(frame);
        Assert.Equal(800, frame!.Width);
        Assert.Equal(600, frame.Height);
        Assert.Equal(800 * 600 * 4, frame.Pixels.Length);

        byte[] firstPixels = frame.Pixels;
        var changeDeadline = DateTime.UtcNow.AddSeconds(2);
        bool changed = false;
        while (DateTime.UtcNow < changeDeadline && !changed)
        {
            Thread.Sleep(20);
            if (decoder.TryTakeFrame(out var later))
                changed = !firstPixels.AsSpan().SequenceEqual(later.Pixels);
        }
        Assert.True(changed, "DirectShow should deliver changing MPEG frames, not one retained still");
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
