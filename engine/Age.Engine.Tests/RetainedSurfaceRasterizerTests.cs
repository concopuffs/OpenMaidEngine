using Age.Engine.Model;
using Age.Engine.Sys4;

public class RetainedSurfaceRasterizerTests
{
    [Fact]
    public void CompositeRangePublishesScaledRetainedTextureIntoOffscreenSurface()
    {
        var gfx = new GfxState();
        gfx.SetSurface(2, 0x1234, -1);
        gfx.BindDraw(100, 2, 0, 0, 4, 4, 0, 0);
        gfx.SetCurrentScale(100, (50, 50, 100));
        gfx.SetSurface(3, 0x5678, -1);
        gfx.BindDraw(200, 3, 0, 0, 2, 2, 0, 0);

        byte[] sourcePixels = new byte[4 * 4 * 4];
        for (int index = 0; index < sourcePixels.Length; index += 4)
        {
            sourcePixels[index] = 0x44;
            sourcePixels[index + 1] = 0x88;
            sourcePixels[index + 2] = 0xcc;
            sourcePixels[index + 3] = 0xff;
        }
        var source = new RgbaImage(4, 4, sourcePixels);
        var excluded = new RgbaImage(2, 2, Enumerable.Repeat((byte)0xff, 16).ToArray());
        var destination = new RgbaImage(2, 2, new byte[16]);

        int rendered = RetainedSurfaceRasterizer.CompositeRange(
            destination, gfx.SnapshotVisibleObjects(), 100, 1,
            item => item.Handle == 100 ? source : excluded);

        Assert.Equal(1, rendered);
        for (int index = 0; index < destination.Pixels.Length; index += 4)
        {
            Assert.Equal(0x44, destination.Pixels[index]);
            Assert.Equal(0x88, destination.Pixels[index + 1]);
            Assert.Equal(0xcc, destination.Pixels[index + 2]);
            Assert.Equal(0xff, destination.Pixels[index + 3]);
        }
    }
}
