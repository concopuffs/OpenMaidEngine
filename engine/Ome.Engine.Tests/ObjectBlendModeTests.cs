using System.Linq;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Xunit;

// Colour modes 2 and 3 of an object (op 0x203's mode), as the builds' sprite draws set the blend (docs/engine-re.md,
// "Object blend modes").
public class ObjectBlendModeTests
{
    [Fact]
    public void AMode3ObjectIsTakenFromWhatIsUnderItScaledByItsAlpha()
    {
        var g = new GfxState();
        g.SetSurface(1, 5, -1);
        g.BindDraw(0x100, 1, 0, 0, 1, 1, 0, 0);
        g.SetStaticObjectColorResolved(0x100, 3, 0x80, 0xffffff);
        RenderObject rendered = g.SnapshotVisibleObjects().Single();
        Assert.Equal((BlendKind.Subtractive, 0x80, true), (rendered.Blend, rendered.Alpha, rendered.MultiplyTint));

        // 200 less 100 x 128 / 255 is 150; the target's alpha stays.
        byte[] target = [200, 200, 200, 255];
        SoftwareAffineRasterizer.BlitRgba(target, 1, 1, [100, 0, 255, 255], 1, 1, 0, 0, 1, 1,
                                          new Affine2D(1, 0, 0, 1, 0, 0), 0xffffff, 0, 0x80 / 255f,
                                          multiplyTint: true, BlendKind.Subtractive);
        Assert.Equal([150, 200, 72, 255], target);
    }

    [Fact]
    public void AMode2ObjectIsCopiedIntoAKind1RenderTargetAndBlendedIntoAKind2One()
    {
        // The source's first texel is half transparent and its second wholly so; both targets start grey.
        byte[] source = [10, 20, 30, 128, 90, 90, 90, 0];
        (byte[] Kind1, byte[] Kind2) drawn = Draw(spriteAlphaTest: true, source);

        // Into kind 1 the first texel replaces the grey, alpha included, and the alpha test keeps the second out.
        Assert.Equal([10, 20, 30, 128, 200, 200, 200, 255], drawn.Kind1);
        // Into kind 2 the object blends as it does on the screen: (10 x 128 + 200 x 127) / 255 is 104.
        Assert.Equal([104, 109, 114, 255, 200, 200, 200, 255], drawn.Kind2);
    }

    [Fact]
    public void ABuildWithoutTheAlphaTestCopiesTheTransparentTexelsToo()
    {
        byte[] source = [10, 20, 30, 128, 90, 90, 90, 0];
        Assert.Equal([10, 20, 30, 128, 90, 90, 90, 0], Draw(spriteAlphaTest: false, source).Kind1);
    }

    [Fact]
    public void TheBuildsDeclareTheSpritesAlphaTest()
    {
        Assert.True(EngineBuilds.Age424J.Rules.Gfx.SpriteAlphaTest);
        Assert.True(EngineBuilds.Age433G.Rules.Gfx.SpriteAlphaTest);
        Assert.False(EngineBuilds.Age438D.Rules.Gfx.SpriteAlphaTest);
    }

    // Object 20 shows surface 2's two texels in colour mode 2 at full colour; op 0x20c presents it into surface 5,
    // made with kind 1, and into surface 6, made with kind 2.
    private static (byte[] Kind1, byte[] Kind2) Draw(bool spriteAlphaTest, byte[] source)
    {
        var graphics = new Graphics(null, new FrameClock(), new Sys4LogicalCanvas(2, 1))
        {
            SpriteAlphaTest = spriteAlphaTest,
        };
        GfxState gfx = graphics.State;
        graphics.CreateSurface(2, 2, 1);
        graphics.PublishSurfacePixels(2, new RgbaImage(2, 1, source));
        gfx.BindDraw(20, 2, 0, 0, 2, 1, 0, 0);
        gfx.SetStaticObjectColorResolved(20, 2, 0xff, 0xffffff);

        byte[] Present(int slot, long kind)
        {
            graphics.CreateSurface(slot, 2, 1, kind);
            graphics.PublishSurfacePixels(slot, new RgbaImage(2, 1, [200, 200, 200, 255, 200, 200, 200, 255]));
            gfx.SelectRenderTarget(slot);
            graphics.PresentFrame();
            gfx.SelectRenderTarget(-1);
            return graphics.CaptureSurfacePixels(slot)!.Pixels;
        }

        return (Present(5, 1), Present(6, 2));
    }
}
