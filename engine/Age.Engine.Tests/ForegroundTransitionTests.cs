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
    public void RadialBlurRangeTransition_SamplesNativeLengthAndCenterTriples()
    {
        var gfx = new GfxState();
        gfx.SetSurface(14, 0, -1);
        gfx.BindDraw(90_000, 14, 0, 0, 1024, 576, 0, 0);
        gfx.BindDraw(70_010, 14, 0, 0, 100, 100, 0, 0);
        gfx.QueueRadialBlurRangeTransition(
            90_000, 14, 70_010, 23,
            100, 512, 204, 0, 512, 204, 100, 500);

        Assert.Equal(1, gfx.StartForegroundTransitions(1_000));
        var delayed = Assert.Single(gfx.SnapshotRadialBlurRangeTransitions(1_050));
        Assert.Equal(100, delayed.Length);
        Assert.Equal(512, delayed.CenterX);
        Assert.Equal(204, delayed.CenterY);
        Assert.Equal(0, delayed.Progress);

        var midpoint = Assert.Single(gfx.SnapshotRadialBlurRangeTransitions(1_350));
        Assert.Equal(50, midpoint.Length);
        Assert.Equal(0.5, midpoint.Progress, 3);
        Assert.True(midpoint.Contains(70_010));
        Assert.True(midpoint.Contains(70_032));
        Assert.False(midpoint.Contains(70_033));
        Assert.True(gfx.HasActiveTimedPresentation(1_350));
        Assert.False(gfx.HasActiveTimedPresentation(1_600));

        RenderObject command = gfx.SnapshotVisibleObjects(1_350).Single(x => x.Handle == 90_000);
        Assert.Equal(midpoint, command.RadialBlurTransition);
        gfx.EraseRange(90_000, 1);
        Assert.Empty(gfx.SnapshotRadialBlurRangeTransitions(1_350));
    }

    [Fact]
    public void DirectionalBlurRangeTransition_SamplesNativeLengthAndAnglePairs()
    {
        var gfx = new GfxState();
        gfx.BindDraw(50_000, 14, 0, 0, 1024, 576, 0, 0);
        gfx.BindDraw(40_000, 14, 0, 0, 100, 100, 0, 0);
        gfx.QueueDirectionalBlurRangeTransition(
            50_000, 14, 40_000, 2,
            100, -90, 0, 90, 100, 500);

        Assert.Equal(1, gfx.StartForegroundTransitions(1_000));
        var delayed = Assert.Single(gfx.SnapshotDirectionalBlurRangeTransitions(1_050));
        Assert.Equal(100, delayed.Length);
        Assert.Equal(-90, delayed.AngleDegrees);
        Assert.Equal(0, delayed.Progress);

        var midpoint = Assert.Single(gfx.SnapshotDirectionalBlurRangeTransitions(1_350));
        Assert.Equal(50, midpoint.Length);
        Assert.Equal(0, midpoint.AngleDegrees);
        Assert.Equal(0.5, midpoint.Progress, 3);
        Assert.True(midpoint.Contains(40_000));
        Assert.True(midpoint.Contains(40_001));
        Assert.False(midpoint.Contains(40_002));

        RenderObject command = gfx.SnapshotVisibleObjects(1_350).Single(x => x.Handle == 50_000);
        Assert.Equal(midpoint, command.DirectionalBlurTransition);
        gfx.EraseRange(50_000, 1);
        Assert.Empty(gfx.SnapshotDirectionalBlurRangeTransitions(1_350));
    }

    [Fact]
    public void Sys4433Opcode250_QueuesExactDirectionalBlurContract()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var scene = ScriptAssembler.Assemble(table, "DIRECTIONAL", new List<(int, Operand[])>
        {
            (0x250, new[]
            {
                I(50_000), I(14), I(40_000), I(2),
                G(10), G(11), G(12), G(13), I(100), I(500),
            }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingHost(),
            compatibility: new("kamidori", "SYS4433"));
        vm.Globals[10] = 100;
        vm.Globals[11] = -90;
        vm.Globals[12] = 0;
        vm.Globals[13] = 90;

        vm.Run();

        var transition = Assert.Single(vm.Gfx.SnapshotDirectionalBlurRangeTransitions(0));
        Assert.Equal(50_000, transition.CommandKey);
        Assert.Equal(14, transition.TargetSlot);
        Assert.Equal(40_000, transition.RangeStart);
        Assert.Equal(2, transition.RangeCount);
        Assert.Equal(100, transition.Length);
        Assert.Equal(-90, transition.AngleDegrees);
        Assert.Null(vm.CompatibilityFailure);
    }

    [Fact]
    public void Sys4433Opcode251_QueuesExactAimDungeonTransitionContract()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
        var scene = ScriptAssembler.Assemble(table, "AIM", new List<(int, Operand[])>
        {
            (0x251, new[]
            {
                I(90_000), I(14), I(70_010), I(23), I(100), I(512), I(204),
                I(0), I(512), I(204), I(100), I(500),
            }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, table, new RecordingHost(),
            compatibility: new("kamidori", "SYS4433"));

        vm.Run();

        var transition = Assert.Single(vm.Gfx.SnapshotRadialBlurRangeTransitions(0));
        Assert.Equal(90_000, transition.CommandKey);
        Assert.Equal(14, transition.TargetSlot);
        Assert.Equal(70_010, transition.RangeStart);
        Assert.Equal(23, transition.RangeCount);
        Assert.Equal(100, transition.Length);
        Assert.Null(vm.CompatibilityFailure);
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

    [Fact]
    public void ClickCompletionFinishesSurfaceAndOrdinaryOneShotsButPreservesExcludedChannels()
    {
        var gfx = new GfxState();
        gfx.SetSurface(4, 0x25, -1);
        gfx.BindDraw(100, 4, 0, 0, 800, 600, 0, 0);
        gfx.BindDraw(101, 4, 0, 0, 800, 600, 0, 0);
        gfx.SetAnimatedObjectColorResolved(100, 0, 1000, 0, 0xffffff);
        gfx.SetScaleChannel(100, 0, 1000, (200, 200, 100));
        gfx.SetAnimatedObjectColorResolved(101, 0, 1000, 0, 0xffffff);
        gfx.SetOneShotAnimationControl(101, 1);
        gfx.SetRotationCycle(100, 1000, (0, 0, 1));
        gfx.QueueSurfaceAlphaTransition(102, 6, 101, 1, 100, 1, 0, 1000);
        gfx.StartForegroundTransitions(100);
        gfx.QueueMovieMaskTransition(new MovieMaskTransitionRequest(
            11, 45, 10, 1, -184, 0, 800, 600, 0, 0x325e, 0, 1000));

        Assert.True(gfx.TryCompleteClickSkippableTimedPresentation(200, out int completed));
        Assert.Equal(3, completed);

        Assert.Equal(1.0, gfx.SnapshotForegroundTransitions(200).Single().Progress);
        Assert.False(gfx.TryGet(100)!.OneShotColorEnabled);
        Assert.False(gfx.TryGet(100)!.ScaleEnabled);
        Assert.True(gfx.TryGet(100)!.RotationEnabled);
        Assert.True(gfx.TryGet(101)!.OneShotColorEnabled);
        Assert.False(gfx.SnapshotMovieMaskTransitions().Single().Completed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AnimationServiceFlagBit0RejectsClickCompletion(long flags)
    {
        var gfx = new GfxState();
        gfx.SetSurface(4, 0x25, -1);
        gfx.BindDraw(100, 4, 0, 0, 800, 600, 0, 0);
        gfx.SetAnimatedObjectColorResolved(100, 0, 1000, 0, 0xffffff);
        gfx.SetAnimationServiceFlags(flags);

        Assert.False(gfx.TryCompleteClickSkippableTimedPresentation(100, out int completed));
        Assert.Equal(0, completed);
        Assert.True(gfx.TryGet(100)!.OneShotColorEnabled);
    }

    [Fact]
    public void AnimationServiceFlagBit1AcceptsWaitBypassWithoutForcingEndpoint()
    {
        var gfx = new GfxState();
        gfx.SetSurface(4, 0x25, -1);
        gfx.BindDraw(100, 4, 0, 0, 800, 600, 0, 0);
        gfx.SetAnimatedObjectColorResolved(100, 0, 1000, 0, 0xffffff);
        gfx.SetAnimationServiceFlags(2);

        Assert.True(gfx.TryCompleteClickSkippableTimedPresentation(100, out int completed));
        Assert.Equal(0, completed);
        Assert.True(gfx.TryGet(100)!.OneShotColorEnabled);
    }

    [Fact]
    public void MovieMaskAcceptsWaitBypassAndContinuesAsynchronously()
    {
        var gfx = new GfxState();
        gfx.QueueMovieMaskTransition(new MovieMaskTransitionRequest(
            11, 45, 10, 1, -184, 0, 800, 600, 0, 0x325e, 0, 1000));

        Assert.True(gfx.TryCompleteClickSkippableTimedPresentation(100, out int completed));
        Assert.Equal(0, completed);
        Assert.True(gfx.HasActiveTimedPresentation(100));
        Assert.False(gfx.SnapshotMovieMaskTransitions().Single().Completed);
    }
}
