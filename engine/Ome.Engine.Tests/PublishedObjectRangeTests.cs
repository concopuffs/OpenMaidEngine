using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using static GraphicsTestSupport;

/// <summary>A render-target publication (ops 0x222 and 0x20c into a selected surface) and a movie mask's capture draw
/// their range through the frame lowering's plain path (Natives/Graphics.Frame.cs) and the reference renderer's
/// rules.</summary>
public class PublishedObjectRangeTests
{
    [Fact]
    public void APublishedRangeDrawsItsScaledObjectAndNotTheOthers()
    {
        var graphics = Graphics.Headless();
        GfxState gfx = graphics.State;
        graphics.CreateSurface(2, 4, 4);
        graphics.PublishSurfacePixels(2, Image(4, 4, (_, _) => [0x44, 0x88, 0xcc, 0xff]));
        gfx.BindDraw(100, 2, 0, 0, 4, 4, 0, 0);
        gfx.SetCurrentScale(100, (50, 50, 100));
        graphics.CreateSurface(3, 2, 2);
        graphics.PublishSurfacePixels(3, Image(2, 2, (_, _) => [0xff, 0xff, 0xff, 0xff]));
        gfx.BindDraw(200, 3, 0, 0, 2, 2, 0, 0);
        var destination = new RgbaImage(2, 2, new byte[16]);

        int rendered = graphics.DrawObjectRange(destination, gfx.SnapshotVisibleObjects(), 100, 1);

        Assert.Equal(1, rendered);
        for (int index = 0; index < destination.Pixels.Length; index += 4)
            Assert.Equal([0x44, 0x88, 0xcc, 0xff], destination.Pixels.AsSpan(index, 4).ToArray());
    }

    [Fact]
    public void ASurfacelessZeroSizedFillCoversTheDestinationRatherThanTheScreen()
    {
        var graphics = Graphics.Headless();
        var fill = new RenderObject(
            Handle: 100, SurfaceResId: 0, ColorKey: 0,
            SrcX: 0, SrcY: 0, W: 0, H: 0, DstX: 0, DstY: 0,
            Transform: new TransformState(1, 1, 1, 0, 0, 0, 0, 0, 0),
            Rotation: new RotationCycleState(false, 0, 0, 0, 0),
            Alpha: 255, Tint: 0x336699, TintStrength: 0,
            Blend: BlendKind.Alpha, MultiplyTint: true);
        var destination = new RgbaImage(1024, 576, new byte[1024 * 576 * 4]);

        int rendered = graphics.DrawObjectRange(destination, [fill], 100, 1);

        Assert.Equal(1, rendered);
        AssertPixel(destination, 0, 0, 0x33, 0x66, 0x99, 0xff);
        AssertPixel(destination, 1023, 575, 0x33, 0x66, 0x99, 0xff);
    }

    [Fact]
    public void AnObjectWhoseSourceRectangleStartsOffItsTextureDrawsNothingInAPublication()
    {
        var graphics = Graphics.Headless();
        GfxState gfx = graphics.State;
        graphics.CreateSurface(2, 2, 2);
        graphics.PublishSurfacePixels(2, Image(2, 2, (_, _) => [1, 2, 3, 255]));
        gfx.BindDraw(100, 2, -1, 0, 2, 2, 0, 0);
        gfx.BindDraw(101, 2, 2, 0, 2, 2, 0, 0);
        var destination = new RgbaImage(2, 2, new byte[16]);

        int rendered = graphics.DrawObjectRange(destination, gfx.SnapshotVisibleObjects(), 100, 2);

        Assert.Equal(0, rendered);
        Assert.All(destination.Pixels, value => Assert.Equal(0, value));
    }

    [Fact]
    public void PresentFrameIntoARenderTargetPublishesTheObjectsIntoItsSurface()
    {
        var graphics = Graphics.Headless();
        GfxState gfx = graphics.State;
        graphics.CreateSurface(2, 2, 2);
        graphics.PublishSurfacePixels(2, Image(2, 2, (_, _) => [9, 8, 7, 255]));
        gfx.BindDraw(10, 2, 0, 0, 2, 2, 1, 1);
        graphics.CreateSurface(5, 4, 4);
        gfx.SelectRenderTarget(5);

        Assert.False(graphics.PresentFrame());   // a render target draws at once; nothing to present

        Assert.Equal([0, 0, 0, 0], graphics.PixelAt(5, 0, 0));
        Assert.Equal([9, 8, 7, 255], graphics.PixelAt(5, 1, 1));
        Assert.Equal([9, 8, 7, 255], graphics.PixelAt(5, 2, 2));
        Assert.Equal([0, 0, 0, 0], graphics.PixelAt(5, 3, 3));
    }

    [Fact]
    public void TheLoweredFrameKeepsTheDynamicSurfaceKeyAndTheReferenceDrawsIt()
    {
        var clock = new FrameClock();
        var graphics = new Graphics(null, clock, new Sys4LogicalCanvas(4, 4));
        GfxState gfx = graphics.State;
        graphics.CreateSurface(2, 2, 2);
        graphics.PublishSurfacePixels(2, Image(2, 2, (_, _) => [9, 8, 7, 255]));
        gfx.BindDraw(10, 2, 0, 0, 2, 2, 1, 1);

        FrameDescription frame = graphics.LowerFrame(clock.NowMs);

        FrameItem item = Assert.Single(frame.Items);
        Assert.Equal(FrameItemKind.Texture, item.Kind);
        Assert.True(item.Dynamic);
        Assert.Equal(2, item.DynamicKey);
        var reference = new ReferenceRenderer(new Sys4LogicalCanvas(4, 4));
        reference.Draw(frame);
        Assert.Equal([9, 8, 7, 255], reference.Pixels.AsSpan((1 * 4 + 1) * 4, 4).ToArray());
        Assert.Equal([0, 0, 0, 0], reference.Pixels.AsSpan(0, 4).ToArray());
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
