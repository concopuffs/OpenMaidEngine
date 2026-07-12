using Age.Engine.Model;
using Xunit;

public class GfxStateTests
{
    [Fact]
    public void QueryReturnsBoundSourceSlot_NotOperandRegistryValue()
    {
        // Native op 0x215 queries the retained-object map and returns obj+4. The default initializer zeroes
        // that field, while draw-texture replaces it with the bound slot. Op 0x1a2 is a separate registry.
        var g = new GfxState();
        g.GetOrCreate(0xcb2a).V18 = (400, 600, 0);
        Assert.Equal(0, g.QuerySlot(0xcb2a));

        g.Register(0xcb2a);
        Assert.Equal(0, g.QuerySlot(0xcb2a));

        g.BindDraw(0xcb2a, 6, 0, 0, 200, 200, 10, 20);
        Assert.Equal(6, g.QuerySlot(0xcb2a));
        Assert.Equal(-1, g.QuerySlot(0x9999));
    }

    [Fact]
    public void UnboundMatrixSetterCreatesQueryableObjectForCleanupBeforeReuse()
    {
        // Native matrix setters create a neutral placeholder even when they ignore the channel because it is
        // not draw-bound. obj+4 still defaults to slot 0, so the later query >= 0 cleanup erases the object.
        var g = new GfxState();
        g.SetTranslationChannel(0xcb2a, 0, 150, (-100, 0, 0));
        g.SetRotationChannel(0xcb2a, 0, 150, (0, 0, 1), -90);
        Assert.Equal(0, g.QuerySlot(0xcb2a));

        if (g.QuerySlot(0xcb2a) >= 0)
            g.EraseRange(0xcb2a, 10);

        g.SetSurface(5, 0x7a, 0);
        g.BindDraw(0xcb2a, 5, 0, 0, 800, 600, 0, 0);
        var reused = Assert.Single(g.SnapshotVisibleObjects(1000));
        Assert.Equal((0.0, 0.0, 0.0),
            (reused.Transform.TranslateX, reused.Transform.TranslateY, reused.Transform.RotationAngleDegrees));
        Assert.Equal(255, reused.Alpha);
    }

    [Fact]
    public void VectorsRoundTripPerObject()
    {
        var g = new GfxState();
        g.GetOrCreate(0x1000).V18 = (10, 20, 30);
        g.GetOrCreate(0x1000).V24 = (40, 50, 60);
        Assert.Equal((10L, 20L, 30L), g.TryGet(0x1000)!.V18);
        Assert.Equal((40L, 50L, 60L), g.TryGet(0x1000)!.V24);
        Assert.Null(g.TryGet(0x2000));            // untouched handle absent
    }

    [Fact]
    public void ReleaseRemovesTheRetainedObject()
    {
        var g = new GfxState();
        g.BindDraw(0x10, 4, 0, 0, 10, 10, 0, 0);
        Assert.Equal(4, g.QuerySlot(0x10));
        g.Release(0x10);
        Assert.Equal(-1, g.QuerySlot(0x10));
    }

    [Fact]
    public void PackColorPacksArgb()
        => Assert.Equal(0x80_112233L, GfxState.PackColor(0x80, 0x112233));

    [Fact]
    public void EraseRangeRemovesHandlesInRange()
    {
        var g = new GfxState();
        g.BindDraw(0x10, 1, 0, 0, 1, 1, 0, 0); g.BindDraw(0x11, 2, 0, 0, 1, 1, 0, 0);
        g.BindDraw(0x12, 3, 0, 0, 1, 1, 0, 0); g.BindDraw(0x20, 4, 0, 0, 1, 1, 0, 0);
        g.EraseRange(0x10, 3);                      // count>1 → erase [0x10, 0x13)
        Assert.Equal(-1, g.QuerySlot(0x10));
        Assert.Equal(-1, g.QuerySlot(0x12));
        Assert.NotEqual(-1, g.QuerySlot(0x20));     // outside the range, kept
    }

    [Fact]
    public void EraseRangeCountLeOneErasesSingleHandle()
    {
        var g = new GfxState();
        g.BindDraw(0x10, 1, 0, 0, 1, 1, 0, 0); g.BindDraw(0x11, 2, 0, 0, 1, 1, 0, 0);
        g.EraseRange(0x10, 1);                       // count<=1 → single handle
        Assert.Equal(-1, g.QuerySlot(0x10));
        Assert.NotEqual(-1, g.QuerySlot(0x11));
    }

    [Fact]
    public void BindDrawMakesAVisibleRenderObjectFromItsSurface()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x25, 0);                        // load resId 0x25 into surface slot 4
        g.BindDraw(0xcb2a, 4, 0, 0, 800, 600, 0, 0);     // object 0xcb2a draws surface 4 at (0,0)
        var vis = g.SnapshotVisibleObjects();
        Assert.Single(vis);
        Assert.Equal(0xcb2a, vis[0].Handle);
        Assert.Equal(0x25, vis[0].SurfaceResId);         // resolved from the object's live source slot
        Assert.Equal((800, 600, 0, 0), (vis[0].W, vis[0].H, vis[0].DstX, vis[0].DstY));
    }

    [Fact]
    public void VisibleObjectsComeInAscendingHandleOrder()   // ascending handle == the engine's z-order
    {
        var g = new GfxState();
        g.SetSurface(4, 0x1, 0); g.SetSurface(5, 0x2, 0);
        g.BindDraw(0xcf08, 5, 0, 0, 10, 10, 0, 0);        // higher handle (should be on top / last)
        g.BindDraw(0xcb20, 4, 0, 0, 10, 10, 0, 0);        // lower handle (behind / first)
        var vis = g.SnapshotVisibleObjects();
        Assert.Equal(2, vis.Count);
        Assert.Equal(0xcb20, vis[0].Handle);
        Assert.Equal(0xcf08, vis[1].Handle);
    }

    [Fact]
    public void EraseRangeRemovesTheObjectFromCompositing()
    {
        var g = new GfxState();
        g.SetSurface(4, 0x1, 0);
        g.BindDraw(0x10, 4, 0, 0, 10, 10, 0, 0);
        g.EraseRange(0x10, 1);
        Assert.Empty(g.SnapshotVisibleObjects());        // erased => gone from the registry => not composited
    }

    [Fact]
    public void Sc0000EffectCleanupQueryEnablesObjectEraseAndSurfaceRelease()
    {
        var g = new GfxState();
        g.SetSurface(6, 0x37, 0);
        g.BindDraw(0xcb8e, 6, 0, 0, 200, 200, 300, 180);

        int slot = g.QuerySlot(0xcb8e);              // mirrors post-effect cleanup at SC0000 0x3321
        Assert.Equal(6, slot);
        g.EraseRange(0xcb8e, 10);                    // op 0x1f7
        g.ClearSurface(slot);                         // op 0x1fa

        Assert.Empty(g.SnapshotVisibleObjects());
        Assert.Equal(-1, g.QuerySlot(0xcb8e));
    }
}
