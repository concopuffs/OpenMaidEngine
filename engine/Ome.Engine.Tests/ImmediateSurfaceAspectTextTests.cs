using Ome.Engine.Model;
using Ome.Engine.Text;

/// <summary>SYS4433's aspect-mode surface text (docs/engine-re.md, "Aspect-mode surface text"): the baseline, the plain
/// and effect draws' advances, and blanks, from synthesized glyph metrics.</summary>
public class ImmediateSurfaceAspectTextTests
{
    private const string Face = "メイリオ";

    /// <summary>One-pixel glyphs at the pen and baseline. The reference ＭＳ ゴシック 激 has a 15-pixel cell and ascent 12;
    /// メイリオ has ascent 15, a 6-pixel cell for one byte and 13 for two. A blank in the reference face has a 9-pixel
    /// cell but a 5-pixel extent (two-byte: 18 and 10), so each rule shows which one it read.</summary>
    private sealed class MetricRasterizer : IGlyphMaskRasterizer
    {
        public List<GlyphRasterRequest> Requests { get; } = new();

        public GlyphMask Rasterize(GlyphRasterRequest request)
        {
            Requests.Add(request);
            bool reference = request.FontFace == AspectFontRealization.ReferenceFace;
            bool singleByte = request.Cp932Code <= 0xff;
            (int advance, int extent) = (reference, request.Cp932Code) switch
            {
                (true, 0x20) => (9, 5),
                (true, 0x8140) => (18, 10),
                (true, _) => (15, 15),
                _ => singleByte ? (6, 6) : (13, 13),
            };
            return new GlyphMask(
                width: 1, height: 1, stride: 1, originX: 0, originY: 0,
                cellAdvanceX: advance, cellAdvanceY: 0, cellWidth: extent, cellHeight: request.PixelHeight,
                coverage: [16], ascent: reference ? 12 : 15);
        }
    }

    private static AdvTextStyle Style(int aspectMode = 1, int renderMode = 0) => AdvTextStyle.Default with
    {
        PrimaryFontSize = 16, FontFace = Face, AspectMode = aspectMode, RenderMode = renderMode,
        TextColor = 0xffffff, EffectColor = 0, EffectOffsetX = 1, EffectOffsetY = 1,
    };

    [Fact]
    public void ModeOneDrawsEveryGlyphOnTheReferenceBaselineAndThePlainDrawAdvancesByEachCell()
    {
        var renderer = new ImmediateSurfaceTextRenderer(new MetricRasterizer());
        var destination = Image(48, 24);

        ImmediateSurfaceTextResult result = renderer.Render(destination, 2, 3, "AあA", Style());

        // Baseline y + 12 (the reference ascent, not メイリオ's 15); pens 2, 8 (+6) and 21 (+13).
        Assert.Equal(new[] { 2, 8, 21 }, TextPixels(destination, 3 + 12));
        Assert.Equal(new ImmediateSurfaceTextResult(3, 27, 3), result);
    }

    [Fact]
    public void ModeTwoKeepsThePrimaryFontsBaseline()
    {
        var renderer = new ImmediateSurfaceTextRenderer(new MetricRasterizer());
        var destination = Image(48, 24);

        renderer.Render(destination, 2, 3, "A", Style(aspectMode: 2));

        Assert.Equal(new[] { 2 }, TextPixels(destination, 3 + 15));
    }

    [Fact]
    public void TheEffectDrawAdvancesByTheReferenceCellRoundedHalfUp()
    {
        var renderer = new ImmediateSurfaceTextRenderer(new MetricRasterizer());
        var destination = Image(48, 24);

        ImmediateSurfaceTextResult result = renderer.Render(destination, 2, 3, "AあA", Style(renderMode: 1));

        // trunc(15 / 2.0 + 0.5) = 8 for one byte, 15 for two, whatever each glyph's own cell.
        Assert.Equal(new[] { 2, 10, 25 }, TextPixels(destination, 3 + 12));
        Assert.Equal(new ImmediateSurfaceTextResult(3, 33, 3), result);
    }

    [Theory]
    // The plain draw: the reference cell, halved for one byte.
    [InlineData(1, 0, false, 7, 15)]
    // The effect draw: the font size, halved for one byte.
    [InlineData(1, 1, false, 8, 16)]
    // BLANKEXTENTMODE: the blank's own extent in the reference font, in the effect draw only in mode 1.
    [InlineData(1, 0, true, 5, 10)]
    [InlineData(2, 0, true, 5, 10)]
    [InlineData(1, 3, true, 5, 10)]
    [InlineData(2, 1, true, 8, 16)]
    public void BlanksDrawNothingAndAdvanceByTheirDrawsRule(
        int aspectMode, int renderMode, bool blankExtentMode, int space, int ideographicSpace)
    {
        var renderer = new ImmediateSurfaceTextRenderer(new MetricRasterizer());
        var destination = Image(64, 24);

        ImmediateSurfaceTextResult spaced = renderer.Render(destination, 0, 0, " ", Style(aspectMode, renderMode),
                                                            blankExtentMode: blankExtentMode);
        ImmediateSurfaceTextResult wide = renderer.Render(destination, 0, 0, "　", Style(aspectMode, renderMode),
                                                          blankExtentMode: blankExtentMode);

        Assert.Equal((space, ideographicSpace), (spaced.CursorX, wide.CursorX));
        Assert.All(destination.Pixels, value => Assert.Equal(0, value));
    }

    [Fact]
    public void AControlByteIsABlankAtTheFallbackAdvance()
    {
        var renderer = new ImmediateSurfaceTextRenderer(new MetricRasterizer());
        var destination = Image(48, 24);

        ImmediateSurfaceTextResult result =
            renderer.Render(destination, 0, 0, "\tA", Style(), blankExtentMode: true);

        Assert.Equal(new[] { 7 }, TextPixels(destination, 12));
        Assert.Equal(13, result.CursorX);
    }

    /// <summary>The x of every pixel in the text colour on row <paramref name="y"/>.</summary>
    private static int[] TextPixels(RgbaImage image, int y)
        => Enumerable.Range(0, image.Width)
            .Where(x => image.Pixels[(y * image.Width + x) * 4] == 255 && image.Pixels[(y * image.Width + x) * 4 + 3] != 0)
            .ToArray();

    private static RgbaImage Image(int width, int height)
        => new(width, height, new byte[checked(width * height * 4)]);
}
