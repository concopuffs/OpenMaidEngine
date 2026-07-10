using System.Linq;
using Age.Engine.Model;
using Xunit;

public class RenderObjectBlendTests
{
    [Fact]
    public void NegativeColorOperands_PreserveCurrentStaticColor()
    {
        var g = new GfxState();
        g.SetObjectColor(0x100, GfxState.PackColor(0, 0xffffff));
        g.SetObjectColorResolved(0x100, -1, -1);
        Assert.Equal(GfxState.PackColor(0, 0xffffff), g.TryGet(0x100)!.Color);
    }

    [Fact]
    public void TransitionColorMode_TreatsWhiteAsIdentityAndAlphaAsOpacity()
    {
        var g = new GfxState();
        g.SetSurface(4, 1, -1);
        g.BindDraw(0x100, 4, 0, 0, 1, 1, 0, 0);
        g.SetStaticObjectColorResolved(0x100, 2, -1, -1);
        var rendered = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, rendered.Alpha);
        Assert.Equal(0, rendered.TintStrength);
    }

    // Make a visible textured object bound to a slot that has a surface, so it appears in the snapshot.
    private static GfxState WithVisibleObject(long handle, long resId, long colorKey)
    {
        var g = new GfxState();
        g.SetSurface(1, resId, colorKey);
        g.BindDraw(handle, 1, 0, 0, 10, 10, 0, 0);   // draw-texture: slot 1, 10x10 at (0,0), visible
        return g;
    }

    [Fact]
    public void ObjectWithoutColor_ResolvesOpaqueNoTint()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, ro.Alpha);          // opaque
        Assert.Equal(0, ro.TintStrength);     // no tint
        Assert.Equal(0xFFFFFF, ro.Tint);
        Assert.Equal(BlendKind.Opaque, ro.Blend);
    }

    [Fact]
    public void DrawColor_0x203_IsTintStrength_ObjectStaysOpaque()
    {
        // Op 0x203/0x202 alpha is TINT-BLEND STRENGTH, not object opacity (evidence: a CG drawn with
        // (alpha=0, color=white) must stay fully OPAQUE + untinted, not vanish). Root cause of the grey-BG.
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        g.SetObjectColor(0x100, GfxState.PackColor(0x00, 0xFFFFFF));   // "no tint" — the grey-BG case
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, ro.Alpha);          // OBJECT STAYS OPAQUE (was wrongly 0 -> invisible)
        Assert.Equal(0, ro.TintStrength);     // zero tint strength

        g.SetObjectColor(0x100, GfxState.PackColor(0x80, 0x102030));   // half-strength tint toward 0x102030
        var ro2 = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, ro2.Alpha);         // still opaque
        Assert.Equal(0x80, ro2.TintStrength); // strength from the alpha byte
        Assert.Equal(0x102030, ro2.Tint);
        Assert.Equal(BlendKind.Alpha, ro2.Blend);
    }

    [Fact]
    public void ColorKey_IsCarriedThrough()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: 0x000000);   // key black
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(0x000000, ro.ColorKey);
    }
}
