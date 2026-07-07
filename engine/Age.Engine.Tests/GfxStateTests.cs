using Age.Engine.Model;
using Xunit;

public class GfxStateTests
{
    [Fact]
    public void DistinctHandlesGetDistinctSlots()
    {
        var g = new GfxState();
        int s1 = g.GetOrCreate(0x1000).Slot;
        int s2 = g.GetOrCreate(0x2000).Slot;
        Assert.NotEqual(s1, s2);
        Assert.Equal(s1, g.QuerySlot(0x1000));   // stable
        Assert.Equal(-1, g.QuerySlot(0x9999));    // unknown -> -1 (matches native 0xffffffff)
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
    public void ReleaseFreesTheSlotForReuse()
    {
        var g = new GfxState();
        int s1 = g.GetOrCreate(0x1000).Slot;
        g.Release(0x1000);
        Assert.Equal(-1, g.QuerySlot(0x1000));
        Assert.Equal(s1, g.GetOrCreate(0x2000).Slot);  // freed slot reused
    }

    [Fact]
    public void PackColorPacksArgb()
        => Assert.Equal(0x80_112233L, GfxState.PackColor(0x80, 0x112233));
}
