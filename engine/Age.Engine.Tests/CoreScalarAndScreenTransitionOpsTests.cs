using System;
using System.Collections.Generic;
using System.Linq;
using Age.Engine.Hosting;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class CoreScalarAndScreenTransitionOpsTests
{
    private const int Imm = 0, GlobalInt = 3, LocalInt = 9;

    [Fact]
    public void ZeroIntRange_ClearsEveryConsecutiveDestinationCell()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        (int, Operand[]) Mov(int address, long value)
            => (0x55, new[] { new Operand(GlobalInt, address), new Operand(Imm, value) });
        var ops = new List<(int, Operand[])>
        {
            Mov(0x2e49, 7), Mov(0x2e4a, 8), Mov(0x2e4b, 9),
            (0x6c, new[] { new Operand(GlobalInt, 0x2e49), new Operand(Imm, 3) }),
            (0x2, Array.Empty<Operand>()),
        };

        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "ZERO_RANGE", ops, Array.Empty<string>()), table,
                                    new RecordingHost());
        vm.Run();

        Assert.All(Enumerable.Range(0x2e49, 3), address => Assert.Equal(0, vm.Globals[address]));
    }

    [Fact]
    public void RandomModulo_WritesAValueInsideTheNativeRange()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var ops = new List<(int, Operand[])>
        {
            (0x60, new[] { new Operand(LocalInt, 0), new Operand(Imm, 4) }),
            (0x55, new[] { new Operand(GlobalInt, 0x100), new Operand(LocalInt, 0) }),
            (0x2, Array.Empty<Operand>()),
        };

        var vm = new VirtualMachine(ScriptAssembler.Assemble(table, "RANDOM_MODULO", ops, Array.Empty<string>()), table,
                                    new RecordingHost());
        vm.Run();

        Assert.InRange(vm.Globals[0x100], 0, 3);
    }

    [Fact]
    public void BlackSurfaceFades_ForwardCapturedSlotTimingAndDirection()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "SURFACE_BLACK_FADES", new List<(int, Operand[])>
        {
            (0x21, new[] { new Operand(Imm, 1), new Operand(Imm, 30) }),
            (0x22, new[] { new Operand(Imm, 2), new Operand(Imm, 65) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal(
            new[]
            {
                (1, 30L, SurfaceBlackFadeDirection.FromBlack),
                (2, 65L, SurfaceBlackFadeDirection.ToBlack),
            },
            host.SurfaceBlackFades);
        Assert.Equal(480, LegacyScreenTransitionTiming.DurationMilliseconds(30));
        Assert.Equal(1024, LegacyScreenTransitionTiming.DurationMilliseconds(65));
    }

    [Fact]
    public void CrossfadeSurfaces_ForwardsBothCapturedSlotsAndTimingArgument()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "SURFACE_CROSSFADE", new List<(int, Operand[])>
        {
            (0x25, new[] { new Operand(Imm, 1), new Operand(Imm, 2), new Operand(Imm, 10) }),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var host = new RecordingHost();

        new VirtualMachine(script, table, host).Run();

        Assert.Equal((1, 2, 10L), Assert.Single(host.SurfaceCrossfades));
        Assert.Equal(160, LegacyScreenTransitionTiming.DurationMilliseconds(10));
        Assert.Equal(480, LegacyScreenTransitionTiming.DurationMilliseconds(30));
        Assert.Equal(1024, LegacyScreenTransitionTiming.DurationMilliseconds(65));
    }
}
