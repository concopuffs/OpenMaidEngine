using System;
using System.Collections.Generic;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class BattleFrontierOpsTests
{
    private const int Immediate = 0, InlineString = 2, GlobalInt = 3, GlobalString = 5,
                      LocalString = 11;

    private static OpcodeTable Table() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand I(long value) => new(Immediate, value);
    private static Operand G(int address) => new(GlobalInt, address);

    [Fact]
    public void SurfaceRectCopy_ForwardsTheCompleteBlitRequest()
    {
        var table = Table();
        var script = ScriptAssembler.Assemble(table, "SURFACE_COPY", new List<(int, Operand[])>
        {
            (0x207, new[] { I(72), I(67), I(12), I(15), I(3), I(4), I(30), I(33) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(new SurfaceRectCopy(72, 67, 12, 15, 3, 4, 30, 33),
                     Assert.Single(host.SurfaceCopies));
    }

    [Fact]
    public void ScaledSurfaceRectCopy_ForwardsIndependentRectangleSizes()
    {
        var table = Table();
        var script = ScriptAssembler.Assemble(table, "SCALED_SURFACE_COPY", new List<(int, Operand[])>
        {
            (0x32, new[] { I(2), I(193), I(0), I(0), I(1024), I(576),
                            I(0), I(0), I(384), I(216) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(new SurfaceRectCopy(2, 193, 0, 0, 1024, 576, 0, 0, 384, 216),
                     Assert.Single(host.SurfaceCopies));
    }

    [Fact]
    public void ScalarBattleHelpers_PreserveNativeSignedBehaviorAndStringAliasing()
    {
        var table = Table();
        var script = ScriptAssembler.Assemble(table, "BATTLE_SCALARS", new List<(int, Operand[])>
        {
            (0x191, new[] { G(0x100), I(int.MinValue) }),
            (0x193, new[] { new Operand(GlobalString, 0x500), new Operand(GlobalString, 0x500),
                            new Operand(InlineString, 0) }),
            (0x1c8, new[] { new Operand(LocalString, 0), I(-42) }),
            (0x55, new[] { new Operand(GlobalString, 0x501), new Operand(LocalString, 0) }),
            (0x2, Array.Empty<Operand>()),
        }, new[] { "/99" });
        var vm = new VirtualMachine(script, table, new RecordingHost());
        vm.GlobalStrings[0x500] = "17";

        vm.Run();

        Assert.Equal(int.MinValue, vm.Globals[0x100]);
        Assert.Equal("17/99", vm.GlobalStrings[0x500]);
        Assert.Equal("-42", vm.GlobalStrings[0x501]);
    }

    [Fact]
    public void MonotonicAndFrameTimeOps_StoreNativeLowDwordSamples()
    {
        var table = Table();
        var host = new SequencedClockHost(0x1_0000_0005, 100, 116);
        var script = ScriptAssembler.Assemble(table, "BATTLE_CLOCK", new List<(int, Operand[])>
        {
            (0xd0, new[] { G(0x100) }),
            (0x23c, Array.Empty<Operand>()),
            (0x23c, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(5, vm.Globals[0x100]);
        Assert.Equal(100u, vm.Gfx.PreviousFrameTimeMilliseconds);
        Assert.Equal(116u, vm.Gfx.CurrentFrameTimeMilliseconds);
    }

    [Fact]
    public void MovieActivityAndAnimationServiceFlags_ControlBattlePollingAndReset()
    {
        var table = Table();
        var host = new RecordingHost();
        host.ActiveMovieSurfaces.Add(7);
        var script = ScriptAssembler.Assemble(table, "BATTLE_CONTROL", new List<(int, Operand[])>
        {
            (0x23a, new[] { G(0x100), I(7) }),
            (0x23a, new[] { G(0x101), I(8) }),
            (0x238, new[] { I(400) }),
            (0x24e, new[] { I(2) }),
            (0x243, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(script, table, host);

        vm.Run();

        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(0, vm.Globals[0x101]);
        Assert.Equal(400, vm.Gfx.AnimClockDurationTicks);
        Assert.Equal(1, vm.Gfx.AnimClockGeneration);
        Assert.Equal(2, vm.Gfx.AnimationServiceFlags);
    }

    [Fact]
    public void DelayedCombatVoice_ForwardsAllSchedulingOperands()
    {
        var table = Table();
        var host = new RecordingHost();
        var script = ScriptAssembler.Assemble(table, "BATTLE_VOICE", new List<(int, Operand[])>
        {
            (0x2c0, new[] { I(123), I(0), I(275) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());

        new VirtualMachine(script, table, host).Run();

        Assert.Equal((123L, 0, 275L), Assert.Single(host.ScheduledVoiceRequests));
    }

    private sealed class SequencedClockHost : RecordingHost
    {
        private readonly Queue<long> _samples;
        public SequencedClockHost(params long[] samples) => _samples = new Queue<long>(samples);
        public override long InputClockMilliseconds => _samples.Dequeue();
    }
}
