using System;
using System.Collections.Generic;
using System.Linq;
using Ome.Engine.Localization;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Localization gate 5 (spec §6.3, §6.4, §18): patterns, their captures, digits, and the ambiguity check.</summary>
public class TextPatternTests
{
    private static Dictionary<string, string>? Match(string pattern, string text)
        => TextPatternMatcher.TryMatch(TextTemplate.Parse(pattern, allowKinds: true), text, out var captures)
            ? captures
            : null;

    [Fact]
    public void APatternMatchesTheWholeStringWithTheShortestCaptures()
    {
        Assert.Equal("黒石", Match("{item}が所持限界数を超えています", "黒石が所持限界数を超えています")!["item"]);
        Assert.Null(Match("{item}が所持限界数を超えています", "黒石が所持限界数を超えています。"));
        Assert.Null(Match("『{v1}』を手に入れた", "『』を手に入れた"));   // a capture is never empty
        // As little as possible, the earlier placeholder first.
        var captures = Match("{a}と{b}", "犬と猫と鳥")!;
        Assert.Equal(("犬", "猫と鳥"), (captures["a"], captures["b"]));
        // Adjacent placeholders: the first takes one character.
        captures = Match("【{a}{b}】", "【黒石】")!;
        Assert.Equal(("黒", "石"), (captures["a"], captures["b"]));
    }

    [Fact]
    public void ANumberIsDigitsOfEitherWidthWithAnOptionalMinus()
    {
        Assert.Equal("１２", Match("武器ＬＶが{level:num}に上がった", "武器ＬＶが１２に上がった")!["level"]);
        Assert.Equal("-3", Match("評価{v:num}", "評価-3")!["v"]);
        Assert.Equal("－３", Match("評価{v:num}", "評価－３")!["v"]);
        Assert.Null(Match("評価{v:num}", "評価－"));
        Assert.Null(Match("評価{v:num}", "評価３点"));
        Assert.Null(Match("評価{v:num}", "評価3-"));
    }

    [Fact]
    public void ALaterNumberIsFoundWhereTheFirstLiteralWouldFailIt()
    {
        // A first-occurrence search would stop at the first X and fail on "bX5"; the matcher does not.
        var captures = Match("{a}X{n:num}", "aXbX5")!;
        Assert.Equal(("aXb", "5"), (captures["a"], captures["n"]));
    }

    [Fact]
    public void MatchingStaysLinearOnAPathologicalPattern()
    {
        // Both anchored literals match, so only the full search can reject it: a backtracking matcher would try every
        // split of the run of a's among 40 placeholders before the number fails.
        string pattern = "x" + string.Concat(Enumerable.Range(0, 40).Select(k => $"{{p{k}}}a")) + "{n:num}b";
        string text = "x" + new string('a', 4000) + "b";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(Match(pattern, text));
        Assert.NotNull(Match(pattern, "x" + new string('a', 4000) + "7b"));
        Assert.True(watch.ElapsedMilliseconds < 2000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void TwoPatternsOfEqualRankThatOneStringMatchesAreAmbiguous()
    {
        var catalog = TextCatalog.Build("en",
        [
            TextEntry.Create("{a}の{b}", "{b} of {a}") with { Script = "SHOP" },
            TextEntry.Create("{a}が{b}", "{a} has {b}") with { Script = "SHOP" },          // "犬の猫が鳥" matches both
            TextEntry.Create("{a}を{b:num}個", "{b} of {a}") with { Script = "SHOP" },
            TextEntry.Create("{a}は{b:num}点", "{a} scores {b}") with { Script = "SHOP" },   // ends in 点, not 個
            TextEntry.Create("{a}は{b}", "{a} is {b}") with { Script = "MENU" },          // another scope
        ]);

        var pair = Assert.Single(TextPatternOverlap.Find(catalog));
        Assert.Equal(("{a}の{b}", "{a}が{b}"), (pair.First.Source, pair.Second.Source));
    }

    [Theory]
    [InlineData("{a}を{n:num}個", "{b}を{m:num}枚", false)]
    [InlineData("評価{v:num}", "評価{w}", true)]
    [InlineData("評価{v:num}", "評価－{w}", true)]
    [InlineData("評価{v:num}点", "評価{w}円", false)]
    [InlineData("{a}x", "x{b}", true)]
    public void OverlapIsDecidedExactly(string a, string b, bool overlap)
        => Assert.Equal(overlap, TextPatternOverlap.CanMatchOneString(
            TextTemplate.Parse(a, allowKinds: true), TextTemplate.Parse(b, allowKinds: true)));

    private static TextPresenter Presenter(DigitsMode digits, params TextEntry[] entries)
        => new(new TextLanguage("en", [TextCatalog.Build("en", entries)]) { Digits = digits });

    private static readonly TextSite Site = new("SHOP", 0x40);

    [Fact]
    public void ALevelUpLinePresentsWithItsNumberMoved()
    {
        var presenter = Presenter(DigitsMode.Keep,
            TextEntry.Create("武器ＬＶが{level:num}に上がった", "Weapon level rose to {level}"));

        Assert.Equal("Weapon level rose to １２", presenter.Present("武器ＬＶが１２に上がった", Site)?.Text);
    }

    [Fact]
    public void ACapturedNameIsItselfTranslated()
    {
        var presenter = Presenter(DigitsMode.Keep,
            TextEntry.Create("{item}が所持限界数を超えています", "You cannot carry any more {item}"),
            TextEntry.Literal("黒石", "Black Stone"));

        Assert.Equal("You cannot carry any more Black Stone",
                     presenter.Present("黒石が所持限界数を超えています", Site)?.Text);
        Assert.Equal("You cannot carry any more 白石", presenter.Present("白石が所持限界数を超えています", Site)?.Text);
    }

    [Fact]
    public void AsciiDigitsConvertNumberCapturesAndBareNumbers()
    {
        var presenter = Presenter(DigitsMode.Ascii,
            TextEntry.Create("『{v1}』を{v2:num}個手に入れた！", "Got {v2} × {v1}!"),
            TextEntry.Literal("黒石", "Black Stone"));

        Assert.Equal("Got 12 × Black Stone!", presenter.Present("『黒石』を１２個手に入れた！", Site)?.Text);
        Assert.Equal("-45", presenter.Present("－４５", Site)?.Text);
        // A bare number keeps its digits under "keep", and is never a miss.
        var misses = new TextMissLog();
        var keep = new TextPresenter(new TextLanguage("en", [TextCatalog.Build("en", [])]), misses: misses);
        Assert.Null(keep.Present("４５", Site));
        Assert.Equal(0, misses.Count);
    }

    [Fact]
    public void MoreLiteralCharactersWinAndAtEqualRankTheLaterEntry()
    {
        var presenter = Presenter(DigitsMode.Keep,
            TextEntry.Create("{a}を発見！", "Found {a}!"),
            TextEntry.Create("宝箱{a}を発見！", "A chest held {a}!"),
            TextEntry.Create("{b}を発見！", "Discovered {b}!"));

        Assert.Equal("A chest held 黒石!", presenter.Present("宝箱黒石を発見！", Site)?.Text);
        Assert.Equal("Discovered 黒石!", presenter.Present("黒石を発見！", Site)?.Text);
    }

    [Fact]
    public void AFilterSeesTheResolvedCaptures()
    {
        var seen = new List<IReadOnlyDictionary<string, string>>();
        var presenter = new TextPresenter(new TextLanguage("en", [TextCatalog.Build("en",
        [
            TextEntry.Create("{item}を拾った", "Picked up {item}"),
            TextEntry.Literal("黒石", "Black Stone"),
        ])]), [new CapturingFilter(seen)]);

        presenter.Present("黒石を拾った", Site);

        Assert.Equal("Black Stone", Assert.Single(seen)["item"]);
    }

    private sealed class CapturingFilter(List<IReadOnlyDictionary<string, string>> seen) : ITextFilter
    {
        public string? Present(in TextFilterInput input)
        {
            seen.Add(input.Captures);
            return null;
        }
    }
}

/// <summary>Spec §5.3: the half cells the length opcodes count.</summary>
public class TextMeasurementTests
{
    [Theory]
    [InlineData("Illumina", 8)]
    [InlineData("イルミナ", 8)]
    [InlineData("ｲﾙﾐﾅ", 4)]
    [InlineData("黒石", 4)]
    [InlineData("αβ", 2)]       // Ambiguous counts 1
    [InlineData("１２", 4)]
    [InlineData("a😀b", 4)]     // an emoji is Wide
    [InlineData("𠮷", 2)]        // a supplementary ideograph
    public void HalfCellsCountByCharacterClass(string text, int cells)
        => Assert.Equal(cells, TextMeasurement.HalfCells(text));

    [Fact]
    public void HalvingRoundsUpAndCharactersCountScalars()
    {
        Assert.Equal(4, TextMeasurement.Halve(7));
        Assert.Equal(4, TextMeasurement.Halve(8));
        Assert.Equal(3, TextMeasurement.Characters("a😀b"));
    }

    [Fact]
    public void ForCp932TextHalfCellsAreTheBytesNativeCounts()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cp932 = System.Text.Encoding.GetEncoding(932);
        foreach (string text in new[] { "やっと匠士になる事が出来た。", "Lv12 ｶﾀｶﾅ", "「黒石」を１２個" })
            Assert.Equal(cp932.GetByteCount(text), TextMeasurement.HalfCells(text));
    }
}
