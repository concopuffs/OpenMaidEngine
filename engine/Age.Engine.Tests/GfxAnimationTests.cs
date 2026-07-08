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
}
