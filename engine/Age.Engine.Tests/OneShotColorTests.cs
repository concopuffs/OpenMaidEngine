using Age.Engine.Model;

namespace Age.Engine.Tests;

public class OneShotColorTests
{
    [Fact]
    public void TimedPresentation_TracksOnlyVisibleFiniteChannelsUntilSampledComplete()
    {
        var visible = Visible(GfxState.PackColor(0, 0xffffff));
        visible.SetAnimatedObjectColorResolved(0x100, 0, 100, 0xff, 0xffffff);
        Assert.True(visible.HasActiveTimedPresentation(1000));
        visible.SnapshotVisibleObjects(1000);
        visible.SnapshotVisibleObjects(1100);
        Assert.False(visible.HasActiveTimedPresentation(1100));

        var unbound = new GfxState();
        unbound.SetAnimatedObjectColorResolved(0x200, 0, 100, 0xff, 0xffffff);
        Assert.False(unbound.HasActiveTimedPresentation(1000));
    }

    private static GfxState Visible(long current)
    {
        var gfx = new GfxState();
        gfx.SetSurface(1, 0x23, -1);
        gfx.BindDraw(0x100, 1, 0, 0, 100, 100, 0, 0);
        gfx.SetObjectColor(0x100, current);
        return gfx;
    }

    [Fact]
    public void DelayedColor_UsesCurrentTargetAndCommitsWithNativeIntegerLerp()
    {
        var gfx = Visible(GfxState.PackColor(0x00, 0x102030));
        gfx.SetAnimatedObjectColorResolved(0x100, 100, 400, 0xff, 0x90a0b0);

        var start = gfx.SnapshotVisibleObjects(1000).Single();
        Assert.Equal(0, start.Alpha);
        Assert.True(start.MultiplyTint);
        Assert.Equal(0.0, start.ColorTransition!.Value.Progress);
        Assert.Equal(0, gfx.SnapshotVisibleObjects(1100).Single().Alpha);

        var half = gfx.SnapshotVisibleObjects(1300).Single();
        Assert.Equal(0x7f, half.Alpha);
        Assert.Equal(0x506070, half.Tint);
        Assert.Equal(0.5, half.ColorTransition!.Value.Progress, 3);

        var end = gfx.SnapshotVisibleObjects(1500).Single();
        Assert.Equal(0xff, end.Alpha);
        Assert.True(end.MultiplyTint); // committed endpoints keep the op-0x202 blend contract
        Assert.Equal(0x90a0b0, end.Tint);
        Assert.False(gfx.TryGet(0x100)!.OneShotColorEnabled);
        Assert.Equal(-1, gfx.TryGet(0x100)!.OneShotColorTarget);
    }

    [Fact]
    public void StaticWriteAfterOp202_BecomesInterpolationCurrent_NotAnEndpointOverwrite()
    {
        var gfx = Visible(GfxState.PackColor(0xff, 0x000000));
        gfx.SetAnimatedObjectColorResolved(0x100, 0, 300, 0x00, 0xffffff);
        gfx.SetStaticObjectColorResolved(0x100, 0, 0xff, 0xffffff);

        var start = gfx.SnapshotVisibleObjects(2000).Single();
        Assert.Equal(0xff, start.Alpha);
        Assert.Equal(0xffffff, start.Tint);
        var half = gfx.SnapshotVisibleObjects(2150).Single();
        Assert.Equal(0x7f, half.Alpha);
        Assert.Equal(0xffffff, half.Tint);
    }

    [Fact]
    public void ColorAndScale_ShareOneShotStartClock()
    {
        var gfx = Visible(GfxState.PackColor(0x00, 0xffffff));
        gfx.SetAnimatedObjectColorResolved(0x100, 0, 1000, 0xff, 0xffffff);
        gfx.SetScaleChannel(0x100, 0, 1000, (200, 200, 100));

        gfx.SnapshotVisibleObjects(5000);
        var half = gfx.SnapshotVisibleObjects(5500).Single();
        Assert.Equal(0x7f, half.Alpha);
        Assert.Equal(1.5, half.Transform.ScaleX, 3);
        Assert.Equal(5000, half.ColorTransition!.Value.StartMs);
    }

    [Fact]
    public void NegativeTargetComponents_PreserveCurrentStaticBytes()
    {
        var gfx = Visible(GfxState.PackColor(0x33, 0x123456));
        gfx.SetAnimatedObjectColorResolved(0x100, 0, 100, -1, -1);
        Assert.Equal(GfxState.PackColor(0x33, 0x123456), gfx.TryGet(0x100)!.OneShotColorTarget);
    }
}
