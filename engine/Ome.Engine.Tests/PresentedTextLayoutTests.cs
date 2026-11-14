using System.Linq;
using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Text;
using Xunit;

/// <summary>Where presented text's rows break (localization spec §7.1), with every glyph 10 pixels wide unless a test
/// lays it out in aspect mode.</summary>
public class PresentedTextLayoutTests
{
    private sealed class FixedRasterizer : IGlyphMaskRasterizer
    {
        public GlyphMask Rasterize(GlyphRasterRequest request)
            => new(1, 1, 1, originX: 0, originY: 5, cellAdvanceX: 10, cellAdvanceY: 0, cellWidth: 10, cellHeight: 12,
                   [16]);
    }

    private static readonly AdvTextStyle Style = AdvTextStyle.Default with { PrimaryFontSize = 12 };

    /// <summary>The rows the plan draws, joined by <c>|</c>.</summary>
    private static string Rows(string text, int rightBound, int cursorX = 0, LineBreakMode mode = LineBreakMode.Words,
                               int aspectMode = 0)
    {
        AdvTextStyle style = Style with { AspectMode = aspectMode };
        var requests = ImmediateSurfaceTextRenderer.CreateRequests(text, style, GlyphRasterPolicy.PortableUnicode);
        GlyphRoom room = new RetainedGlyphLayoutEngine(new FixedRasterizer())
            .Room(style, GlyphRasterPolicy.PortableUnicode, antialias: true);
        PresentedTextPlan plan = PresentedTextLayout.Plan(text, PresentedLineBreaks.Opportunities(text, mode), requests,
            room, cursorX, lineOriginX: 0, rightBound);
        var rows = new StringBuilder();
        for (int glyph = 0, next = 0; glyph <= plan.Glyphs.Count; glyph++)
        {
            for (; next < plan.BreaksBefore.Count && plan.BreaksBefore[next] == glyph; next++) rows.Append('|');
            if (glyph < plan.Glyphs.Count) rows.Append(char.ConvertFromUtf32(plan.Glyphs[glyph].UnicodeScalar));
        }
        return rows.ToString();
    }

    [Fact]
    public void WordsMoveToTheNextRowAndTheSpaceAtTheBreakIsDropped()
    {
        Assert.Equal("I|picked|up a|stone", Rows("I picked up a stone", rightBound: 60));
        Assert.Equal("I picked up a stone", Rows("I picked up a stone", rightBound: 190));
    }

    [Fact]
    public void AWordLongerThanARowBreaksAtTheBound()
        => Assert.Equal("Superc|alifra|gilist|ic|word", Rows("Supercalifragilistic word", rightBound: 60));

    [Fact]
    public void ARunThatBeganMidRowMayBreakBeforeItsFirstWord()
    {
        Assert.Equal("|hello|world", Rows("hello world", rightBound: 60, cursorX: 40));
        Assert.Equal("hi|world", Rows("hi world", rightBound: 60, cursorX: 40));
    }

    [Fact]
    public void InAspectModeEachGlyphTakesItsClasssCell()
    {
        // The reference cell is 10 pixels: a Latin letter takes half of it, Ⅰ (two bytes in CP932) all of it.
        Assert.Equal("Cover I|and|more", Rows("Cover I and more", rightBound: 35, aspectMode: 1));
        Assert.Equal("Cover|Ⅰ and|more", Rows("Cover Ⅰ and more", rightBound: 35, aspectMode: 1));
    }

    [Fact]
    public void ControlCharactersOtherThanNewlinesStay()
        => Assert.Equal("a\tb", Rows("a\tb", rightBound: 600));

    [Fact]
    public void NewlinesBreakAndTheirSpacesAreDropped()
    {
        Assert.Equal("one|two", Rows("one  \ntwo", rightBound: 600));
        Assert.Equal("a|", Rows("a\n", rightBound: 600));
        Assert.Equal("a|  b", Rows("a\n  b", rightBound: 600));
    }

    [Fact]
    public void CjkBreaksBetweenCharactersButNotBeforeClosingPunctuation()
    {
        Assert.Equal("あいうえおか|きくけこ。", Rows("あいうえおかきくけこ。", rightBound: 60));
        Assert.Equal("あいうえ|お。かき", Rows("あいうえお。かき", rightBound: 50));
    }
}
