using Ome.Engine.Sys4;
using Ome.Engine.Text;

namespace Ome.Engine.Tests;

/// <summary>The characters a horizontal ADV layout keeps on the line they overflow, and where each engine build
/// takes them from (docs/engine-re.md, "Kamidori against Arterial, triaged").</summary>
public sealed class LineEndCharactersTests
{
    private static Sys4StartupSettings Settings(params (string Key, string Value)[] pairs)
        => Sys4StartupSettings.FromPairs(pairs.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));

    [Theory]
    [InlineData(0x8141)] // 、
    [InlineData(0x8142)] // 。
    [InlineData(0x8176)] // 」
    public void TheFixedThreeAreNativesClosingPunctuation(int cp932)
        => Assert.True(LineEndCharacters.FixedThree.Contains((ushort)cp932));

    [Theory]
    [InlineData(0x8140)] // a full-width space
    [InlineData(0x82a0)] // あ
    [InlineData(0x816a)] // ）
    public void TheFixedThreeHoldNothingElse(int cp932)
        => Assert.False(LineEndCharacters.FixedThree.Contains((ushort)cp932));

    [Fact]
    public void TheFixedRuleIgnoresTheCatalog()
        => Assert.Same(LineEndCharacters.FixedThree,
                       LineEndRule.FixedThree.For(Settings(("JUSTIFYCHAR", "）"))));

    [Fact]
    public void TheCatalogRuleTakesEveryJustifyCharPairAndNothingElse()
    {
        // 創刻のアテリアル's four, among other keys.
        LineEndCharacters characters = LineEndRule.CatalogList.For(Settings(
            ("VALIDFONTCHECKER", "悪"), ("JUSTIFYCHAR", "、"), ("JUSTIFYCHAR", "。"), ("justifychar", "」"),
            ("JUSTIFYCHAR", "）"), ("SAVEWINPOS", "1")));

        Assert.Equal(new ushort[] { 0x8141, 0x8142, 0x816a, 0x8176 }, characters.Codes.Order());
        Assert.False(characters.Contains(0x88ab)); // 悪 belongs to another key
    }

    [Fact]
    public void ACatalogWithoutTheKeyKeepsNoCharacterOnItsLine()
    {
        LineEndCharacters characters = LineEndRule.CatalogList.For(Settings(("RUBYSHIFTMODE", "1")));

        Assert.Empty(characters.Codes);
        Assert.False(characters.Contains(0x8141));
    }

    [Fact]
    public void AnEntryMatchesOnlyAsOneWholeCharacter()
    {
        // Native compares the glyph's one or two bytes with the whole entry.
        LineEndCharacters characters = LineEndCharacters.Of(["!", "ab", "、。", "", "ｱ", "\u00e9"]);

        Assert.Equal(new ushort[] { 0x21, 0xb1 }, characters.Codes.Order());
    }

    [Fact]
    public void TheSettingsReadTheWindowPositionKey()
    {
        Assert.True(Settings(("SAVEWINPOS", "1")).SaveWindowPosition);
        Assert.False(Settings(("SAVEWINPOS", "0")).SaveWindowPosition);
        Assert.False(Settings().SaveWindowPosition);
        // Native's loader and writer test for 1; another value keeps no position.
        Assert.False(Settings(("SAVEWINPOS", "2")).SaveWindowPosition);
    }
}
