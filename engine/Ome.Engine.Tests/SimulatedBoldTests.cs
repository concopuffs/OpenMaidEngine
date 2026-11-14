using Ome.Engine.Text;

public class SimulatedBoldTests
{
    [Fact]
    public void AGlyphGainsACopyOfItselfOneColumnRightAndOnePixelOfAdvance()
    {
        var mask = new GlyphMask(3, 2, 3, originX: 1, originY: 2, cellAdvanceX: 11, cellAdvanceY: 0, cellWidth: 11,
                                 cellHeight: 22, [16, 8, 0, 0, 16, 16], ascent: 19);

        GlyphMask bold = SimulatedBold.Apply(mask);

        Assert.Equal((4, 2, 1, 2), (bold.Width, bold.Height, bold.OriginX, bold.OriginY));
        Assert.Equal((12, 12, 22, 19), (bold.CellAdvanceX, bold.CellWidth, bold.CellHeight, bold.Ascent));
        // Coverage adds and saturates at full.
        Assert.Equal([16, 16, 8, 0, 0, 16, 16, 16], bold.Coverage.ToArray());
    }

    [Fact]
    public void ABlankGlyphStaysBlankAndStillGainsAPixelOfAdvance()
    {
        var space = new GlyphMask(0, 0, 0, 0, 0, 11, 0, 11, 22, []);
        var blank = new GlyphMask(1, 1, 1, 0, 0, 11, 0, 11, 22, [0]);

        Assert.Equal((0, 12, 12), (SimulatedBold.Apply(space).Width, SimulatedBold.Apply(space).CellAdvanceX,
                                   SimulatedBold.Apply(space).CellWidth));
        Assert.Equal((1, 12), (SimulatedBold.Apply(blank).Width, SimulatedBold.Apply(blank).CellAdvanceX));
    }

    [Fact]
    public void AMonochromeGlyphStaysMonochrome()
    {
        var mask = new GlyphMask(2, 1, 2, 0, 1, 11, 0, 11, 22, [16, 16], monochrome: true);

        GlyphMask bold = SimulatedBold.Apply(mask);

        Assert.True(bold.Monochrome);
        Assert.Equal([16, 16, 16], bold.Coverage.ToArray());
    }
}
