using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

namespace Age.Engine.Tests;

public class GfxAnimationDetachTests
{
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int address) => new(3, address);
    private static Operand I(long value) => new(0, value);
    private static (int, Operand[]) Mov(int address, long value) => (0x55, new[] { G(address), I(value) });
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void Opcode242SetsControlWordOnGetOrCreateObjectAndCloneCopiesIt()
    {
        var table = T();
        var scene = ScriptAssembler.Assemble(table, "ANIM-DETACH", new List<(int, Operand[])>
        {
            Mov(1, 0x100), Mov(2, 0x101),
            (0x242, new[] { G(1), I(1) }),
            (0x21d, new[] { G(1), G(2) }),
            Exit(),
        }, System.Array.Empty<string>());

        var vm = new VirtualMachine(scene, table, new RecordingHost());
        vm.Run();

        Assert.Equal(1, vm.Gfx.TryGet(0x100)!.OneShotAnimationControlFlags);
        Assert.Equal(1, vm.Gfx.TryGet(0x101)!.OneShotAnimationControlFlags);
    }

    [Fact]
    public void DetachedOneShotSurvivesGlobalCompletionWithoutBlockingAndClearsFlagNaturally()
    {
        var gfx = new GfxState();
        gfx.SetSurface(1, 0x20, -1);
        gfx.BindDraw(0x100, 1, 0, 0, 100, 100, 0, 0);
        gfx.BindDraw(0x101, 1, 0, 0, 100, 100, 0, 0);
        gfx.SetAnimatedObjectColorResolved(0x100, 0, 1000, 0, 0xffffff);
        gfx.SetAnimatedObjectColorResolved(0x101, 0, 1000, 0, 0xffffff);
        gfx.SetOneShotAnimationControl(0x100, 1);

        gfx.ResetAnimClock();

        Assert.True(gfx.TryGet(0x100)!.OneShotColorEnabled);
        Assert.False(gfx.TryGet(0x101)!.OneShotColorEnabled);
        Assert.False(gfx.HasActiveTimedPresentation(1000));
        Assert.True(gfx.HasActiveVisualPresentation(1000));

        gfx.SnapshotVisibleObjects(1000);
        Assert.True(gfx.SnapshotVisibleObjects(1500).Single(x => x.Handle == 0x100).ColorTransition!.Value.Active);
        gfx.SnapshotVisibleObjects(2000);

        Assert.False(gfx.TryGet(0x100)!.OneShotColorEnabled);
        Assert.Equal(0, gfx.TryGet(0x100)!.OneShotAnimationControlFlags & 1);
    }
}
