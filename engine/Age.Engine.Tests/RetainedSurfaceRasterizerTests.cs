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

    [Fact]
    public void SurfacelessZeroSizedFillUsesDestinationCanvasRatherThanLegacyProfileSize()
    {
        var fill = new RenderObject(
            Handle: 100, SurfaceResId: 0, ColorKey: 0,
            SrcX: 0, SrcY: 0, W: 0, H: 0, DstX: 0, DstY: 0,
            Transform: new TransformState(1, 1, 1, 0, 0, 0, 0, 0, 0),
            Rotation: new RotationCycleState(false, 0, 0, 0, 0),
            Alpha: 255, Tint: 0x336699, TintStrength: 0,
            Blend: BlendKind.Alpha, MultiplyTint: true);
        var destination = new RgbaImage(1024, 576, new byte[1024 * 576 * 4]);

        int rendered = RetainedSurfaceRasterizer.CompositeRange(
            destination, [fill], 100, 1, _ => null);

        Assert.Equal(1, rendered);
        AssertPixel(destination, 0, 0, 0x33, 0x66, 0x99, 0xff);
        AssertPixel(destination, 1023, 575, 0x33, 0x66, 0x99, 0xff);
    }

    private static void AssertPixel(
        RgbaImage image, int x, int y, byte red, byte green, byte blue, byte alpha)
    {
        int offset = (y * image.Width + x) * 4;
        Assert.Equal(red, image.Pixels[offset]);
        Assert.Equal(green, image.Pixels[offset + 1]);
        Assert.Equal(blue, image.Pixels[offset + 2]);
        Assert.Equal(alpha, image.Pixels[offset + 3]);
    }
}
