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
    public void TexturedMode0_UsesOpaqueMultiplicativeRgbAndIgnoresPackedAlpha()
    {
        // Mode 0 is the native opaque textured path. Its packed alpha byte is neither object opacity nor
        // tint strength; RGB multiplicatively modulates the source, so white is identity.
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        g.SetObjectColor(0x100, GfxState.PackColor(0x00, 0xFFFFFF));   // "no tint" — the grey-BG case
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, ro.Alpha);          // opaque
        Assert.Equal(0, ro.TintStrength);     // zero tint strength
        Assert.True(ro.MultiplyTint);

        g.SetObjectColor(0x100, GfxState.PackColor(0x80, 0x102030));
        var ro2 = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, ro2.Alpha);         // packed alpha remains ignored
        Assert.Equal(0, ro2.TintStrength);
        Assert.Equal(0x102030, ro2.Tint);
        Assert.True(ro2.MultiplyTint);
        Assert.Equal(BlendKind.Alpha, ro2.Blend);
    }

    [Fact]
    public void FieldMovement_StaticAlphaSuppressesOnlyIdleLoopingClone()
    {
        const long prototype = 0x9c40;
        const long idle = prototype + 7;
        const long moving = prototype + 0x76c;

        var g = new GfxState();
        g.SetSurface(0x8e, 0x1234, 0x00ff00);

        // FIELD@0x9230: the shared zero-area prototype receives mode-0 color before its loop is armed.
        g.BindDraw(prototype, 0x3e, 0, 0, 0, 0, 0, 0);
        g.SetStaticObjectColorResolved(prototype, mode: 0, alpha: 0, rgb: 0);
        g.SetSrcRect(prototype, frameCount: 4, columns: 2, cell: 0, period: 200);

        // DRAWCH clones the prototype, then draw-texture makes the concrete unit visible.
        Assert.True(g.CloneObject(prototype, idle));
        g.BindDraw(idle, 0x8e, 0, 0, 32, 48, 100, 100);
        Assert.Equal(255, g.SnapshotVisibleObjects().Single(x => x.Handle == idle).Alpha);

        // FIELD@0x45c9 clones the idle unit for motion and @0x45dc suppresses the retained original.
        Assert.True(g.CloneObject(idle, moving));
        g.SetStaticObjectColorResolved(idle, mode: 0, alpha: 0, rgb: -1);
        var movingFrame = g.SnapshotVisibleObjects();
        Assert.Equal(0, movingFrame.Single(x => x.Handle == idle).Alpha);
        Assert.Equal(255, movingFrame.Single(x => x.Handle == moving).Alpha);

        // DRAWCH at the next tile republishes the idle object; the following static write can suppress
        // it for another route segment or restore it at alpha 255 when movement finishes.
        g.BindDraw(idle, 0x8e, 0, 0, 32, 48, 200, 100);
        Assert.Equal(255, g.SnapshotVisibleObjects().Single(x => x.Handle == idle).Alpha);
        g.SetStaticObjectColorResolved(idle, mode: 0, alpha: 255, rgb: 0xffffff);
        Assert.Equal(255, g.SnapshotVisibleObjects().Single(x => x.Handle == idle).Alpha);
    }

    [Fact]
    public void TransitionCleanup_Mode2ToMode0PreservedWhite_RemainsIdentity()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        g.SetStaticObjectColorResolved(0x100, 2, -1, -1); // 0x128de: transition source
        Assert.Equal(0, g.SnapshotVisibleObjects().Single().TintStrength);

        g.SetStaticObjectColorResolved(0x100, 0, -1, -1); // 0x12478: transition cleanup
        var rendered = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, rendered.Alpha);
        Assert.Equal(0xffffff, rendered.Tint);
        Assert.Equal(0, rendered.TintStrength);
        Assert.True(rendered.MultiplyTint);
    }

    [Fact]
    public void SurfacelessMode0_RetainsFillStrength()
    {
        var g = new GfxState();
        g.BindDraw(0x100, 1, 0, 0, 10, 10, 0, 0);
        g.SetObjectColor(0x100, GfxState.PackColor(0x80, 0x102030));
        var rendered = g.SnapshotVisibleObjects().Single();
        Assert.Equal(255, rendered.Alpha);
        Assert.Equal(0x80, rendered.TintStrength);
        Assert.False(rendered.MultiplyTint);
    }

    [Fact]
    public void CreatedSurfaceMode0_UsesPackedAlphaAsOpacityAndRgbAsModulation()
    {
        var g = new GfxState();
        g.CreateSurface(3);
        g.BindDraw(0x100, 3, 0, 0, 10, 10, 0, 0);
        g.SetStaticObjectColorResolved(0x100, 0, 0x40, 0x102030);

        var rendered = g.SnapshotVisibleObjects().Single();

        Assert.Equal(0, rendered.SurfaceResId);
        Assert.Equal(-1, rendered.ColorKey);
        Assert.Equal(0x40, rendered.Alpha);
        Assert.Equal(0, rendered.TintStrength);
        Assert.Equal(0x102030, rendered.Tint);
        Assert.True(rendered.MultiplyTint);
        Assert.Equal(BlendKind.Alpha, rendered.Blend);
    }

    [Fact]
    public void Mode1_UsesAdditiveBlendWithArgbSourceScaleAndRgbModulation()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        g.SetStaticObjectColorResolved(0x100, 1, 0x40, 0x80ff40);
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(0x40, ro.Alpha);
        Assert.Equal(0, ro.TintStrength);
        Assert.Equal(0x80ff40, ro.Tint);
        Assert.True(ro.MultiplyTint);
        Assert.Equal(BlendKind.Additive, ro.Blend);
    }

    [Fact]
    public void Mode0_OneShotColor_UsesOpacityAndMultiplicativeRgbAfterCommit()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: -1);
        g.SetAnimatedObjectColorResolved(0x100, 0, 100, 0x00, 0xffffff);
        g.SetStaticObjectColorResolved(0x100, 0, 0xff, 0xffffff);

        g.SnapshotVisibleObjects(1000); // seed
        var hidden = g.SnapshotVisibleObjects(1100).Single();
        Assert.Equal(0, hidden.Alpha);
        Assert.Equal(0, hidden.TintStrength);
        Assert.True(hidden.MultiplyTint);
        Assert.False(g.TryGet(0x100)!.OneShotColorEnabled);
    }

    [Fact]
    public void ColorKey_IsCarriedThrough()
    {
        var g = WithVisibleObject(0x100, resId: 5, colorKey: 0x000000);   // key black
        var ro = g.SnapshotVisibleObjects().Single();
        Assert.Equal(0x000000, ro.ColorKey);
    }
}
