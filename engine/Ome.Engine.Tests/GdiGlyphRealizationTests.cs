using Ome.Engine.Text;

/// <summary>What a rasterizer without GDI makes of a glyph drawn at its face's own width
/// (<see cref="GdiGlyphRealization"/>), for faces whose tables are known.</summary>
public class GdiGlyphRealizationTests
{
    /// <summary>A 20-pixel inked square advancing 21 in a 21 by 23 cell, its baseline 19 down: a full-width glyph at
    /// 21 pixels as a text server draws it.</summary>
    private static GlyphMask Natural(int advance = 21)
        => new(20, 20, 20, 0, 18, advance, 0, advance, 23, Enumerable.Repeat((byte)16, 400).ToArray(), ascent: 19);

    private static GlyphRasterRequest Request(int height, int width, int weight = 400)
        => new("Test", height, width, weight, '激', 0x8c83, GlyphRasterPolicy.PortableUnicode);

    /// <summary>ＭＳ ゴシック's proportions: 256 units, averaging half of them, monospaced by PANOSE.</summary>
    private static OpenTypeFace Gothic(TestStrike? strike = null)
    {
        var font = new TestFont
        {
            UnitsPerEm = 256, AverageCharWidth = 128, WindowsAscent = 220, WindowsDescent = 36,
            Panose = [2, 11, 6, 9, 7, 2, 5, 8, 2, 4],
        };
        font.Map(('激', font.AddGlyph(TestGlyph.Box(0, 0, 250, 250))));
        if (strike != null) font.Strikes.Add(strike);
        return font.Read();
    }

    /// <summary>メイリオ's proportions: 2048 units averaging 1918, proportional.</summary>
    private static OpenTypeFace Meiryo()
    {
        var font = new TestFont { UnitsPerEm = 2048, AverageCharWidth = 1918, WindowsAscent = 2171, WindowsDescent = 901 };
        font.Map(('激', font.AddGlyph(TestGlyph.Box(0, 0, 2000, 2000))));
        return font.Read();
    }

    [Fact]
    public void AMonospacedFaceIsScaledToTheWidthAndAdvancesByWholeCells()
    {
        // At 21 pixels the face averages 10.5: -10 scales it by 10 / 10.5, and a full-width glyph advances two widths.
        GlyphMask drawn = GdiGlyphRealization.Draw(Natural(), Gothic(), Request(21, -10), simulatedBold: false);

        Assert.Equal((20, 20, 20, 21, 18), (drawn.Width, drawn.CellAdvanceX, drawn.CellWidth, drawn.CellHeight,
                                            drawn.Ascent));
    }

    [Fact]
    public void AProportionalFaceIsScaledAgainstItsOwnAverage()
    {
        // メイリオ at 22 pixels averages 20.6: -11 draws it at about half its width, as GDI does outside aspect mode.
        GlyphMask drawn = GdiGlyphRealization.Draw(Natural(), Meiryo(), Request(22, -11), simulatedBold: false);

        Assert.Equal((11, 11, 11, 33, 23), (drawn.Width, drawn.CellAdvanceX, drawn.CellWidth, drawn.CellHeight,
                                            drawn.Ascent));
        Assert.Equal(20, GdiGlyphRealization.Draw(Natural(), Meiryo(), Request(22, 0), false).Width);
    }

    [Fact]
    public void SimulatedBoldWidensTheGlyphAfterTheWidth()
    {
        GlyphMask regular = GdiGlyphRealization.Draw(Natural(), Gothic(), Request(21, -10, 700), simulatedBold: false);
        GlyphMask bold = GdiGlyphRealization.Draw(Natural(), Gothic(), Request(21, -10, 700), simulatedBold: true);

        Assert.Equal((regular.Width + 1, regular.CellAdvanceX + 1), (bold.Width, bold.CellAdvanceX));
    }

    [Fact]
    public void AFaceWhoseFileIsUnknownIsScaledAgainstHalfItsHeight()
    {
        GlyphMask natural = Natural();

        Assert.Same(natural, GdiGlyphRealization.Draw(natural, null, Request(22, -11), simulatedBold: false));
        GlyphMask narrowed = GdiGlyphRealization.Draw(natural, null, Request(22, -8), simulatedBold: false);
        Assert.Equal((15, 15, 23, 19), (narrowed.Width, narrowed.CellAdvanceX, narrowed.CellHeight, narrowed.Ascent));
    }

    [Fact]
    public void TheProbeReportsGdisMetricsAndAnUnscaledRequestsBitmapBox()
    {
        var strike = new TestStrike(22, 1, 1, 1, [new TestBitmap(1, 1, 18, 22, "..#####..", "..#...#..")]);
        OpenTypeFace face = Gothic(strike);
        GlyphRasterRequest request = Request(22, -11, 700);
        GlyphMask drawn = GdiGlyphRealization.Draw(Natural(), face, request, simulatedBold: true);

        FontProbeMetrics bitmap = GdiGlyphRealization.Measure(drawn, face, request, simulatedBold: true, glyph: 1);
        FontProbeMetrics natural = GdiGlyphRealization.Measure(drawn, face, Request(22, 0, 700), true, glyph: 1);
        FontProbeMetrics scaled = GdiGlyphRealization.Measure(drawn, face, Request(22, -10, 700), true, glyph: 1);

        // The strike's ink is 5 wide, and simulated bold widens it by one; tmAveCharWidth gains one too.
        Assert.Equal(new FontProbeMetrics(12, 6, (short)drawn.CellAdvanceX, 22), bitmap);
        Assert.Equal(6u, natural.BlackBoxX);
        Assert.Equal(12, natural.AverageCharWidth);
        // A scaled request is measured on its outline: the drawn glyph's ink.
        Assert.Equal(new FontProbeMetrics(11, 21, (short)drawn.CellAdvanceX, 22), scaled);
    }

    [Fact]
    public void WithoutTheFileTheProbeReportsHalfTheHeightAndTheDrawnGlyph()
    {
        GlyphMask natural = Natural();

        Assert.Equal(new FontProbeMetrics(11, 20, 21, 23),
                     GdiGlyphRealization.Measure(natural, null, Request(22, 0), simulatedBold: false, glyph: 0));
        Assert.Equal(new FontProbeMetrics(8, 20, 21, 23),
                     GdiGlyphRealization.Measure(natural, null, Request(22, -8), simulatedBold: false, glyph: 0));
    }
}
