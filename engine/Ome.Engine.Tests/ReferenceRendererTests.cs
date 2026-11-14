using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Xunit;

/// <summary>The reference renderer's own rules (engine layering spec §6.3) that the lowering tests do not reach.</summary>
public class ReferenceRendererTests
{
    private static FrameDescription OneStaticDraw(RgbaImage pixels)
    {
        var frame = new FrameDescription { Width = 1, Height = 1 };
        frame.Items.Add(new FrameItem(
            FrameItemKind.Texture, pixels, AssetId: 7, Dynamic: false, DynamicKey: 0, ColorKey: -1, SourceX: 0,
            SourceY: 0, Width: 1, Height: 1, Matrix: new Affine2D(1, 0, 0, 1, 0, 0), Opacity: 1, Tint: 0,
            TintStrength: 0, MultiplyTint: false, Blend: BlendKind.Opaque));
        return frame;
    }

    [Fact]
    public void AStaticAssetDecodedAgainIsPreparedAgain()
    {
        // A language switch: the graphics native forgets its decoded images, so the asset id's next decode is another
        // language's file. A frame lowered before the switch can be drawn after the renderer forgot its pixels; the
        // next frame's newer image must replace what that frame left.
        var renderer = new ReferenceRenderer(new Sys4LogicalCanvas(1, 1));
        renderer.Draw(OneStaticDraw(new RgbaImage(1, 1, [10, 20, 30, 255])));
        Assert.Equal([10, 20, 30, 255], renderer.Pixels[..4]);

        renderer.Draw(OneStaticDraw(new RgbaImage(1, 1, [40, 50, 60, 255])));

        Assert.Equal([40, 50, 60, 255], renderer.Pixels[..4]);
    }
}
