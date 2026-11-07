using Age.Engine.Model;
using Age.Engine.Text;

/// <summary>SYS4433's aspect-mode glyph placement and raise (docs/engine-re.md, "Aspect-mode glyph placement"), from
/// synthesized glyph masks.</summary>
public class AspectGlyphPlacementTests
{
    private const byte Proportional = 34;   // VARIABLE_PITCH | FF_SWISS
    private const byte Fixed = 49;          // FIXED_PITCH | FF_MODERN

    private sealed class NoProbe : IFontMetricsProbe
    {
        public FontProbeMetrics Measure(string faceName, int pixelHeight, int width, int weight, ushort code)
            => new(11, 19, 23);
    }

    /// <summary>The reference ＭＳ ゴシック 激 rasterizes 21 pixels wide, as antialiased bold at 22 does, though its
    /// GGO_METRICS black box (<see cref="NoProbe"/>) is 19, and advances 23; every other glyph is a 4-pixel ink row whose
    /// origin sits 2 pixels right of the pen. Both share an ascent, so nothing is raised.</summary>
    private sealed class FakeRasterizer : IGlyphMaskRasterizer
    {
        public GlyphMask Rasterize(GlyphRasterRequest request)
            => request.FontFace == AspectFontRealization.ReferenceFace
                ? Glyph(width: 21, originX: 1, advance: 23)
                : Glyph(width: 4, originX: 2, advance: 9);
    }

    private static GlyphMask Glyph(int width, int originX, int advance, int ascent = 19, int originY = 1)
        => new(width, 1, (width + 3) & ~3, originX, originY, advance, 0, advance, 22,
               Enumerable.Repeat((byte)16, (width + 3) & ~3).ToArray(), ascent);

    private static AdvTextStyle Style(string face) => AdvTextStyle.Default with
    {
        PrimaryFontSize = 22, Bold = true, FontFace = face, AspectMode = 1,
    };

    private static int[] InkColumns(RgbaImage image, int row)
        => Enumerable.Range(0, image.Width).Where(x => image.Pixels[(row * image.Width + x) * 4 + 3] != 0).ToArray();

    [Theory]
    [InlineData(0x8176, 0)]    // 」
    [InlineData(0x8141, 0)]    // 、
    [InlineData(0x8144, 0)]    // ．
    [InlineData(0x8175, 1)]    // 「
    [InlineData(0x816f, 1)]    // ｛
    [InlineData(0x82a0, -1)]   // あ
    [InlineData(0x0041, -1)]   // A: single-byte glyphs are never classified
    public void BracketsAndPunctuationHaveTheirOwnClass(int code, int expected)
        => Assert.Equal(expected, AspectGlyphPlacement.Classify((ushort)code));

    [Theory]
    // trunc(22 / 2 + 0.5) - trunc(4 * 0.5 + 0.5) - 2 = 7: the black box is centred on half the font size.
    [InlineData(0x82a0, 1, 7)]
    // 19 - 2 - 4 = 13: an opening bracket's black box ends at the reference 激's black-box width.
    [InlineData(0x8175, 1, 13)]
    // A closing bracket keeps the pen.
    [InlineData(0x8176, 1, 0)]
    // A single-byte glyph centres on a quarter of the size: trunc(5.5 + 0.5) - 2 - 2 = 2.
    [InlineData(0x0041, 1, 2)]
    // Any other mode centres every glyph.
    [InlineData(0x8175, 2, 7)]
    public void TheOffsetFollowsTheGlyphsClass(int code, int aspectMode, int expected)
        => Assert.Equal(expected, AspectGlyphPlacement.OffsetX(
            Glyph(width: 4, originX: 2, advance: 9), 19, 22, (ushort)code, aspectMode));

    [Theory]
    [InlineData("メイリオ", true, new[] { 9, 38, 48 })]        // centred, right-aligned to the probe's 19, at the pen
    [InlineData("ＭＳ 明朝", true, new[] { 2, 25, 48 })]       // a fixed-pitch face draws at the pen
    [InlineData("メイリオ", false, new[] { 2, 25, 48 })]       // without the installed-font list
    public void TheLayoutPlacesAProportionalFacesGlyphsInTheirCells(string face, bool withFontList, int[] inkStarts)
    {
        var engine = new RetainedGlyphLayoutEngine(new FakeRasterizer());
        var realization = new AspectFontRealization(
            [new("ＭＳ ゴシック", Fixed), new("ＭＳ 明朝", Fixed), new("メイリオ", Proportional)], new NoProbe());
        var image = new RgbaImage(80, 30, new byte[80 * 30 * 4]);
        IReadOnlyList<GlyphRasterRequest> glyphs = ImmediateSurfaceTextRenderer.CreateRequests("あ「」", Style(face));

        GlyphTextLayoutResult result = engine.Render(image,
            new GlyphTextLayoutOptions(0, 0, 0, 80, 30, true, Style(face),
                FontRealization: withFontList ? realization : null),
            glyphs);

        // Cells advance by the reference 激 whatever the ink; the baseline is the cell top plus the ascent (19), and
        // each ink row sits one pixel above it.
        Assert.Equal(new[] { 0, 23, 46 }, result.Records.Select(record => record.Left));
        Assert.Equal(inkStarts.SelectMany(start => Enumerable.Range(start, 4)), InkColumns(image, 18));
    }

    [Theory]
    // d = trunc(23 - 19 - 0.5) = 3 and e = trunc(23 - 18 - 1) = 4: the larger wins.
    [InlineData(23, 18, 4)]
    // e = trunc(23 - 21 - 1) = 1 is smaller than d.
    [InlineData(23, 21, 3)]
    // A face as tall as the reference (d = 0) is not raised.
    [InlineData(19, 10, 0)]
    public void AspectModeRaisesATallerFaceAboveItsCell(int ascent, int realizedOriginY, int expected)
    {
        GlyphMask glyph = Glyph(4, 2, 9, ascent: ascent);
        GlyphMask reference = Glyph(19, 1, 23, ascent: 19);
        GlyphMask realized = Glyph(19, 1, 22, ascent: ascent, originY: realizedOriginY);

        Assert.Equal(expected, RetainedGlyphLayoutEngine.AspectGlyphRaise(glyph, reference, realized, 22));
    }
}
