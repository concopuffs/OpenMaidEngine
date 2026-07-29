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
        g.BindDraw(0x1000, 1, 0, 0, 1, 1, 0, 0);
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
    public void CallerOwnedVisibleSnapshot_ReusesStorageAndPreservesHandleOrder()
    {
        var g = new GfxState();
        for (int i = 999; i >= 0; i--)
            g.BindDraw(0x1000 + i, 1, 0, 0, 1, 1, i, 0);
        var snapshot = new List<RenderObject>();
        g.SnapshotVisibleObjects(0, snapshot); // Grow and warm the buffer outside the measured interval.

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) g.SnapshotVisibleObjects(i, snapshot);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(1000, snapshot.Count);
        Assert.Equal(0x1000, snapshot[0].Handle);
        Assert.Equal(0x1000 + 999, snapshot[^1].Handle);
    }

    [Fact]
    public void CurrentScaleSetter_ExpandsGlowAroundItsAnchor()
    {
        var g = new GfxState();
        g.SetSurface(6, 0x29, -1);
        g.BindDraw(0xcb8e, 6, 0, 0, 800, 800, 0, 550);
        g.GetOrCreate(0xcb8e).V18 = (400, 950, 0);
        g.SetCurrentScale(0xcb8e, (210, 210, 100));

        var v = g.SnapshotVisibleObjects().Single();
        var top = Transform2DMath.Build(v.Transform).FromLocalOrigin(v.DstX, v.DstY).Apply(400, 0);
        Assert.Equal(2.1, v.Transform.ScaleX, 3);
        Assert.Equal(2.1, v.Transform.ScaleY, 3);
        Assert.Equal(110, top.Y, 3);
    }

    [Fact]
    public void RotationCycle_DoesNotOverwriteMatrixChannels()
    {
        var g = new GfxState();
        g.BindDraw(0x1000, 1, 0, 0, 1, 1, 0, 0);
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
    public void PreBindMatrixChannels_CreateNeutralPlaceholderWithoutLatentMotion()
    {
        // SC0000 calls op 0x220 on background handle 0xcb20 before BG001A is bound. Native creates the
        // object (so op 0x215 returns slot 0) but ignores all three matrix setters while visible bit 0 is clear.
        var g = new GfxState();
        g.SetScaleChannel(0xcb20, 0, 500, (200, 200, 100));
        g.SetRotationChannel(0xcb20, 0, 500, (0, 0, 1), 90);
        g.SetTranslationChannel(0xcb20, 0, 500, (0, 600, 0));

        var placeholder = g.TryGet(0xcb20)!;
        Assert.Equal(0, g.QuerySlot(0xcb20));
        Assert.False(placeholder.Visible);
        Assert.False(placeholder.ScaleEnabled);
        Assert.False(placeholder.RotationChannelEnabled);
        Assert.False(placeholder.TranslationEnabled);
        Assert.Equal((0.0, 0.0, 0.0), placeholder.TranslationTarget);

        g.BindDraw(0xcb20, 4, 0, 0, 800, 500, 0, -500);
        g.SetTranslationChannel(0xcb20, 0, 500, (0, 600, 0));
        Assert.True(placeholder.TranslationEnabled);
        Assert.Equal((0.0, 600.0, 0.0), placeholder.TranslationTarget);
    }

    [Fact]
    public void ActiveVisualPresentation_ExcludesStaticWaits_ButIncludesAmbientChannels()
    {
        static GfxState VisibleObject()
        {
            var state = new GfxState();
            state.SetSurface(1, 5, -1);
            state.BindDraw(7, 1, 0, 0, 64, 64, 0, 0);
            return state;
        }

        var unchanged = VisibleObject();
        Assert.False(unchanged.HasActiveVisualPresentation(1000));
        Assert.False(unchanged.SnapshotVisibleObjects(1000).Single().TimeVarying);

        var spritesheet = VisibleObject();
        spritesheet.SetSrcRect(7, 4, 1, 0, 800);
        Assert.True(spritesheet.HasActiveVisualPresentation(1000));
        Assert.True(spritesheet.SnapshotVisibleObjects(1000).Single().TimeVarying);

        var color = VisibleObject();
        color.SetColorAnim(7, 1000, GfxState.PackColor(0x80, 0xff0000));
        Assert.True(color.HasActiveVisualPresentation(1000));
        Assert.True(color.SnapshotVisibleObjects(1000).Single().TimeVarying);

        var rotation = VisibleObject();
        rotation.SetRotationCycle(7, 1000, (0, 0, 1));
        Assert.True(rotation.HasActiveVisualPresentation(1000));
        Assert.True(rotation.SnapshotVisibleObjects(1000).Single().TimeVarying);

        var scale = VisibleObject();
        scale.SetScaleCycle(7, 1000, (150, 75, 100));
        Assert.True(scale.HasActiveVisualPresentation(1000));
        Assert.True(scale.SnapshotVisibleObjects(1000).Single().TimeVarying);
    }

    [Fact]
    public void LoopingSpritesheet_PreservesCellSize_AdvancesRowMajor_AndWraps()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x37, -1);
        g.BindDraw(0xcf3a, 4, 0, 0, 200, 200, 300, 100); // AE001H: 800x400, eight 200x200 cells
        g.SetSrcRect(0xcf3a, frameCount: 8, columns: 4, cell: 0, period: 100);

        var first = g.SnapshotVisibleObjects(1000).Single();
        var second = g.SnapshotVisibleObjects(1100).Single();
        var fifth = g.SnapshotVisibleObjects(1400).Single();
        var eighth = g.SnapshotVisibleObjects(1700).Single();
        var wrapped = g.SnapshotVisibleObjects(1800).Single();

        Assert.Equal((0, 0, 200, 200), (first.SrcX, first.SrcY, first.W, first.H));
        Assert.Equal((200, 0, 200, 200), (second.SrcX, second.SrcY, second.W, second.H));
        Assert.Equal((0, 200, 200, 200), (fifth.SrcX, fifth.SrcY, fifth.W, fifth.H));
        Assert.Equal((600, 200, 200, 200), (eighth.SrcX, eighth.SrcY, eighth.W, eighth.H));
        Assert.Equal((0, 0, 200, 200), (wrapped.SrcX, wrapped.SrcY, wrapped.W, wrapped.H));
    }

    [Fact]
    public void PresentationReasons_PublishMutationOnce_AndSpritesheetOnlyAtCellBoundaries()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x37, -1);
        g.BindDraw(7, 4, 0, 0, 16, 16, 0, 0);
        g.SetSrcRect(7, frameCount: 4, columns: 2, cell: 0, period: 200);

        Assert.Equal(GfxPresentationReason.RetainedMutation, g.ConsumePresentationReasons(1000));
        Assert.Equal(GfxPresentationReason.None, g.ConsumePresentationReasons(1199));
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1200));
        Assert.Equal(GfxPresentationReason.None, g.ConsumePresentationReasons(1399));
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1400));
        Assert.Equal(GfxPresentationReason.None, g.ConsumePresentationReasons(1599));
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1600));
        Assert.Equal(GfxPresentationReason.None, g.ConsumePresentationReasons(1799));
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1800));
    }

    [Fact]
    public void PresentationReasons_CloneBeforeFirstSampleSharesPhase_AndCloneAfterKeepsIt()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x37, -1);
        g.BindDraw(10, 4, 0, 0, 16, 16, 0, 0);
        g.SetSrcRect(10, frameCount: 4, columns: 2, cell: 0, period: 200);
        Assert.True(g.CloneObject(10, 11));

        Assert.Equal(GfxPresentationReason.RetainedMutation, g.ConsumePresentationReasons(1000));
        Assert.Equal(1000, g.TryGet(10)!.SrcStart);
        Assert.Equal(1000, g.TryGet(11)!.SrcStart);
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1200));
        Assert.Equal(g.SnapshotVisibleObjects(1200).Single(x => x.Handle == 10).SrcX,
                     g.SnapshotVisibleObjects(1200).Single(x => x.Handle == 11).SrcX);

        Assert.True(g.CloneObject(10, 12));
        Assert.Equal(GfxPresentationReason.RetainedMutation, g.ConsumePresentationReasons(1250));
        Assert.Equal(1000, g.TryGet(12)!.SrcStart);
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1400));
        var cells = g.SnapshotVisibleObjects(1400).Select(x => x.SrcX).Distinct().ToArray();
        Assert.Single(cells);
    }

    [Fact]
    public void PresentationReasons_UsesObjectLocalPeriods_AndReconfigurationRestartsAtSharedSample()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x37, -1);
        g.BindDraw(10, 4, 0, 0, 16, 16, 0, 0);
        g.BindDraw(11, 4, 0, 0, 16, 16, 0, 0);
        g.SetSrcRect(10, frameCount: 4, columns: 2, cell: 0, period: 100);
        g.SetSrcRect(11, frameCount: 4, columns: 2, cell: 0, period: 250);
        g.ConsumePresentationReasons(1000);

        Assert.Equal(GfxPresentationReason.None, g.ConsumePresentationReasons(1099));
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1100));
        Assert.Equal(GfxPresentationReason.None, g.ConsumePresentationReasons(1199));
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1200));
        Assert.Equal(GfxPresentationReason.None, g.ConsumePresentationReasons(1249));
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1250));

        g.SetSrcRect(10, frameCount: 4, columns: 2, cell: 0, period: 400);
        Assert.Equal(GfxPresentationReason.RetainedMutation, g.ConsumePresentationReasons(1250));
        Assert.Equal(1250, g.TryGet(10)!.SrcStart);
        Assert.Equal(GfxPresentationReason.None, g.ConsumePresentationReasons(1499));
        // The second object's 250 ms boundary and the reconfigured object's 400 ms boundary coincide here.
        Assert.Equal(GfxPresentationReason.DiscreteSourceCell, g.ConsumePresentationReasons(1650));
    }

    [Fact]
    public void PresentationReasons_ContinuousChannelRemainsFrameDriven()
    {
        var g = new GfxState();
        g.SetSurface(1, 5, -1);
        g.BindDraw(7, 1, 0, 0, 64, 64, 0, 0);
        g.SetColorAnim(7, 1000, GfxState.PackColor(0x80, 0xff0000));

        var first = g.ConsumePresentationReasons(1000);
        Assert.True((first & GfxPresentationReason.RetainedMutation) != 0);
        Assert.True((first & GfxPresentationReason.ContinuousChannel) != 0);
        Assert.Equal(GfxPresentationReason.ContinuousChannel, g.ConsumePresentationReasons(1016));
    }

    [Fact]
    public void PresentationReasons_PhaseLockedUnitFamilyPublishesFiveCellChangesPerSecond()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x37, -1);
        g.BindDraw(100, 4, 0, 0, 16, 16, 0, 0);
        g.SetSrcRect(100, frameCount: 4, columns: 2, cell: 0, period: 200);
        for (long handle = 101; handle < 151; handle++) Assert.True(g.CloneObject(100, handle));
        g.ConsumePresentationReasons(1000);

        int cellChanges = 0;
        for (long now = 1001; now <= 2000; now++)
            if ((g.ConsumePresentationReasons(now) & GfxPresentationReason.DiscreteSourceCell) != 0)
                cellChanges++;

        Assert.Equal(5, cellChanges);
        Assert.Single(g.Objects.Select(pair => g.TryGet(pair.Handle)!.SrcStart).Distinct());
    }

    [Fact]
    public void OneShotRotation_SharesMatrixClockAndMatchesNativeSample()
    {
        var g = new GfxState();
        g.SetSurface(6, 1, -1); g.BindDraw(0xcb8e, 6, 0, 0, 800, 800, 300, 200);
        g.GetOrCreate(0xcb8e).V18 = (700, 600, 0);
        g.SetScaleChannel(0xcb8e, 500, 390, (110, 110, 100));
        g.GetOrCreate(0xcb8e).ScaleCurrent = (0.9, 0.9, 1);
        g.SetRotationChannel(0xcb8e, 500, 390, (0, 0, 1), 30);
        g.SnapshotVisibleObjects(1000);
        var sample = g.SnapshotVisibleObjects(1511).Single();
        var m = Transform2DMath.Build(sample.Transform);
        Assert.Equal(0.9055, m.XX, 4);
        Assert.Equal(0.0134, m.XY, 4);
        Assert.Equal(-0.0134, m.YX, 4);
        Assert.Equal(74.1449, m.TX, 3);
        Assert.Equal(47.3127, m.TY, 3);
    }

    [Fact]
    public void CyclicRotation_FloorsDegreesAndWrapsAtPeriod()
    {
        var g = new GfxState();
        g.SetSurface(1, 1, -1); g.BindDraw(7, 1, 0, 0, 1, 1, 0, 0);
        g.SetRotationCycle(7, 1000, (0, 0, -1));
        Assert.Equal(0, g.SnapshotVisibleObjects(5000).Single().Rotation.AngleDegrees);
        Assert.Equal(89, g.SnapshotVisibleObjects(5249).Single().Rotation.AngleDegrees);
        Assert.Equal(0, g.SnapshotVisibleObjects(6000).Single().Rotation.AngleDegrees);
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
            (0x1fb, new[] { G(1), I(0), I(0), I(0), I(1), I(1), I(0), I(0) }),
            (0x220, new[] { G(1), G(2), G(3), G(4), G(5), G(6) }),
            MovGI(4, 200), MovGI(5, 50), MovGI(6, 100),
            (0x21e, new[] { G(1), G(2), G(3), G(4), G(5), G(6) }),
            MovGI(4, 0), MovGI(5, 0), MovGI(6, 1), MovGI(7, 30),
            (0x21f, new[] { G(1), G(2), G(3), G(4), G(5), G(6), G(7) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.Equal((800.0, 500.0, 0.0), o.TranslationTarget);
        Assert.Equal((2.0, 0.5, 1.0), o.ScaleTarget);
        Assert.True(o.TranslationEnabled);
        Assert.True(o.ScaleEnabled);
        Assert.Equal((0.0, 0.0, 1.0, 30.0), o.RotationTarget);
        Assert.True(o.RotationChannelEnabled);
    }

    [Fact]
    public void Op0x1fd_SetsCurrentScalePercentages()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "STATIC_SCALE", new List<(int, Operand[])>
        {
            MovGI(1, 0xcb8e), MovGI(2, 210), MovGI(3, 240), MovGI(4, 100),
            (0x1fd, new[] { G(1), G(2), G(3), G(4) }), Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        Assert.Equal((2.1, 2.4, 1.0), vm.Gfx.TryGet(0xcb8e)!.ScaleCurrent);
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
    public void Op0x233_ConfiguresAnIndependentCyclicScaleChannel()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "SCALE_CYCLE", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000), MovGI(2, 1200), MovGI(3, 80), MovGI(4, 120), MovGI(5, 100),
            (0x233, new[] { G(1), G(2), G(3), G(4), G(5) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());

        vm.Run();

        var o = vm.Gfx.TryGet(0x1000)!;
        Assert.Equal(1200, o.ScaleCyclePeriodMs);
        Assert.Equal((0.8, 1.2, 1.0), o.ScaleCycleTarget);
        Assert.True(o.ScaleCycleEnabled);
        Assert.Equal((1.0, 1.0, 1.0), o.ScaleCurrent);
    }

    [Fact]
    public void ResetCyclicAnimationChannels_StopsEveryLoopAndPreservesBaseAndOneShotState()
    {
        var g = new GfxState();
        g.SetSurface(1, 5, -1);
        g.BindDraw(7, 1, 0, 0, 64, 64, 12, 34);
        var o = g.TryGet(7)!;
        o.V18 = (10, 20, 30);
        o.V24 = (12, 34, 56);
        o.V16c = (7, 8, 9);
        o.ScaleCurrent = (1.25, 0.75, 2);
        o.TranslationCurrent = (4, 5, 6);
        o.RotationCurrent = (0, 0, 1, 15);
        o.ScaleEnabled = true;
        o.ScaleDurationMs = 200;
        o.OneShotStartMs = 123;
        o.NativePersistenceRecord = Enumerable.Repeat((byte)0x7f, 0x2d4).ToArray();

        g.SetColorAnim(7, 400, GfxState.PackColor(0x80, 0xff0000));
        g.SetScaleCycle(7, 600, (150, 75, 100));
        g.SetRotationCycle(7, 800, (0, 0, 1));
        g.SetSrcRect(7, frameCount: 8, columns: 4, cell: 3, period: 100);

        g.ResetCyclicAnimationChannels(7);

        Assert.False(o.ColorAnim);
        Assert.False(o.ScaleCycleEnabled);
        Assert.False(o.RotationEnabled);
        Assert.False(o.SrcAnim);
        Assert.Equal((0L, 0L, 0L, 0L), (o.ColorPeriod, o.ScaleCyclePeriodMs, o.RotationPeriodMs, o.SrcPeriod));
        Assert.Equal((-1L, -1L, -1L, -1L), (o.ColorStart, o.ScaleCycleStartMs, o.RotationStartMs, o.SrcStart));
        Assert.Equal((10L, 20L, 30L), o.V18);
        Assert.Equal((12L, 34L, 56L), o.V24);
        Assert.Equal((7L, 8L, 9L), o.V16c);
        Assert.Equal((1.25, 0.75, 2.0), o.ScaleCurrent);
        Assert.Equal((4.0, 5.0, 6.0), o.TranslationCurrent);
        Assert.Equal((0.0, 0.0, 1.0, 15.0), o.RotationCurrent);
        Assert.True(o.ScaleEnabled);
        Assert.Equal(200, o.ScaleDurationMs);
        Assert.Equal(123, o.OneShotStartMs);
        Assert.Equal((1.5, 0.75, 1.0), o.ScaleCycleTarget);
        Assert.Equal((0L, 0L, 1L), o.RotationAxis);
        Assert.Equal((8L, 4L, 3L), (o.SrcFrameCount, o.SrcColumns, o.SrcCell));
        Assert.Equal(0, o.NativePersistenceRecord[0] & 4);
        Assert.All(o.NativePersistenceRecord.Skip(0x20c).Take(0x28), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Op0x230_DispatchGetsOrCreatesObjectAndRemovesContinuousPresentation()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "RESET_CYCLES", new List<(int, Operand[])>
        {
            MovGI(1, 0x1000),
            (0x230, new[] { G(1) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Gfx.SetSurface(1, 5, -1);
        vm.Gfx.BindDraw(0x1000, 1, 0, 0, 64, 64, 0, 0);
        vm.Gfx.SetColorAnim(0x1000, 400, GfxState.PackColor(0x80, 0xff0000));
        vm.Gfx.SetScaleCycle(0x1000, 600, (150, 75, 100));
        vm.Gfx.SetRotationCycle(0x1000, 800, (0, 0, 1));
        vm.Gfx.SetSrcRect(0x1000, frameCount: 8, columns: 4, cell: 0, period: 100);
        Assert.True(vm.Gfx.HasActiveVisualPresentation(1000));

        vm.Run();

        Assert.NotNull(vm.Gfx.TryGet(0x1000));
        Assert.False(vm.Gfx.HasActiveVisualPresentation(1000));
        Assert.Equal(
            GfxPresentationReason.RetainedMutation,
            vm.Gfx.ConsumePresentationReasons(1000));

        var createOnly = new VirtualMachine(scene, t, new RecordingHost());
        createOnly.Run();
        Assert.NotNull(createOnly.Gfx.TryGet(0x1000));
    }

    [Fact]
    public void CurrentTranslationSetter_ReplacesTheLiveMatrixImmediately()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "CURRENTTRANSLATION", new List<(int, Operand[])>
        {
            MovGI(1, 0xcb20), MovGI(2, 12), MovGI(3, 34), MovGI(4, 5),
            (0x1ff, new[] { G(1), G(2), G(3), G(4) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());
        vm.Run();
        var o = vm.Gfx.TryGet(0xcb20)!;
        Assert.Equal((12.0, 34.0, 5.0), o.TranslationCurrent);
        Assert.Equal((12L, 34L, 5L), o.V16c);
    }

    [Fact]
    public void CurrentRotationSetter_ReplacesTheLiveAxisAngleImmediately()
    {
        var t = T();
        var scene = ScriptAssembler.Assemble(t, "CURRENTROTATION", new List<(int, Operand[])>
        {
            MovGI(1, 0xcb20), MovGI(2, 0), MovGI(3, 1), MovGI(4, 0), MovGI(5, 180),
            (0x1fe, new[] { G(1), G(2), G(3), G(4), G(5) }),
            Exit(),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(scene, t, new RecordingHost());

        vm.Run();

        var o = vm.Gfx.TryGet(0xcb20)!;
        Assert.Equal((0.0, 1.0, 0.0, 180.0), o.RotationCurrent);
        Assert.False(o.RotationChannelEnabled);
    }

    [Fact]
    public void ResetAnimClock_DispatchClearsGlobalServiceClock()
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
    public void SnapshotSamplesScaleCycleWithNativeTriangularPhase()
    {
        var g = new GfxState();
        g.SetSurface(1, 5, -1);
        g.BindDraw(7, 1, 0, 0, 64, 64, 0, 0);
        g.SetScaleCycle(7, 1200, (200, 50, 100));

        var start = g.SnapshotVisibleObjects(1000).Single().ScaleCycle;
        var quarter = g.SnapshotVisibleObjects(1300).Single().ScaleCycle;
        var midpoint = g.SnapshotVisibleObjects(1600).Single().ScaleCycle;
        var threeQuarter = g.SnapshotVisibleObjects(1900).Single().ScaleCycle;
        var wrapped = g.SnapshotVisibleObjects(2200).Single().ScaleCycle;

        Assert.Equal((1.0, 1.0, 1.0), (start.ScaleX, start.ScaleY, start.ScaleZ));
        Assert.Equal((1.5, 0.75, 1.0), (quarter.ScaleX, quarter.ScaleY, quarter.ScaleZ));
        Assert.Equal((2.0, 0.5, 1.0), (midpoint.ScaleX, midpoint.ScaleY, midpoint.ScaleZ));
        Assert.Equal((1.5, 0.75, 1.0), (threeQuarter.ScaleX, threeQuarter.ScaleY, threeQuarter.ScaleZ));
        Assert.Equal((1.0, 1.0, 1.0), (wrapped.ScaleX, wrapped.ScaleY, wrapped.ScaleZ));

        g.SetScaleCycle(7, 5, (200, 100, 100));
        _ = g.SnapshotVisibleObjects(3000);
        var oddPeriod = g.SnapshotVisibleObjects(3002).Single().ScaleCycle;
        Assert.Equal(1.8, oddPeriod.ScaleX, 10);
    }

    [Fact]
    public void ScaleCycleClonePreservesTargetAndSharedPhase()
    {
        var g = new GfxState();
        g.SetSurface(1, 5, -1);
        g.BindDraw(7, 1, 0, 0, 64, 64, 0, 0);
        g.SetScaleCycle(7, 1000, (160, 80, 100));
        _ = g.SnapshotVisibleObjects(2000);

        Assert.True(g.CloneObject(7, 8));
        var samples = g.SnapshotVisibleObjects(2250);
        var source = samples.Single(x => x.Handle == 7).ScaleCycle;
        var clone = samples.Single(x => x.Handle == 8).ScaleCycle;

        Assert.Equal(source, clone);
        Assert.Equal((1.3, 0.9, 1.0), (clone.ScaleX, clone.ScaleY, clone.ScaleZ));
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

    [Fact]
    public void Transform2D_CyclicRotationOccursAfterTranslation()
    {
        var t = new TransformState(2, 1, 1, 10, 0, 0, 100, 50, 0);
        var cycle = new RotationCycleState(true, 1000, 0, 0, 1, 90);
        var p = Transform2DMath.Apply(120, 50, t, cycle);
        Assert.Equal(100.0, p.X, 10);
        Assert.Equal(100.0, p.Y, 10);
    }

    [Fact]
    public void Transform2D_CyclicScaleOccursAfterTranslationAndBeforeCyclicRotation()
    {
        var t = new TransformState(2, 1, 1, 10, 0, 0, 100, 50, 0);
        var scale = new ScaleCycleState(true, 1000, 3, 1, 1);
        var rotation = new RotationCycleState(true, 1000, 0, 0, 1, 90);

        var p = Transform2DMath.Apply(120, 50, t, rotation, scale);

        Assert.Equal(100.0, p.X, 10);
        Assert.Equal(200.0, p.Y, 10);
    }
}
