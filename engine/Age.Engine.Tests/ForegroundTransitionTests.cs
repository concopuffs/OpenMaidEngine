using System.Collections.Generic;
using System.Linq;
using Age.Engine.Model;
using Age.Engine.Hosting;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class ForegroundTransitionTests
{
    private static OpcodeTable T() => OpcodeTableJson.Load(Paths.OpcodesJson);
    private static Operand G(int address) => new(3, address);
    private static Operand I(long value) => new(0, value);

    [Fact]
    public void SurfaceTransition_HasStartDelayProgressNaturalAndForcedCompletion()
    {
        var gfx = new GfxState();
        gfx.SetSurface(4, 0x25, -1);
        gfx.BindDraw(100, 4, 0, 0, 800, 600, 0, 0);
        gfx.SetRotationCycle(100, 1000, (0, 0, 1)); // ambient, deliberately independent
        gfx.QueueSurfaceAlphaTransition(102, 6, 101, 1, 100, 1, 100, 200);

        Assert.Equal(1, gfx.StartForegroundTransitions(1000));
        Assert.Equal(0.0, gfx.SnapshotForegroundTransitions(1050).Single().Progress);
        Assert.Equal(0.25, gfx.SnapshotForegroundTransitions(1150).Single().Progress, 3);
        Assert.False(gfx.HasActiveForegroundTransitions(1300));
        Assert.True(gfx.TryGet(100)!.RotationEnabled);

        gfx.QueueSurfaceAlphaTransition(202, 7, 201, 1, 200, 1, 0, 500);
        gfx.StartForegroundTransitions(2000);
        Assert.True(gfx.HasActiveForegroundTransitions(2100));
        Assert.Equal(1, gfx.CompleteForegroundTransitions(2100));
        var forced = gfx.SnapshotForegroundTransitions(2100).Single(t => t.TargetSlot == 7);
        Assert.True(forced.Forced);
        Assert.Equal(1.0, forced.Progress);
        Assert.True(gfx.TryGet(100)!.RotationEnabled); // transition click did not finish ambient animation
    }

    [Fact]
    public void CloneObject_SnapshotsOldSurfaceBindingForTransitionRangeA()
    {
        var gfx = new GfxState();
        gfx.SetSurface(4, 0x25, -1);
        gfx.BindDraw(100, 4, 0, 0, 800, 600, 0, 0);
        gfx.SetStaticObjectColorResolved(100, 2, -1, -1);
        Assert.True(gfx.CloneObject(100, 101));
        gfx.SetSurface(5, 0x27, -1);
        gfx.BindDraw(100, 5, 0, 0, 800, 600, 0, 0);

        var snapshot = gfx.SnapshotVisibleObjects();
        Assert.Equal(0x27, snapshot.Single(x => x.Handle == 100).SurfaceResId);
        Assert.Equal(0x25, snapshot.Single(x => x.Handle == 101).SurfaceResId);
    }

    [Fact]
    public void VmDispatchesSkipQueriesQueueAndPresentResumeBoundary()
    {
        var table = T();
        var scene = ScriptAssembler.Assemble(table, "FOREGROUND", new List<(int, Operand[])>
        {
            (0x1c7, new[] { G(1) }),
            (0x1cc, new[] { G(2) }),
            (0x223, new[] { I(102), I(6), I(101), I(1), I(100), I(1), I(50), I(200) }),
            (0x21c, System.Array.Empty<Operand>()),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var host = new RecordingHost { MessageSkip = true, AdvReadSkip = true };
        var vm = new VirtualMachine(scene, table, host);

        vm.Run();

        Assert.Equal(1, vm.Globals[1]);
        Assert.Equal(1, vm.Globals[2]);
        Assert.Equal(1, host.TransitionWaits);
        Assert.Equal(1.0, vm.Gfx.SnapshotForegroundTransitions(100).Single().Progress);
    }

    [Fact]
    public void MovieMaskTransitionBlocksUntilMovieCompletionAndIgnoresClickCompletion()
    {
        var gfx = new GfxState();
        var request = new MovieMaskTransitionRequest(
            11, 45, 10, 1, -184, 0, 800, 600, 0, 0x325e, 0, 1000);

        gfx.QueueMovieMaskTransition(request);

        Assert.True(gfx.HasActiveForegroundTransitions(0));
        Assert.True(gfx.HasActiveTimedPresentation(0));
        Assert.Equal(0, gfx.CompleteForegroundTransitions(100));
        Assert.False(gfx.SnapshotMovieMaskTransitions().Single().Completed);
        Assert.True(gfx.CompleteMovieMaskTransition(45));
        Assert.False(gfx.HasActiveForegroundTransitions(100));
        Assert.True(gfx.SnapshotMovieMaskTransitions().Single().Completed);
    }
}
