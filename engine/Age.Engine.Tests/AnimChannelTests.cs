using Age.Engine.Model;
using Xunit;

public class AnimChannelTests
{
    private static GfxState VisibleObj(long handle)
    {
        var g = new GfxState();
        g.SetSurface(1, resId: 5, colorKey: -1);
        g.BindDraw(handle, 1, 0, 0, 256, 64, 100, 100);   // 256x64 sheet at base pos (100,100)
        return g;
    }

    [Fact]
    public void SetSrcRect_StoresGridCellPeriod()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRect(0x100, gridW: 4, gridH: 1, cell: 2, period: 800);
        var o = g.TryGet(0x100)!;
        Assert.Equal(4, o.SrcGridW);
        Assert.Equal(2, o.SrcCell);
        Assert.Equal(800, o.SrcPeriod);
        Assert.True(o.SrcAnim);
        Assert.Equal(-1, o.SrcStart);   // uninitialized until first interpolated frame
    }

    [Fact]
    public void SetSrcRect_ClampsGridToAtLeastOne()
    {
        var g = VisibleObj(0x100);
        g.SetSrcRect(0x100, gridW: 0, gridH: 0, cell: 0, period: 0);
        var o = g.TryGet(0x100)!;
        Assert.Equal(1, o.SrcGridW);
        Assert.Equal(1, o.SrcGridH);
    }

    [Fact]
    public void SetColorAnim_StoresPeriodAndTarget()
    {
        var g = VisibleObj(0x100);
        long target = GfxState.PackColor(0x80, 0xFF0000);   // half-alpha red glow
        g.SetColorAnim(0x100, period: 1000, target: target);
        var o = g.TryGet(0x100)!;
        Assert.Equal(1000, o.ColorPeriod);
        Assert.Equal(target, o.ColorTarget);
        Assert.True(o.ColorAnim);
    }
}
