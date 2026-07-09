using System.Linq;
using Age.Engine.Model;
using Xunit;

public class AnimInterpolatorTests
{
    private static GfxState VisibleObj(long handle)
    {
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(handle, 1, 0, 0, 256, 64, 100, 100);   // 256x64 sheet, base pos (100,100)
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
    public void SrcRect_StaticCell_SelectsSubRect()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRect(0x100, gridW: 4, gridH: 1, cell: 2, period: 0);   // static cell 2 of 4 across 256px
        var ro = g.SnapshotVisibleObjects(0).Single();
        Assert.Equal(128, ro.SrcX);   // cell 2 * 64
        Assert.Equal(64, ro.W);
    }

    [Fact]
    public void SrcRect_Animated_PingPongsCellAcrossGrid()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRect(0x100, gridW: 4, gridH: 1, cell: 0, period: 1000);
        g.SnapshotVisibleObjects(0);                       // seeds start=0
        var half = g.SnapshotVisibleObjects(500).Single();
        Assert.Equal(192, half.SrcX);                      // t=1 -> last cell (3) * 64
        var back = g.SnapshotVisibleObjects(1000).Single();
        Assert.Equal(0, back.SrcX);                        // ping-ponged back to cell 0
    }

    [Fact]
    public void ColorAnim_PingPongsAlphaTowardTarget()
    {
        var g = VisibleObj(0x100);
        // base = opaque white (no static color); target = alpha 0 (fade out) => pulses opaque<->transparent
        g.SetColorAnim(0x100, period: 1000, target: GfxState.PackColor(0x00, 0x000000));
        g.SnapshotVisibleObjects(0);                       // seeds start=0
        var opaque = g.SnapshotVisibleObjects(0).Single();
        Assert.Equal(255, opaque.Alpha);                   // t=0 -> base opaque
        var peak = g.SnapshotVisibleObjects(500).Single();
        Assert.Equal(0, peak.Alpha);                       // t=1 -> target alpha 0
    }
}
