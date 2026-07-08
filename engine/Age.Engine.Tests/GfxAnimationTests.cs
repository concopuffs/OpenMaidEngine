using System.Collections.Generic;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class GfxAnimationTests
{
    // ---- Task 2: GfxState animation-channel data model (revised global-clock model, see engine-re.md) ----

    [Fact]
    public void SetAnimTransform_RecordsTargetParamsAndEnables()
    {
        var g = new GfxState();
        g.SetAnimTransform(0x1000, p1: 7, p2: 9, target: (100, 100, 100), normalized: true);
        var o = g.TryGet(0x1000)!;
        Assert.Equal((100L, 100L, 100L), o.AnimTarget);
        Assert.Equal(7, o.AnimParam1);
        Assert.Equal(9, o.AnimParam2);
        Assert.True(o.AnimNormalized);
        Assert.True(o.AnimEnabled);
    }

    [Fact]
    public void StartAnim_SetsTargetDurationEnablesAndBumpsGeneration()
    {
        var g = new GfxState();
        g.StartAnim(0x1000, durationTicks: 30, target: (0, 0, 5));
        var o = g.TryGet(0x1000)!;
        Assert.Equal((0L, 0L, 5L), o.AnimTarget);
        Assert.Equal(30, o.AnimDurationTicks);
        Assert.True(o.AnimEnabled);
        Assert.Equal(1, o.AnimGeneration);      // fresh object starts at 0, one anim-start -> 1
    }

    [Fact]
    public void SetAnimClock_SetsGlobalDurationAndBumpsClockGeneration()
    {
        var g = new GfxState();
        var cg0 = g.AnimClockGeneration;
        g.SetAnimClock(400);
        Assert.Equal(400, g.AnimClockDurationTicks);
        Assert.Equal(cg0 + 1, g.AnimClockGeneration);
    }

    // ---- Tasks 3-5: VM dispatch + snapshot (synthesized scenes) ----

    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int a) => new(3, a);
    private static Operand I(long v) => new(0, v);
    private static (int, Operand[]) MovGI(int d, long v) => (0x55, new[] { G(d), I(v) });
    private static (int, Operand[]) Exit() => (0x2, System.Array.Empty<Operand>());

    [Fact]
    public void SetAnimTransformAbs_DispatchRecordsChannel()
    {
        var t = T();
        // handle g[1]=0x1000; p1 g[2]=7; p2 g[3]=9; target g[4,5,6]=(800,500,0)
        var scene = ScriptAssembler.Assemble(t, "ANIM", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 7), MovGI(3, 9), MovGI(4, 800), MovGI(5, 500), MovGI(6, 0),
            (0x220, new[] { G(1), G(2), G(3), G(4), G(5), G(6) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.Equal((800L, 500L, 0L), o.AnimTarget);
        Assert.False(o.AnimNormalized);
        Assert.True(o.AnimEnabled);
    }

    [Fact]
    public void AnimStartAndClock_DispatchSetTargetDurationClockAndGeneration()
    {
        var t = T();
        // anim-start(handle=0x1000, duration=30, target=(0,0,5)); set-anim-clock(400)
        var scene = ScriptAssembler.Assemble(t, "ANIM", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 30), MovGI(3, 0), MovGI(4, 0), MovGI(5, 5), MovGI(6, 400),
            (0x234, new[] { G(1), G(2), G(3), G(4), G(5) }),
            (0x238, new[] { G(6) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.Equal((0L, 0L, 5L), o.AnimTarget);
        Assert.Equal(30, o.AnimDurationTicks);
        Assert.Equal(1, o.AnimGeneration);
        Assert.True(o.AnimEnabled);
        Assert.Equal(400, vm.Gfx.AnimClockDurationTicks);
        Assert.Equal(1, vm.Gfx.AnimClockGeneration);
    }

    [Fact]
    public void SnapshotCarriesAnimStateForVisibleObject()
    {
        var t = T();
        // make object 0xA visible via set/draw-texture, then anim-start it toward (800,600,0) over 30 ticks.
        var scene = ScriptAssembler.Assemble(t, "ANIM", new List<(int, Operand[])>
        {
            MovGI(1, 0xA), MovGI(2, 4), MovGI(7, 0x25), MovGI(3, 800), MovGI(4, 600), MovGI(5, 0), MovGI(6, 0),
            (0x1f9, new[] { G(7), G(2), I(0) }),                              // set-texture resId 0x25 -> slot 4
            (0x1fb, new[] { G(1), G(2), I(0), I(0), G(3), G(4), G(5), G(6) }),  // draw-texture: object 0xA visible
            MovGI(8, 30),
            (0x234, new[] { G(1), G(8), G(3), G(4), G(5) }),                 // anim-start(0xA, dur=30, (800,600,0))
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var vis = vm.Gfx.SnapshotVisibleObjects();
        Assert.Single(vis);
        Assert.True(vis[0].Anim.Enabled);
        Assert.Equal((800L, 600L, 0L), (vis[0].Anim.TX, vis[0].Anim.TY, vis[0].Anim.TZ));
        Assert.Equal(30, vis[0].Anim.DurationTicks);
        Assert.Equal(1, vis[0].Anim.Generation);
    }
}
