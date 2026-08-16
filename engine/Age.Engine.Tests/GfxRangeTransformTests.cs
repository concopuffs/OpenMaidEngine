using System.Collections.Generic;
using System.Linq;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Xunit;

public class GfxRangeTransformTests
{
    [Fact]
    public void RangeCameraCentersItsAnchorAndLeavesUiHandlesUnchanged()
    {
        var gfx = new GfxState();
        gfx.SetSurface(1, 1, -1);
        gfx.BindDraw(10, 1, 0, 0, 1, 1, 500, 350); // selected map object at the camera anchor
        gfx.BindDraw(20, 1, 0, 0, 1, 1, 500, 350); // screen-fixed UI object outside the range
        gfx.SetRangeTransform(10, 1, (500, 350, 0));
        gfx.SetRangeTranslationCurrent((-100, -50, 0));
        gfx.SetRangeScaleCurrent((150, 150, 100));

        var objects = gfx.SnapshotVisibleObjects();
        var map = objects.Single(x => x.Handle == 10);
        var ui = objects.Single(x => x.Handle == 20);
        var mapOrigin = Transform2DMath.Build(map.Transform).FromLocalOrigin(map.DstX, map.DstY)
            .Then(map.RangeTransform!.Value).Apply(0, 0);
        var uiOrigin = Transform2DMath.Build(ui.Transform).FromLocalOrigin(ui.DstX, ui.DstY).Apply(0, 0);

        Assert.Equal((400.0, 300.0), mapOrigin);
        Assert.Null(ui.RangeTransform);
        Assert.Equal((500.0, 350.0), uiOrigin);
    }

    [Fact]
    public void RangeScaleTargetUsesTheOrdinaryOneShotClock()
    {
        var gfx = new GfxState();
        gfx.SetSurface(1, 1, -1);
        gfx.BindDraw(10, 1, 0, 0, 1, 1, 410, 300);
        gfx.SetRangeTransform(10, 1, (400, 300, 0));
        gfx.SetRangeScaleCurrent((100, 100, 100));
        gfx.SetRangeScaleChannel(delayMs: 0, durationMs: 300, percent: (200, 200, 100));

        gfx.SnapshotVisibleObjects(1000); // seeds the native-style shared channel start
        var halfway = gfx.SnapshotVisibleObjects(1150).Single();
        var p = Transform2DMath.Build(halfway.Transform).FromLocalOrigin(halfway.DstX, halfway.DstY)
            .Then(halfway.RangeTransform!.Value).Apply(0, 0);

        Assert.Equal((415.0, 300.0), p);
        Assert.True(gfx.HasActiveTimedPresentation(1150));
        gfx.SnapshotVisibleObjects(1300);
        Assert.False(gfx.HasActiveTimedPresentation(1300));
    }

    [Fact]
    public void Opcode22fPansTheSelectedRangeInSyncWithAnOrdinaryObjectTween()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "RANGE_PAN", new List<(int, Operand[])>
        {
            (0x229, new[] { I(1), I(1), I(500), I(350), I(0) }),
            (0x22c, new[] { I(100), I(50), I(0) }),
            (0x22f, new[] { I(0), I(300), I(300), I(150), I(0) }),
            (0x220, new[] { I(50001), I(0), I(300), I(100), I(50), I(0) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Gfx.SetSurface(1, 1, -1);
        vm.Gfx.BindDraw(1, 1, 0, 0, 1, 1, 500, 350);
        vm.Gfx.BindDraw(50001, 1, 0, 0, 1, 1, 0, 0);
        vm.Run();
        vm.Gfx.SnapshotVisibleObjects(1000); // seed the shared one-shot start
        var halfway = vm.Gfx.SnapshotVisibleObjects(1150);
        var map = halfway.Single(x => x.Handle == 1);
        var marker = halfway.Single(x => x.Handle == 50001);
        var mapPoint = Transform2DMath.Build(map.Transform).FromLocalOrigin(map.DstX, map.DstY)
            .Then(map.RangeTransform!.Value).Apply(0, 0);
        var markerPoint = Transform2DMath.Build(marker.Transform)
            .FromLocalOrigin(marker.DstX, marker.DstY).Apply(0, 0);

        Assert.Equal((700.0, 450.0), mapPoint);
        Assert.Equal((50.0, 25.0), markerPoint);
        Assert.Null(vm.Gfx.TryGet(0));
        Assert.True(vm.Gfx.HasActiveTimedPresentation(1150));
        vm.Gfx.SnapshotVisibleObjects(1300);
        Assert.False(vm.Gfx.HasActiveTimedPresentation(1300));
    }

    [Fact]
    public void Opcode229SelectsRangeWithoutCreatingAnOrdinaryObject()
    {
        var table = OpcodeTableJson.Load(Paths.OpcodesJson);
        var script = ScriptAssembler.Assemble(table, "RANGE", new List<(int, Operand[])>
        {
            (0x229, new[] { I(1), I(100), I(400), I(300), I(0) }),
            (0x22c, new[] { I(0), I(0), I(0) }),
            (0x22a, new[] { I(100), I(100), I(100) }),
            (0x2, System.Array.Empty<Operand>()),
        }, System.Array.Empty<string>());
        var vm = new VirtualMachine(script, table, new RecordingHost());

        vm.Run();

        Assert.Null(vm.Gfx.TryGet(1));
        vm.Gfx.SetSurface(1, 1, -1);
        vm.Gfx.BindDraw(1, 1, 0, 0, 1, 1, 400, 300);
        Assert.NotNull(vm.Gfx.SnapshotVisibleObjects().Single().RangeTransform);
    }

    [Fact]
    public void BunkiAnimationPublicationExcludesStablePopupHandles()
    {
        var animationRange = new GfxHandleRange(0, 60000);

        Assert.True(animationRange.Contains(59999));
        Assert.False(animationRange.Contains(60000));
        Assert.False(animationRange.Contains(60050));
        Assert.True(GfxHandleRange.All.Contains(60050));
    }

    private static Operand I(long v) => new(0, v);
}
