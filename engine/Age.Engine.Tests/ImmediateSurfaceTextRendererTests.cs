using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Text;

public class ImmediateSurfaceTextRendererTests
{
    private sealed class RecordingRasterizer : IGlyphMaskRasterizer
    {
        public List<GlyphRasterRequest> Requests { get; } = new();

        public GlyphMask Rasterize(GlyphRasterRequest request)
        {
            Requests.Add(request);
            return new GlyphMask(
                width: 1, height: 1, stride: 1,
                originX: 0, originY: request.PixelHeight,
                cellAdvanceX: request.Cp932Code <= 0x7f ? 2 : 4,
                cellAdvanceY: 0,
                cellWidth: request.Cp932Code <= 0x7f ? 2 : 4,
                cellHeight: request.PixelHeight,
                coverage: [16]);
        }
    }

    [Fact]
    public void ImmediateStringBuildsExactCp932RequestsAndAdvancesFromGdiMetrics()
    {
        var rasterizer = new RecordingRasterizer();
        var renderer = new ImmediateSurfaceTextRenderer(rasterizer);
        var destination = Image(16, 8);
        AdvTextStyle style = Style() with
        {
            PrimaryFontSize = 5,
            Bold = true,
            FontFace = "ＭＳ ゴシック",
        };

        ImmediateSurfaceTextResult result =
            renderer.Render(destination, 1, 2, "Aあ", style);

        Assert.Equal(new ImmediateSurfaceTextResult(2, 7, 2), result);
        Assert.Collection(rasterizer.Requests,
            ascii => Assert.Equal(
                ("ＭＳ ゴシック", 4, -2, 700, 0x41, (ushort)0x41),
                RequestIdentity(ascii)),
            japanese => Assert.Equal(
                ("ＭＳ ゴシック", 4, -2, 700, 0x3042, (ushort)0x82a0),
                RequestIdentity(japanese)));
        Assert.Equal((255, 255, 255, 255), Pixel(destination, 1, 2));
        Assert.Equal((255, 255, 255, 255), Pixel(destination, 3, 2));
    }

    [Fact]
    public void UnsupportedCp932CharacterFailsBeforeRasterizationOrPixelMutation()
    {
        var rasterizer = new RecordingRasterizer();
        var renderer = new ImmediateSurfaceTextRenderer(rasterizer);
        var destination = Image(8, 8);

        Assert.Throws<ArgumentException>(
            () => renderer.Render(destination, 0, 0, "A😀", Style()));

        Assert.Empty(rasterizer.Requests);
        Assert.All(destination.Pixels, value => Assert.Equal(0, value));
    }

    [Fact]
    public void PortableRequestsPreserveUnicodeOutsideCp932()
    {
        IReadOnlyList<GlyphRasterRequest> requests =
            ImmediateSurfaceTextRenderer.CreateRequests(
                "A😀",
                Style(),
                GlyphRasterPolicy.PortableUnicode);

        Assert.Collection(
            requests,
            request =>
            {
                Assert.Equal(GlyphRasterPolicy.PortableUnicode, request.Policy);
                Assert.Equal((ushort)0x41, request.Cp932Code);
            },
            request =>
            {
                Assert.Equal(GlyphRasterPolicy.PortableUnicode, request.Policy);
                Assert.Equal(0x1f600, request.UnicodeScalar);
                Assert.Null(request.Cp932Code);
            });
    }

    [Theory]
    [InlineData(24, 24)]
    [InlineData(25, 24)]
    [InlineData(32, 31)]
    [InlineData(33, 31)]
    public void NativeFontHeightMatchesDecodedLogfontRebuildRules(int requested, int expected)
        => Assert.Equal(expected, ImmediateSurfaceTextRenderer.NativePixelHeight(requested));

    [Fact]
    public void RasterizedTextPixelsObeyRetainedOrderAlphaTintTransformAndSourceClip()
    {
        var renderer = new ImmediateSurfaceTextRenderer(new RecordingRasterizer());
        var textSurface = Image(8, 8);
        renderer.Render(textSurface, 1, 1, "AA", Style());

        var gfx = new GfxState();
        gfx.CreateSurface(2);
        gfx.BindDraw(10, 2, 1, 1, 1, 1, 0, 0);
        gfx.SetCurrentScale(10, (200, 200, 100));
        gfx.SetCurrentTranslation(10, (3, 2, 0));
        gfx.SetObjectColorResolved(10, 128, 0x00ff00);

        var occluder = new RgbaImage(1, 1, [9, 8, 7, 255]);
        gfx.SetSurface(3, 0x3000, -1);
        gfx.BindDraw(20, 3, 0, 0, 1, 1, 3, 2);

        var destination = Image(8, 8);
        IReadOnlyList<RenderObject> visible = gfx.SnapshotVisibleObjects();
        int rendered = RetainedSurfaceRasterizer.CompositeRange(
            destination, visible, 10, 1, _ => textSurface);

        Assert.Equal(1, rendered);
        Assert.Equal((0, 128, 0, 128), Pixel(destination, 3, 2));
        Assert.Equal((0, 128, 0, 128), Pixel(destination, 4, 2));
        Assert.Equal((0, 0, 0, 0), Pixel(destination, 5, 2));

        RetainedSurfaceRasterizer.CompositeRange(
            destination, visible, 20, 1, _ => occluder);
        Assert.Equal((9, 8, 7, 255), Pixel(destination, 3, 2));
    }

    [Fact]
    public void RasterizedTextPixelsInheritTheBoundObjectsAnimatedFade()
    {
        var renderer = new ImmediateSurfaceTextRenderer(new RecordingRasterizer());
        var textSurface = Image(8, 8);
        renderer.Render(textSurface, 1, 1, "A", Style());

        var gfx = new GfxState();
        gfx.CreateSurface(2);
        gfx.BindDraw(10, 2, 1, 1, 1, 1, 2, 3);
        gfx.SetObjectColorResolved(10, 255, 0xffffff);
        gfx.SetAnimatedObjectColorResolved(
            10, delayMs: 0, durationMs: 100, alpha: 0, rgb: 0xffffff);
        gfx.SnapshotVisibleObjects(nowMs: 1000); // establish the native one-shot start
        IReadOnlyList<RenderObject> midpoint = gfx.SnapshotVisibleObjects(nowMs: 1050);
        var destination = Image(8, 8);

        RetainedSurfaceRasterizer.CompositeRange(
            destination, midpoint, 10, 1, _ => textSurface);

        Assert.InRange(Pixel(destination, 2, 3).A, 126, 128);
        Assert.Equal(0, gfx.SnapshotVisibleObjects(nowMs: 1100).Single().Alpha);
    }

    [Fact]
    public void RasterizedTextSurvivesSurfaceCopyAndCanBeClearedAsPixels()
    {
        var renderer = new ImmediateSurfaceTextRenderer(new RecordingRasterizer());
        var source = Image(8, 8);
        renderer.Render(source, 1, 1, "A", Style());
        var captured = new RgbaImage(
            source.Width, source.Height, (byte[])source.Pixels.Clone());
        var destination = Image(8, 8);

        Assert.True(RgbaSurfaceOps.CopyRect(captured, destination, 1, 1, 1, 1, 4, 5));
        Assert.Equal((255, 255, 255, 255), Pixel(destination, 4, 5));

        Assert.True(RgbaSurfaceOps.FillRect(destination, 4, 5, 1, 1, 0, 0));
        Assert.Equal((0, 0, 0, 0), Pixel(destination, 4, 5));
    }

    private static AdvTextStyle Style()
        => new(
            PrimaryFontSize: 4,
            RubyFontSize: 0,
            Bold: false,
            TextColor: 0xffffff,
            EffectColor: 0,
            RenderMode: 0,
            EffectOffsetX: 0,
            EffectOffsetY: 0,
            LineSpacing: 0,
            FontFace: "");

    private static (
        string Face, int Height, int Width, int Weight, int Scalar, ushort Cp932)
        RequestIdentity(GlyphRasterRequest request)
        => (
            request.FontFace, request.PixelHeight, request.RequestedWidth, request.Weight,
            request.UnicodeScalar, request.Cp932Code!.Value);

    private static RgbaImage Image(int width, int height)
        => new(width, height, new byte[checked(width * height * 4)]);

    private static (byte R, byte G, byte B, byte A) Pixel(RgbaImage image, int x, int y)
    {
        int offset = checked((y * image.Width + x) * 4);
        return (
            image.Pixels[offset],
            image.Pixels[offset + 1],
            image.Pixels[offset + 2],
            image.Pixels[offset + 3]);
    }
}
