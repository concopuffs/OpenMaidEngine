using System.Linq;
using Age.Engine.Model;
using Xunit;

public class AnimInterpolatorTests
{
    private static GfxState VisibleObj(long handle)
    {
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(handle, 1, 0, 0, 64, 64, 100, 100);   // one 64x64 cell, base pos (100,100)
        return g;
    }

    [Fact]
    public void PingPong_FoldsAtHalfPeriod()
    {
        Assert.Equal(0,   BlendMath.PingPong(now: 0,    start: 0, period: 1000));
        Assert.Equal(500, BlendMath.PingPong(now: 500,  start: 0, period: 1000));   // peak at half
        Assert.Equal(0,   BlendMath.PingPong(now: 1000, start: 0, period: 1000));   // back to base
    }

    [Fact]
    public void SrcRectOneShot_StepsRowMajorCellsOverDurationThenCollapses()
    {
        // BTL's sword clash: 28 cells in 7 columns over 1000 ms, flags 0.
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(0x100, 1, 0, 0, 140, 140, 330, 230);
        g.SetSrcRectOneShot(0x100, delayMs: 0, durationMs: 1000, frameCount: 28, columns: 7, flags: 0);
        Assert.True(g.HasActiveTimedPresentation(0));

        Assert.Empty(g.SnapshotVisibleObjects(1000));             // seeds start = 1000; empty until it passes
        var cell = g.SnapshotVisibleObjects(1500).Single();       // 500 * 28 / 1000 = cell 14: row 2, column 0
        Assert.Equal((0, 280, 140, 140), (cell.SrcX, cell.SrcY, cell.W, cell.H));
        var late = g.SnapshotVisibleObjects(1999).Single();       // 999 * 28 / 1000 = cell 27: row 3, column 6
        Assert.Equal((840, 420), (late.SrcX, late.SrcY));
        Assert.Empty(g.SnapshotVisibleObjects(2000));             // flag bit 0 clear: the rect collapses
        Assert.False(g.TryGet(0x100)!.SrcOneShotEnabled);
        Assert.Empty(g.SnapshotVisibleObjects(2500));
        g.BindDraw(0x100, 1, 0, 0, 140, 140, 330, 230);           // a new draw-texture draws again
        Assert.Single(g.SnapshotVisibleObjects(2600));
        Assert.False(g.HasActiveTimedPresentation(2001));
    }

    [Fact]
    public void SrcRectOneShot_FlagsSelectDelayCellAndCommittedLastCell()
    {
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(0x100, 1, 0, 0, 64, 32, 0, 0);
        g.SetSrcRectOneShot(0x100, delayMs: 100, durationMs: 400, frameCount: 4, columns: 2, flags: 3);

        var delayed = g.SnapshotVisibleObjects(0).Single();       // flag bit 1: base cell during the delay
        Assert.Equal((0, 0, 64, 32), (delayed.SrcX, delayed.SrcY, delayed.W, delayed.H));
        var second = g.SnapshotVisibleObjects(200).Single();      // 100 * 4 / 400 = cell 1
        Assert.Equal((64, 0), (second.SrcX, second.SrcY));
        var done = g.SnapshotVisibleObjects(500).Single();        // flag bit 0: last cell (row 1, column 1) kept
        Assert.Equal((64, 32, 64, 32), (done.SrcX, done.SrcY, done.W, done.H));
        Assert.Equal((64, 32, 64, 32), g.TryGet(0x100)!.SrcRect);
        Assert.Equal((64, 32), (g.SnapshotVisibleObjects(900).Single().SrcX, g.SnapshotVisibleObjects(900).Single().SrcY));
    }

    [Fact]
    public void SrcRectOneShot_ForcedCompletionCommitsByFlags()
    {
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(0x100, 1, 0, 0, 64, 32, 0, 0);
        g.SetSrcRectOneShot(0x100, delayMs: 0, durationMs: 400, frameCount: 4, columns: 2, flags: 1);
        g.SnapshotVisibleObjects(0);
        g.ResetAnimClock();
        Assert.False(g.TryGet(0x100)!.SrcOneShotEnabled);
        Assert.Equal((64, 32, 64, 32), g.TryGet(0x100)!.SrcRect);
    }

    [Fact]
    public void SrcRect_Animated_AdvancesOneCellPerPeriodAndWraps()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRect(0x100, frameCount: 4, columns: 4, period: 100);
        g.SnapshotVisibleObjects(0);                       // seeds start=0
        var second = g.SnapshotVisibleObjects(100).Single();
        Assert.Equal(64, second.SrcX);
        var fourth = g.SnapshotVisibleObjects(300).Single();
        Assert.Equal(192, fourth.SrcX);
        var wrapped = g.SnapshotVisibleObjects(400).Single();
        Assert.Equal(0, wrapped.SrcX);
    }

    [Fact]
    public void ColorAnim_Mode0Textured_UsesRgbModulationAndIgnoresAlpha()
    {
        var g = VisibleObj(0x100);
        g.SetColorAnim(0x100, period: 1000, target: GfxState.PackColor(0x80, 0xFF0000));
        g.SnapshotVisibleObjects(0);                       // seeds start=0
        var baseFrame = g.SnapshotVisibleObjects(0).Single();
        var peak = g.SnapshotVisibleObjects(500).Single();
        Assert.Equal((255, 0xffffffL, 0, true),
            (baseFrame.Alpha, baseFrame.Tint, baseFrame.TintStrength, baseFrame.MultiplyTint));
        Assert.Equal((255, 0xff0000L, 0, true),
            (peak.Alpha, peak.Tint, peak.TintStrength, peak.MultiplyTint));
    }

    [Fact]
    public void ColorAnim_Mode1_UsesSampledAlphaAsOpacity()
    {
        var g = VisibleObj(0x100);
        g.SetStaticObjectColorResolved(0x100, mode: 1, alpha: 255, rgb: 0xffffff);
        g.SetColorAnim(0x100, period: 1000, target: GfxState.PackColor(0x80, 0xffffff));
        g.SnapshotVisibleObjects(0);
        var peak = g.SnapshotVisibleObjects(500).Single();
        Assert.Equal(0x80, peak.Alpha);
        Assert.Equal(0, peak.TintStrength);
        Assert.True(peak.MultiplyTint);
    }

    [Fact]
    public void Ae001hMode0AlphaCycle_IsVisuallyInert()
    {
        var g = VisibleObj(0x100);
        g.SetColorAnimResolved(0x100, period: 1200, alpha: 224, rgb: -1);
        var start = g.SnapshotVisibleObjects(1000).Single();
        var peak = g.SnapshotVisibleObjects(1600).Single();
        Assert.Equal(0xe0ffffff, g.TryGet(0x100)!.ColorTarget);
        Assert.Equal((255, 0xffffffL, 0, true),
            (start.Alpha, start.Tint, start.TintStrength, start.MultiplyTint));
        Assert.Equal((start.Alpha, start.Tint, start.TintStrength, start.MultiplyTint),
            (peak.Alpha, peak.Tint, peak.TintStrength, peak.MultiplyTint));
    }
}
