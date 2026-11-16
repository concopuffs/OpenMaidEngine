using System.Globalization;
using System.Text.RegularExpressions;
using Ome.Launcher;

/// <summary>The launcher's string tables (launcher spec §8.8, plan §6.1): both have every key, each text formats, and the
/// language the launcher starts in.</summary>
public sealed partial class StringsTests
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

    /// <summary>Every key the launcher's source names is in the tables. A view's <c>S[key]</c> binding is not checked
    /// when the view compiles, and a missing key shows as itself (the Duplicate dialog read "saves.title", plan §10).
    /// The source is read from the checkout: a view's bindings, and each plain string literal inside a view model's
    /// <c>S[…]</c> or <c>S.Format(…)</c>. A key put together at run time (<c>$"language.{tag}"</c>) is not seen.</summary>
    [Fact]
    public void EveryKeyTheSourceNamesIsInTheTables()
    {
        string source = SourceFolder();
        IReadOnlyDictionary<string, string> english = LauncherStrings.Table("en")!;
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string view in Directory.EnumerateFiles(source, "*.axaml", SearchOption.AllDirectories))
            foreach (Match binding in ViewKey().Matches(File.ReadAllText(view)))
                named.Add(binding.Groups[1].Value);
        foreach (string code in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(code);
            foreach (Match use in CodeUse().Matches(text))
            {
                string arguments = Enclosed(text, use.Index + use.Length - 1);
                foreach (Match literal in KeyLiteral().Matches(arguments))
                    named.Add(literal.Groups[1].Value);
            }
        }
        Assert.Contains("duplicate.saves", named);   // the scan sees the source at all
        Assert.Empty(named.Where(key => !english.ContainsKey(key)));
    }

    /// <summary><c>launcher/Ome.Launcher</c> of the checkout the tests were built from.</summary>
    private static string SourceFolder()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at != null; at = at.Parent)
        {
            string candidate = Path.Combine(at.FullName, "Ome.Launcher", "Views");
            if (Directory.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
        }
        throw new DirectoryNotFoundException($"no launcher source above {AppContext.BaseDirectory}");
    }

    /// <summary>The text from the bracket at <paramref name="open"/> to its match, without the brackets.</summary>
    private static string Enclosed(string text, int open)
    {
        char opening = text[open], closing = opening == '[' ? ']' : ')';
        int depth = 0;
        for (int at = open; at < text.Length; at++)
        {
            if (text[at] == '"')
            {
                at = text.IndexOf('"', at + 1);
                if (at < 0) break;
            }
            else if (text[at] == opening) depth++;
            else if (text[at] == closing && --depth == 0) return text[(open + 1)..at];
        }
        throw new InvalidDataException($"an unclosed {opening} at {open}");
    }

    [GeneratedRegex(@"\bS\[([a-z0-9][a-z0-9.-]*)\]")]
    private static partial Regex ViewKey();

    [GeneratedRegex(@"\bS(?:\[|\.Format\()")]
    private static partial Regex CodeUse();

    /// <summary>A plain literal (not interpolated) shaped like a key: dotted lowercase words.</summary>
    [GeneratedRegex(@"(?<![$@\w])""([a-z][a-z0-9-]*(?:\.[a-z0-9-]+)+)""")]
    private static partial Regex KeyLiteral();

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
