using System.Collections.Generic;
using System.Linq;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class GfxAnimationTests
{
    // ---- Native independent matrix channels + separate cyclic rotation (see engine-re.md). ----

    [Fact]
    public void ScaleAndTranslationChannels_AreIndependent()
    {
        var g = new GfxState();
        g.SetScaleChannel(0x1000, delayMs: 7, durationMs: 900, percent: (200, 50, 100));
        g.SetTranslationChannel(0x1000, delayMs: 9, durationMs: 1200, target: (80, -25, 6));
        var o = g.TryGet(0x1000)!;
        Assert.Equal((2.0, 0.5, 1.0), o.ScaleTarget);
        Assert.Equal((80.0, -25.0, 6.0), o.TranslationTarget);
        Assert.Equal((7, 900), (o.ScaleDelayMs, o.ScaleDurationMs));
        Assert.Equal((9, 1200), (o.TranslationDelayMs, o.TranslationDurationMs));
        Assert.True(o.ScaleEnabled);
        Assert.True(o.TranslationEnabled);
    }

    [Fact]
    public void RotationCycle_DoesNotOverwriteMatrixChannels()
    {
        var g = new GfxState();
        g.SetScaleChannel(0x1000, 0, 100, (150, 150, 100));
        g.SetTranslationChannel(0x1000, 0, 100, (10, 20, 0));
        g.SetRotationCycle(0x1000, periodMs: 30, axis: (0, 0, 5));
        var o = g.TryGet(0x1000)!;
        Assert.Equal((1.5, 1.5, 1.0), o.ScaleTarget);
        Assert.Equal((10.0, 20.0, 0.0), o.TranslationTarget);
        Assert.Equal((0L, 0L, 5L), o.RotationAxis);
        Assert.Equal(30, o.RotationPeriodMs);
        Assert.True(o.RotationEnabled);
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
    public void MatrixOpcodeDispatch_RecordsSeparateChannels()
    {
        var t = T();
        // 0x220 sets translation; 0x21e sets scale percentages without overwriting it.
        var scene = ScriptAssembler.Assemble(t, "ANIM", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 7), MovGI(3, 9), MovGI(4, 800), MovGI(5, 500), MovGI(6, 0),
            (0x220, new[] { G(1), G(2), G(3), G(4), G(5), G(6) }),
            MovGI(4, 200), MovGI(5, 50), MovGI(6, 100),
            (0x21e, new[] { G(1), G(2), G(3), G(4), G(5), G(6) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.Equal((800.0, 500.0, 0.0), o.TranslationTarget);
        Assert.Equal((2.0, 0.5, 1.0), o.ScaleTarget);
        Assert.True(o.TranslationEnabled);
        Assert.True(o.ScaleEnabled);
    }

    [Fact]
    public void RotationCycleAndClock_DispatchRemainSeparate()
    {
        var t = T();
        // The legacy anim-start name is a rotation period+axis; 0x238 remains its own clock service.
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
        Assert.Equal((0L, 0L, 5L), o.RotationAxis);
        Assert.Equal(30, o.RotationPeriodMs);
        Assert.True(o.RotationEnabled);
        Assert.Equal((1.0, 1.0, 1.0), o.ScaleCurrent);
        Assert.Equal((0.0, 0.0, 0.0), o.TranslationCurrent);
        Assert.Equal(400, vm.Gfx.AnimClockDurationTicks);
        Assert.Equal(1, vm.Gfx.AnimClockGeneration);
    }

    [Fact]
    public void ResetAnimClock_DispatchClearsOnlyGlobalServiceClock()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "CLOCKRESET", new List<(int, Operand[])>
        {
            MovGI(1, 400),
            (0x238, new[] { G(1) }),
            (0x243, System.Array.Empty<Operand>()),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal(0, vm.Gfx.AnimClockDurationTicks);
        Assert.Equal(2, vm.Gfx.AnimClockGeneration);
    }

    [Fact]
    public void SnapshotSamplesDelayedMatrixChannels_WithoutUsingZAsOpacity()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x25, -1);
        g.BindDraw(0xA, 4, 0, 0, 100, 80, 120, 70);
        g.GetOrCreate(0xA).V18 = (100, 50, 0);
        g.SetScaleChannel(0xA, delayMs: 100, durationMs: 200, percent: (200, 50, 300));
        g.SetTranslationChannel(0xA, delayMs: 100, durationMs: 200, target: (40, -10, 99));
        g.SetRotationCycle(0xA, periodMs: 30, axis: (0, 0, 5));

        var start = g.SnapshotVisibleObjects(1000).Single();
        Assert.Equal((1.0, 1.0, 1.0), (start.Transform.ScaleX, start.Transform.ScaleY, start.Transform.ScaleZ));
        Assert.Equal((0.0, 0.0, 0.0),
            (start.Transform.TranslateX, start.Transform.TranslateY, start.Transform.TranslateZ));

        var held = g.SnapshotVisibleObjects(1100).Single();
        Assert.Equal((1.0, 1.0, 1.0), (held.Transform.ScaleX, held.Transform.ScaleY, held.Transform.ScaleZ));
        Assert.Equal((0.0, 0.0, 0.0),
            (held.Transform.TranslateX, held.Transform.TranslateY, held.Transform.TranslateZ));

        var halfway = g.SnapshotVisibleObjects(1200).Single();
        Assert.Equal((1.5, 0.75, 2.0),
            (halfway.Transform.ScaleX, halfway.Transform.ScaleY, halfway.Transform.ScaleZ));
        Assert.Equal((20.0, -5.0, 49.5),
            (halfway.Transform.TranslateX, halfway.Transform.TranslateY, halfway.Transform.TranslateZ));
        Assert.Equal(255, halfway.Alpha);

        var done = g.SnapshotVisibleObjects(1300).Single();
        Assert.Equal((2.0, 0.5, 3.0), (done.Transform.ScaleX, done.Transform.ScaleY, done.Transform.ScaleZ));
        Assert.Equal((40.0, -10.0, 99.0),
            (done.Transform.TranslateX, done.Transform.TranslateY, done.Transform.TranslateZ));
        Assert.True(done.Rotation.Enabled);
        Assert.Equal(255, done.Alpha);   // scale-Z=3, translation-Z=99, rotation-axis-Z=5: still opaque
    }

    [Fact]
    public void Transform2D_UsesNativeAnchoredRowVectorOrder_AndDirectProjection()
    {
        var t = new TransformState(2, 3, 99, 10, -7, 1234, 100, 50, 888);
        var p = Transform2DMath.Apply(120, 60, t);
        Assert.Equal((150.0, 73.0), p);
    }

    [Fact]
    public void Transform2D_NegativeScaleMovesFarEdgeAcrossAnchor()
    {
        var t = new TransformState(-2, 1, 1, 0, 0, 0, 100, 0, 0);
        var left = Transform2DMath.Apply(90, 0, t);
        var right = Transform2DMath.Apply(110, 0, t);
        Assert.Equal((120.0, 0.0), left);
        Assert.Equal((80.0, 0.0), right);
    }

    [Fact]
    public void Transform2D_MatchesCapturedNativeSc0000ScaleEndpoint()
    {
        // Native handle 0xcbc0: base=(0,600), anchor=(400,1000), scale=5.
        var t = new TransformState(5, 5, 1, 0, 0, 0, 400, 1000, 0);
        Assert.Equal((-1600.0, -1000.0), Transform2DMath.Apply(0, 600, t));
    }
}
