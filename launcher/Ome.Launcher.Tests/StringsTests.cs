using System.Globalization;
using Ome.Launcher;

/// <summary>The launcher's string tables (launcher spec §8.8, plan §6.1): both have every key, each text formats, and the
/// language the launcher starts in.</summary>
public sealed class StringsTests
{
    [Fact]
    public void BothTablesHaveTheSameKeysAndEachTextFormatsTheSameArguments()
    {
        IReadOnlyDictionary<string, string> english = LauncherStrings.Table("en")!;
        IReadOnlyDictionary<string, string> japanese = LauncherStrings.Table("ja")!;
        Assert.Empty(english.Keys.Except(japanese.Keys));
        Assert.Empty(japanese.Keys.Except(english.Keys));
        foreach ((string key, string text) in english)
        {
            int[]? takes = LauncherStrings.Arguments(text);
            Assert.True(takes != null, $"en '{key}' does not format");
            int[]? uses = LauncherStrings.Arguments(japanese[key]);
            Assert.True(uses != null, $"ja '{key}' does not format");
            Assert.True(takes.SequenceEqual(uses),
                        $"'{key}' takes {string.Join(",", takes)} in English and {string.Join(",", uses)} in Japanese");
        }
    }

    [Theory]
    [InlineData(null, "ja-JP", "ja")]
    [InlineData(null, "en-GB", "en")]
    [InlineData(null, "fr-FR", "en")]
    [InlineData("ja", "en-US", "ja")]
    [InlineData("en", "ja-JP", "en")]
    [InlineData("fr", "ja-JP", "ja")]
    public void TheLauncherStartsInItsSettingElseTheOsLanguageElseEnglish(string? setting, string os, string expected)
        => Assert.Equal(expected, LauncherStrings.Choose(setting, CultureInfo.GetCultureInfo(os)));

    [Fact]
    public void AMissingKeyReadsFromEnglishThenAsItself()
    {
        LauncherStrings japanese = LauncherStrings.Load("ja");
        Assert.Equal("ja", japanese.Language);
        Assert.Equal("プロファイル", japanese["profiles.heading"]);
        Assert.Equal("no.such.key", japanese["no.such.key"]);
        Assert.Same(LauncherStrings.English, LauncherStrings.Load("fr"));
    }
}
