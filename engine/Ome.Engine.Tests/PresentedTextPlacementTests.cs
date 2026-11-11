using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Text;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Alignment, fit and overflow of presented text (localization spec §7.2, §7.3), with glyphs 10 pixels wide
/// (a space 5) whose masks are full ink.</summary>
public class PresentedTextPlacementTests
{
    /// <summary>Every glyph a 10 × 4 mask of full coverage, advancing 10; a space advances 5. Counts what it draws.</summary>
    private sealed class FixedRasterizer : IGlyphMaskRasterizer
    {
        public readonly List<GlyphRasterRequest> Requests = [];

        public GlyphMask Rasterize(GlyphRasterRequest request)
        {
            Requests.Add(request);
            bool space = request.UnicodeScalar == ' ';
            int width = space ? 0 : 10;
            return new GlyphMask(width, 4, width, originX: 0, originY: 4, cellAdvanceX: space ? 5 : 10, cellAdvanceY: 0,
                                 cellWidth: space ? 5 : 10, cellHeight: 12, Enumerable.Repeat((byte)16, width * 4).ToArray(),
                                 ascent: 10);
        }
    }

    private static readonly AdvTextStyle Style = AdvTextStyle.Default with { PrimaryFontSize = 12 };

    private static IReadOnlyList<GlyphRasterRequest> Requests(string text)
        => ImmediateSurfaceTextRenderer.CreateRequests(text, Style, GlyphRasterPolicy.PortableUnicode);

    private static CachedGlyphMaskRasterizer Cache(FixedRasterizer? inner = null) => new(inner ?? new FixedRasterizer(), 256);

    // ---- narrowing -------------------------------------------------------------------------------------------------

    [Fact]
    public void ANarrowedMaskAveragesItsColumnsAndScalesItsAdvance()
    {
        // Columns of 16 and 0 alternating: halved, every column averages to 8.
        byte[] stripes = Enumerable.Range(0, 10 * 2).Select(i => (byte)(i % 2 == 0 ? 16 : 0)).ToArray();
        var mask = new GlyphMask(10, 2, 10, originX: 2, originY: 3, cellAdvanceX: 12, cellAdvanceY: 0, cellWidth: 12,
                                 cellHeight: 14, stripes, ascent: 11);

        GlyphMask half = GlyphMaskNarrowing.Narrow(mask, 50);

        Assert.Equal((5, 2, 5, 1, 3, 6, 6, 14, 11), (half.Width, half.Height, half.Stride, half.OriginX, half.OriginY,
            half.CellAdvanceX, half.CellWidth, half.CellHeight, half.Ascent!.Value));
        Assert.All(half.Coverage.ToArray(), value => Assert.Equal(8, value));
        Assert.Same(mask, GlyphMaskNarrowing.Narrow(mask, 100));
    }

    [Fact]
    public void AMonochromeMaskStaysMonochrome()
    {
        byte[] stripes = Enumerable.Range(0, 4).Select(i => (byte)(i == 0 ? 16 : 0)).ToArray();
        var mask = new GlyphMask(4, 1, 4, 0, 1, 4, 0, 4, 4, stripes, monochrome: true);

        GlyphMask narrowed = GlyphMaskNarrowing.Narrow(mask, 60);

        Assert.True(narrowed.Monochrome);
        Assert.All(narrowed.Coverage.ToArray(), value => Assert.True(value is 0 or 16));
    }

    [Fact]
    public void TheCacheNarrowsTheFontWidthGlyphSoNoBackendNarrows()
    {
        var inner = new FixedRasterizer();
        CachedGlyphMaskRasterizer cache = Cache(inner);
        GlyphRasterRequest glyph = Requests("A")[0];

        GlyphMask narrowed = cache.Rasterize(glyph with { WidthPercent = 80 });
        cache.Rasterize(glyph);
        cache.Rasterize(glyph with { WidthPercent = 80 });

        Assert.Equal((8, 8), (narrowed.Width, narrowed.CellAdvanceX));
        Assert.Equal(100, Assert.Single(inner.Requests).WidthPercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => glyph with { WidthPercent = 0 });
        Assert.NotEqual(glyph, glyph with { WidthPercent = 80 });
    }

    // ---- extents, fit, alignment ---------------------------------------------------------------------------------

    [Fact]
    public void AnExtentRunsFromTheFirstToTheLastGlyphThatIsNotASpace()
    {
        AdvRetainedGlyphRecord Cell(int left) => new(0, left, 0, left + 10, 12);
        Assert.Equal(new TextExtent(10, 30), PresentedTextPlacement.Extent("　ab　", [Cell(0), Cell(10), Cell(20), Cell(30)]));
        Assert.Null(PresentedTextPlacement.Extent("　 ", [Cell(0), Cell(10)]));

        Assert.Equal(new TextExtent(105, 125), PresentedTextPlacement.Extent(Requests(" ab "), Cache(), 100));
    }

    [Fact]
    public void FitNarrowsTheWidestLineToTheSourceWidthDownToTheFloor()
    {
        CachedGlyphMaskRasterizer cache = Cache();
        IReadOnlyList<GlyphRasterRequest>[] lines = [Requests("abcdefghij"), Requests("abc")];   // 100 and 30 wide

        Assert.Equal(100, PresentedTextPlacement.FitPercent(lines, cache, 100, 0.5));
        // 75 % rounds each glyph's advance to 8 (half to even), 80 wide; 74 % to 7, 71 wide.
        Assert.Equal(74, PresentedTextPlacement.FitPercent(lines, cache, 75, 0.5));
        Assert.Equal(60, PresentedTextPlacement.FitPercent(lines, cache, 40, 0.6));
        // 72 % rounds each 10-pixel glyph to 7: 9 advances of 7 and a cell of 8 make 71.
        Assert.Equal(72, PresentedTextPlacement.FitPercent(lines, cache, 72, 0.5));
    }

    [Theory]
    [InlineData(TextAlign.Center, 100, 200, 20, 40, 120)]    // centre 150: the 20-pixel row runs 140 to 160
    [InlineData(TextAlign.End, 100, 200, 20, 40, 160)]
    [InlineData(TextAlign.Start, 100, 200, 20, 40, 0)]
    [InlineData(TextAlign.Center, 100, 140, 100, 300, -80)]  // wider than the source, so it moves left
    [InlineData(TextAlign.Center, 0, 40, 0, 200, 0)]         // but not past the left bound
    public void AlignmentMovesARowAgainstTheSourceExtentInsideItsBounds(TextAlign align, int sourceLeft, int sourceRight,
                                                                        int rowLeft, int rowRight, int expected)
    {
        int shift = PresentedTextPlacement.Shift(align, new TextExtent(sourceLeft, sourceRight),
                                                 new TextExtent(rowLeft, rowRight), minLeft: 0, maxRight: 640);
        Assert.Equal(expected, shift);
    }

    [Fact]
    public void ARowEndingPastTheRightBoundMovesBack()
        => Assert.Equal(-20, PresentedTextPlacement.Shift(TextAlign.End, new TextExtent(0, 120), new TextExtent(60, 120),
                                                          minLeft: 0, maxRight: 100));

    private static PresentedRunPlacement Place(string text, TextAlign align, TextFit fit, TextExtent? source,
                                               int cursorX = 0, int right = 200, double minFit = 0.5, int sourceRows = 1,
                                               Func<PresentedRunPlacement, bool>? fitsWindow = null)
        => PresentedTextPlacement.Place(text, PresentedLineBreaks.Opportunities(text, LineBreakMode.Words), Requests(text),
                                        Cache(), cursorX, lineOriginX: 0, right, align, fit, source, sourceRows, minFit,
                                        fitsWindow);

    [Fact]
    public void ACentredRunMovesEachRowUnderTheSourceCentre()
    {
        // Two rows of 30 and 50 pixels under a source that ran from 50 to 150.
        PresentedRunPlacement placed = Place("abc\nabcde", TextAlign.Center, TextFit.None, new TextExtent(50, 150));

        Assert.Equal([85, 75], placed.RowShiftsX);
        Assert.Equal(100, placed.WidthPercent);
        Assert.Null(Place("abc", TextAlign.Start, TextFit.None, new TextExtent(50, 150)).RowShiftsX);
        Assert.Null(Place("abc", TextAlign.Center, TextFit.None, source: null).RowShiftsX);
    }

    [Fact]
    public void AFittedRunIsNarrowedBeforeItsRowsAreBroken()
    {
        // 150 pixels of text against a 100-pixel source: 66 % still makes 105, 65 % makes 91, one row within 120.
        PresentedRunPlacement placed = Place("abcdefghijklmno", TextAlign.Start, TextFit.Shrink, new TextExtent(0, 100),
                                             right: 120);

        Assert.Equal(65, placed.WidthPercent);
        Assert.All(placed.Plan.Glyphs, glyph => Assert.Equal(65, glyph.WidthPercent));
        Assert.Empty(placed.Plan.BreaksBefore);
        // At the floor the text still wraps.
        PresentedRunPlacement floored = Place("abcdefghijklmno", TextAlign.Start, TextFit.Shrink, new TextExtent(0, 20),
                                              right: 100, minFit: 0.8);
        Assert.Equal(80, floored.WidthPercent);
        Assert.NotEmpty(floored.Plan.BreaksBefore);
    }

    [Fact]
    public void ASourceOfSeveralRowsFitsByRowsNotByWidth()
    {
        // Three words of 50 pixels in a 120-pixel layout: three rows. A source of two rows narrows them until two words
        // share a row (110 pixels at 100 %; 51 + 5 + 51 at 91 %: "abcde fghij" within 120 needs 107).
        const string text = "abcde fghij klmno";
        Assert.Equal(3, PresentedTextPlacement.RowCount(Place(text, TextAlign.Start, TextFit.None, null, right: 100).Plan));

        PresentedRunPlacement twoRows = Place(text, TextAlign.Start, TextFit.Shrink, new TextExtent(0, 100), right: 100,
                                              sourceRows: 2);
        Assert.Equal(2, PresentedTextPlacement.RowCount(twoRows.Plan));
        Assert.True(twoRows.WidthPercent is < 100 and >= 90, twoRows.WidthPercent.ToString());
        // Already within the source's rows: as written.
        Assert.Equal(100, Place(text, TextAlign.Start, TextFit.Shrink, new TextExtent(0, 100), right: 100,
                                sourceRows: 3).WidthPercent);
        // A blank row counts; a break after the last glyph does not.
        Assert.Equal(3, PresentedTextPlacement.RowCount(Place("ab\n\ncd\n", TextAlign.Start, TextFit.None, null).Plan));
    }

    [Fact]
    public void ACutCountsTheRowsWithGlyphsFromTheOneItStoppedIn()
    {
        // Rows: "ab", "cd", "", "ef" (a blank row between).
        PresentedTextPlan plan = Place("ab\ncd\n\nef", TextAlign.Start, TextFit.None, null).Plan;

        Assert.Equal(0, PresentedTextPlacement.CutRows(plan, plan.Glyphs.Count));
        Assert.Equal(2, PresentedTextPlacement.CutRows(plan, 2));
        Assert.Equal(2, PresentedTextPlacement.CutRows(plan, 3));
        Assert.Equal(1, PresentedTextPlacement.CutRows(plan, 4));
    }

    // ---- overflow ----------------------------------------------------------------------------------------------------

    /// <summary>Whether a placement fits a window <paramref name="rows"/> rows high, laid out as the host draws it
    /// (without drawing); each width tried goes to <paramref name="tried"/>.</summary>
    private static Func<PresentedRunPlacement, bool> FitsRows(int rows, List<int>? tried = null)
    {
        var engine = new RetainedGlyphLayoutEngine(Cache());
        int bottom = (rows - 1) * (12 + Style.LineSpacing) + 12;
        return placement =>
        {
            tried?.Add(placement.WidthPercent);
            return !engine.Render(null,
                new GlyphTextLayoutOptions(0, 0, 0, int.MaxValue, bottom, WrapHorizontally: false, Style,
                                           placement.Plan.BreaksBefore, OverflowAtFontHeight: true,
                                           RowShiftsX: placement.RowShiftsX),
                placement.Plan.Glyphs).StoppedOnVerticalOverflow;
        };
    }

    [Fact]
    public void ARunThatWouldBeCutIsNarrowedToTheWidestWidthThatFitsItsWindow()
    {
        // Three words of 50 pixels in a 100-pixel layout take three rows. Two words share a row at 94 % (each glyph
        // advances 9: "abcde fghij" ends with a 10-pixel cell at 96), not at 95 % (9.5 rounds half to even, to 10).
        const string text = "abcde fghij klmno";
        var tried = new List<int>();
        PresentedRunPlacement placed = Place(text, TextAlign.Start, TextFit.None, null, right: 100,
                                             fitsWindow: FitsRows(2, tried));

        Assert.Equal((94, true, 2), (placed.WidthPercent, placed.NarrowedToFit, PresentedTextPlacement.RowCount(placed.Plan)));
        Assert.All(placed.Plan.Glyphs, glyph => Assert.Equal(94, glyph.WidthPercent));
        // As written, the floor, then halving.
        Assert.Equal([100, 50, 75, 87, 93, 96, 94, 95], tried);
        // An aligned run's shifts are those of its narrowed rows: 0..96 and 0..46 centred under 0..100.
        Assert.Equal([2, 27], Place(text, TextAlign.Center, TextFit.None, new TextExtent(0, 100), right: 100,
                                    sourceRows: 2, fitsWindow: FitsRows(2)).RowShiftsX);
    }

    [Fact]
    public void ARunThatFitsItsWindowIsCheckedOnceAndKeepsItsWidth()
    {
        var tried = new List<int>();
        PresentedRunPlacement placed = Place("abcde fghij klmno", TextAlign.Start, TextFit.None, null, right: 100,
                                             fitsWindow: FitsRows(3, tried));

        Assert.Equal((100, false), (placed.WidthPercent, placed.NarrowedToFit));
        Assert.Equal([100], tried);
    }

    [Fact]
    public void ARunThatDoesNotFitEvenAtTheFloorIsLeftToBeCut()
    {
        // Its own breaks make three rows, which no width takes away.
        var tried = new List<int>();
        PresentedRunPlacement placed = Place("ab\ncd\nef", TextAlign.Start, TextFit.None, null,
                                             fitsWindow: FitsRows(2, tried));

        Assert.Equal((100, false, 3), (placed.WidthPercent, placed.NarrowedToFit, PresentedTextPlacement.RowCount(placed.Plan)));
        Assert.Equal([100, 50], tried);
    }

    [Fact]
    public void OverflowNarrowsOnlyPastTheWidthTheEntrysFitLeft()
    {
        const string text = "abcde fghij klmno";
        // Fit by a source of three rows keeps 100 %; the two-row window then takes it to 94 %.
        PresentedRunPlacement byRows = Place(text, TextAlign.Start, TextFit.Shrink, new TextExtent(0, 100), right: 100,
                                             sourceRows: 3, fitsWindow: FitsRows(2));
        Assert.Equal((94, true), (byRows.WidthPercent, byRows.NarrowedToFit));
        // Fit to a one-row source 100 pixels wide narrows further than the window needs: the entry's width stands.
        PresentedRunPlacement byWidth = Place(text, TextAlign.Start, TextFit.Shrink, new TextExtent(0, 100), right: 100,
                                              fitsWindow: FitsRows(2));
        Assert.False(byWidth.NarrowedToFit);
        Assert.True(byWidth.WidthPercent < 94, byWidth.WidthPercent.ToString());
    }

    // ---- the layout engine and the surface renderer --------------------------------------------------------------

    [Fact]
    public void TheLayoutEngineStartsEachRowAtItsShift()
    {
        var engine = new RetainedGlyphLayoutEngine(Cache());
        GlyphTextLayoutResult laid = engine.Render(null,
            new GlyphTextLayoutOptions(20, 0, 0, int.MaxValue, int.MaxValue, WrapHorizontally: false, Style,
                                       LineBreaksBefore: [2, 2], RowShiftsX: [5, 0, 30]),
            Requests("abcd"));

        Assert.Equal([25, 35, 30, 40], laid.Records.Select(record => record.Left));
        Assert.Equal((50, 2 * (12 + Style.LineSpacing)), (laid.CursorX, laid.CursorY));
    }

    [Fact]
    public void TheSurfaceRendererMeasuresWithoutDrawingAndPlacesNarrowedRequests()
    {
        var renderer = new ImmediateSurfaceTextRenderer(Cache(), GlyphRasterPolicy.PortableUnicode);

        ImmediateSurfaceTextResult measured = renderer.Render(null, 10, 0, " ab", Style);
        var surface = new RgbaImage(64, 8, new byte[64 * 8 * 4]);
        ImmediateSurfaceTextResult drawn = renderer.RenderGlyphs(
            surface, 0, 0, Requests("ab").Select(glyph => glyph with { WidthPercent = 50 }).ToArray(), Style);

        Assert.Equal((new TextExtent(15, 35), 35), (measured.Ink!.Value, measured.CursorX));
        Assert.Equal((new TextExtent(0, 10), 10), (drawn.Ink!.Value, drawn.CursorX));
    }
}
