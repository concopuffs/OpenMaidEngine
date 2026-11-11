using System.Linq;
using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Text;
using Xunit;

/// <summary>Where presented text's rows break (localization spec §7.1), with every glyph 10 pixels wide.</summary>
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
    private static string Rows(string text, int rightBound, int cursorX = 0, LineBreakMode mode = LineBreakMode.Words)
    {
        var requests = ImmediateSurfaceTextRenderer.CreateRequests(text, Style, GlyphRasterPolicy.PortableUnicode);
        PresentedTextPlan plan = PresentedTextLayout.Plan(text, PresentedLineBreaks.Opportunities(text, mode), requests,
            new FixedRasterizer(), cursorX, lineOriginX: 0, rightBound);
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
    public void ARunThatBeganMidRowMovesDownWhole()
        => Assert.Equal("|hello|world", Rows("hello world", rightBound: 60, cursorX: 40));

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
