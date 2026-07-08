using System.Linq;
using Age.Engine.Model;
using Xunit;

public class RenderObjectBlendTests
{
    // Make a visible textured object bound to a slot that has a surface, so it appears in the snapshot.
    private static GfxState WithVisibleObject(long handle, long resId, long colorKey)
    {
        var g = new GfxState();
        g.SetSurface(1, resId, colorKey);
        g.BindDraw(handle, 1, 0, 0, 10, 10, 0, 0);   // draw-texture: slot 1, 10x10 at (0,0), visible
        return g;
    }

    [Fact]
    public void ObjectWithoutColor_ResolvesOpaqueWhiteTint()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, ro.Alpha);
        Assert.Equal(0xFFFFFF, ro.Tint);
        Assert.Equal(BlendKind.Opaque, ro.Blend);
    }

    [Fact]
    public void DrawColor_0x203_SetsAlphaTintAndBlend()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        // emulate op 0x203: pack (alpha=0x80, color=0x102030) and mark HasColor
        g.SetObjectColor(0x100, GfxState.PackColor(0x80, 0x102030));
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(0x80, ro.Alpha);
        Assert.Equal(0x102030, ro.Tint);
        Assert.Equal(BlendKind.Alpha, ro.Blend);
    }

    [Fact]
    public void ColorKey_IsCarriedThrough()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: 0x000000);   // key black
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(0x000000, ro.ColorKey);
    }
}
