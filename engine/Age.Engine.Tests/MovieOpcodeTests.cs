using System.Collections.Generic;
using Age.Engine.Diagnostics;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class MovieOpcodeTests
{
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
    public void Sc0000MoviePayloadReadsFromArchiveVfsAndIsMpegProgramStream()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var entry = resources.Resolve("SC0000", 0x33);

        Assert.Equal("CHAPTER.AGF", entry?.Name);
        var movie = resources.ReadMovie(entry!);
        Assert.Equal(new byte[] { 0, 0, 1, 0xba }, movie.Bytes[..4]);
        Assert.Equal(8_194_052, movie.Bytes.Length);
    }

    [Fact]
    public void Sc0000MoviePayloadDecodesAn800By600FrameOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var resources = new ResourceMap(catalog, new Sys4AssetStore(catalog, Paths.GameDir));
        var entry = resources.Resolve("SC0000", 0x33)!;
        using var decoder = new DirectShowMovieDecoder(resources.ReadMovie(entry));

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
}
