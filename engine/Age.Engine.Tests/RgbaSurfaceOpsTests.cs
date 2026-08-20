using Age.Engine.Sys4;
using Age.Engine.Model;
using Xunit;

public class RgbaSurfaceOpsTests
{
    [Fact]
    public void FillRect_ReplacesClippedPixelsIncludingAlpha()
    {
        var surface = Row(1, 2, 3, 4);

        Assert.True(RgbaSurfaceOps.FillRect(surface, -1, 0, 3, 1, 0xd0, 0x112233));

        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0xd0 }, surface.Pixels[..4]);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0xd0 }, surface.Pixels[4..8]);
        Assert.Equal(3, Values(surface)[2]);
        Assert.Equal(4, Values(surface)[3]);
    }

    [Fact]
    public void GeneratedSurfaceBinding_DefaultsToNoColorKey()
    {
        var gfx = new GfxState();
        gfx.CreateSurface(67);
        gfx.BindDraw(1, 67, 0, 0, 4, 1, 0, 0);

        Assert.Equal(-1, Assert.Single(gfx.SnapshotVisibleObjects()).ColorKey);
    }

    [Fact]
    public void CopyRect_ClipsSourceAndDestinationAsOnePairedRectangle()
    {
        var source = Row(10, 20, 30, 40);
        var destination = Row(1, 2, 3, 4);

        Assert.True(RgbaSurfaceOps.CopyRect(source, destination, -1, 0, 4, 1, 1, 0));

        Assert.Equal(new byte[] { 1, 2, 10, 20 }, Values(destination));
    }

    [Fact]
    public void CopyRect_UsesStableSourcePixelsForOverlappingSelfCopy()
    {
        var surface = Row(10, 20, 30, 40);

        Assert.True(RgbaSurfaceOps.CopyRect(surface, surface, 0, 0, 3, 1, 1, 0));

        Assert.Equal(new byte[] { 10, 10, 20, 30 }, Values(surface));
    }

    [Fact]
    public void ScaleCopyRect_AreaAveragesSourcePixelsWhenShrinking()
    {
        var source = new RgbaImage(2, 2, new byte[]
        {
            0, 0, 0, 0,       40, 80, 120, 160,
            80, 120, 160, 200, 120, 160, 200, 240,
        });
        var destination = new RgbaImage(1, 1, new byte[4]);

        Assert.True(RgbaSurfaceOps.ScaleCopyRect(source, destination, 0, 0, 2, 2, 0, 0, 1, 1));

        Assert.Equal(new byte[] { 60, 90, 120, 150 }, destination.Pixels);
    }

    [Fact]
    public void WithColorKey_MakesMatchingSourcePixelsTransparentBeforeComposition()
    {
        var source = new RgbaImage(2, 1, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 });

        var keyed = RgbaSurfaceOps.WithColorKey(source, 0x010203);

        Assert.Equal(0, keyed.Pixels[3]);
        Assert.Equal(255, keyed.Pixels[7]);
        Assert.Equal(255, source.Pixels[3]);
    }

    private static RgbaImage Row(params byte[] values)
    {
        var pixels = new byte[values.Length * 4];
        for (int index = 0; index < values.Length; index++)
        {
            pixels[index * 4] = values[index];
            pixels[index * 4 + 3] = 255;
        }
        return new RgbaImage(values.Length, 1, pixels);
    }

    private static byte[] Values(RgbaImage image)
    {
        var values = new byte[image.Width];
        for (int index = 0; index < values.Length; index++) values[index] = image.Pixels[index * 4];
        return values;
    }
}
