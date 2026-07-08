using Age.Engine.Model;
using Xunit;

public class GfxStateTests
{
    [Fact]
    public void QueryRegistryIsPopulatedOnlyByRegister_NotByGeometryOps()
    {
        // Native contract (docs/engine-re.md op 0x215/0x1a2): the op-0x215 query registry is populated ONLY by
        // op 0x1a2 (gfx-cmd-register). Merely giving a handle geometry (GetOrCreate, as the set-geom ops do)
        // must NOT make query-gfx-object return a slot for it — otherwise a CG handle (never 0x1a2-registered)
        // wrongly takes label_12649's existing branch and collapses off-screen.
        var g = new GfxState();
        g.GetOrCreate(0xcb2a).V18 = (400, 600, 0);   // geometry only, like the fresh CG-load branch
        Assert.Equal(-1, g.QuerySlot(0xcb2a));        // NOT registered => -1 => fresh branch (correct)

        g.Register(0xd);                               // op 0x1a2 registers a small system/UI handle
        Assert.Equal(0xd, g.QuerySlot(0xd));           // native map[handle]=handle; the value doubles as its slot
        Assert.Equal(-1, g.QuerySlot(0x9999));         // unknown -> -1 (matches native 0xffffffff)
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
    public void ReleaseRemovesTheHandleFromTheQueryRegistry()
    {
        var g = new GfxState();
        g.Register(0x10);
        Assert.Equal(0x10, g.QuerySlot(0x10));
        g.Release(0x10);                          // op 0x1fa / 0x1f7 tear down the registration too
        Assert.Equal(-1, g.QuerySlot(0x10));
    }

    [Fact]
    public void PackColorPacksArgb()
        => Assert.Equal(0x80_112233L, GfxState.PackColor(0x80, 0x112233));

    [Fact]
    public void EraseRangeRemovesHandlesInRange()
    {
        var g = new GfxState();
        g.Register(0x10); g.Register(0x11); g.Register(0x12); g.Register(0x20);
        g.EraseRange(0x10, 3);                      // count>1 → erase [0x10, 0x13)
        Assert.Equal(-1, g.QuerySlot(0x10));
        Assert.Equal(-1, g.QuerySlot(0x12));
        Assert.NotEqual(-1, g.QuerySlot(0x20));     // outside the range, kept
    }

    [Fact]
    public void EraseRangeCountLeOneErasesSingleHandle()
    {
        var g = new GfxState();
        g.Register(0x10); g.Register(0x11);
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
}
