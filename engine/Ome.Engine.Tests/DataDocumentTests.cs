using System.Text;
using Ome.Engine.Modding;
using Ome.Mods.Data;

/// <summary>
/// The document of docs/superpowers/specs/2026-11-17-data-patches-design.md §5.3: a data file read into plain values,
/// with each value Lua cannot hold, and each bound, refused with its line or its path (plan §4.2, §4.3). Gate 2's
/// syntax error, date, integer past 2^53 and file past a bound are proven here at the reader, and again through the
/// link in DataLinkTests.
/// </summary>
public sealed class DataDocumentTests
{
    private static DataDocument Read(string text)
    {
        DataReadResult result = DataDocumentReader.Read(text, "data/a.toml");
        Assert.True(result.Ok, string.Join("\n", result.Problems.Select(problem => problem.Describe("data/a.toml"))));
        return result.Document!;
    }

    private static string[] Refused(string text)
    {
        DataReadResult result = DataDocumentReader.Read(text, "data/a.toml");
        Assert.False(result.Ok);
        Assert.Null(result.Document);
        return result.Problems.Select(problem => problem.Describe("data/a.toml")).ToArray();
    }

    [Fact]
    public void ADocumentIsPlainValuesWithKeysInTheFilesOrder()
    {
        DataDocument document = Read("""
            zeta = "last letter"
            alpha = true

            [unit.761]
            experience_reward = 999
            stats.max_hp = 150
            drop_chance = { drop_2 = 100 }

            [unit.12]
            rate = 0.5
            names = ["a", "b", ["c", 1]]

            [[stage]]
            id = 1

            [[stage]]
            id = -9007199254740992
            """);
        Assert.Equal("""{zeta = "last letter", alpha = true, unit = {761 = {experience_reward = 999, stats = {max_hp = 150}, """
                     + """drop_chance = {drop_2 = 100}}, 12 = {rate = 0.5, names = ["a", "b", ["c", 1]]}}, """
                     + """stage = [{id = 1}, {id = -9007199254740992}]}""", document.Root.ToText());
        Assert.Equal(["zeta", "alpha", "unit", "stage"], document.Root.Entries.Select(entry => entry.Key));
        var unit = Assert.IsType<DataTable>(document.Root["unit"]);
        Assert.Equal(["761", "12"], unit.Entries.Select(entry => entry.Key));
        Assert.Equal(999, Assert.IsType<DataInteger>(Assert.IsType<DataTable>(unit["761"])["experience_reward"]).Value);
        Assert.Null(unit["13"]);
        // Each string, boolean and number counts one, and so does each table and array (plan §12.6): twelve values;
        // the tables unit, 761, stats, drop_chance, 12 and stage's two; the arrays names, its inner array and stage.
        Assert.Equal(new DataSize(12, 7, 3), document.Size);
        Assert.Equal(22, document.Size.Total);
        Assert.Equal("12 values in 7 tables and 3 arrays", document.Size.Describe());
    }

    [Fact]
    public void AnEmptyFileIsAnEmptyDocument()
    {
        DataDocument document = Read("# nothing yet\n");
        Assert.Empty(document.Root.Entries);
        Assert.Equal(new DataSize(0, 0, 0), document.Size);
        Assert.Equal("no value", document.Size.Describe());
    }

    [Theory]
    [InlineData("a = 1\nb = \nc = 3", "data/a.toml:2: Missing value after `=`.")]
    [InlineData("a = 1\n[t\nb = 2", "data/a.toml:2:")]
    // Tomlyn's model alone takes a repeated key, the last winning; the reader refuses it, as TOML 1.0 does.
    [InlineData("a = 1\na = 2", "data/a.toml:2: The key `a` is already defined")]
    [InlineData("[t]\nx = 1\n[t]\ny = 2", "data/a.toml:3: The key `t` is already defined")]
    [InlineData("a = { b = 1, b = 2 }", "data/a.toml:1: The key `a.b` is already defined")]
    public void ASyntaxErrorIsRefusedWithItsLine(string text, string problem)
    {
        Assert.Contains(Refused(text), found => found.StartsWith(problem, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[unit.761]\nwhen = 1979-05-27T07:32:00Z", "data/a.toml: unit.761.when: a date or a time has no value here")]
    [InlineData("a = 1979-05-27", "data/a.toml: a: a date or a time has no value here")]
    [InlineData("a = [1, 07:32:00]", "data/a.toml: a[2]: a date or a time has no value here")]
    [InlineData("a = inf", "data/a.toml: a: a float that is not finite (inf or nan) has no value here")]
    [InlineData("a = { b = -nan }", "data/a.toml: a.b: a float that is not finite (inf or nan) has no value here")]
    [InlineData("a = 1e309", "data/a.toml: a: a float that is not finite (inf or nan) has no value here")]
    public void AValueLuaCannotHoldIsRefusedWithItsPath(string text, string problem)
    {
        Assert.Equal([problem], Refused(text));
    }

    [Theory]
    [InlineData("9007199254740993")]
    [InlineData("-9007199254740993")]
    [InlineData("9_007_199_254_740_993")]
    [InlineData("9223372036854775807")]
    // Tomlyn wraps these three into a long: -1, -1 and -9223372036854775808. The reader reads them as written.
    [InlineData("18446744073709551615")]
    [InlineData("0xFFFFFFFFFFFFFFFF")]
    [InlineData("9223372036854775808")]
    [InlineData("0b1111111111111111111111111111111111111111111111111111111111111111")]
    [InlineData("0o777777777777777777")]
    [InlineData("0x20000000000001")]
    public void AnIntegerPast2To53IsRefusedWithItsLine(string written)
    {
        Assert.Equal([$"data/a.toml:2: the integer {written} is past 2^53 in size, which Lua cannot hold as written"],
                     Refused($"ok = 1\nbig = {written}"));
    }

    [Theory]
    [InlineData("9007199254740992", 9007199254740992)]
    [InlineData("-9007199254740992", -9007199254740992)]
    [InlineData("+9_007_199_254_740_992", 9007199254740992)]
    [InlineData("0x20000000000000", 9007199254740992)]
    [InlineData("0o400000000000000000", 9007199254740992)]
    [InlineData("0b100000000000000000000000000000000000000000000000000000", 9007199254740992)]
    [InlineData("0", 0)]
    public void AnIntegerUpTo2To53IsItself(string written, long value)
    {
        Assert.Equal(value, Assert.IsType<DataInteger>(Read($"a = {written}").Root["a"]).Value);
    }

    private const string Hidden = "a control or format character, a line or paragraph separator, or half of a surrogate pair";

    /// <summary>Plan §12.6: a control character, a format character (U+202E turns the text after it around in a message,
    /// U+200B is not seen at all, U+E0001 is one past U+FFFF), a line or a paragraph separator: each is refused in a key,
    /// with its code point, and the key is printed with it escaped.</summary>
    [Theory]
    [InlineData("[t]\n\"a\\u0001b\" = 1", @"t.""a\u0001b""", "0001")]
    [InlineData("\"\\u0085\" = 1", @"""\u0085""", "0085")]
    [InlineData("\"ab\\u202Ec\" = 1", @"""ab\u202Ec""", "202E")]
    [InlineData("\"a\u200Bb\" = 1", @"""a\u200Bb""", "200B")]
    [InlineData("\"a\\u2028\" = 1", @"""a\u2028""", "2028")]
    [InlineData("[t]\n\"\\u2029\" = 1", @"t.""\u2029""", "2029")]
    [InlineData("\"tag\\U000E0001\" = 1", @"""tag\U000E0001""", "E0001")]
    public void AKeyWithACharacterThatDoesNotPrintIsRefusedAndPrintedEscaped(string text, string path, string code)
    {
        Assert.Equal([$"data/a.toml: {path}: a key holds a character that does not print (U+{code}): {Hidden}"], Refused(text));
    }

    [Fact]
    public void AStringValueMayHoldACharacterThatDoesNotPrintAndPrintsItEscaped()
    {
        // Only keys are refused: a string value keeps what the file gives it, and prints it escaped.
        DataTable root = Read("a = \"a\\u0001b\\u202E\\n\"\nb = \"\U0001F600 \\U000E0001\"").Root;
        Assert.Equal("a\u0001b\u202E\n", Assert.IsType<DataString>(root["a"]).Value);
        Assert.Equal(@"{a = ""a\u0001b\u202E\u000A"", b = ""😀 \U000E0001""}", root.ToText());
        // A key of letters outside ASCII is quoted, and kept.
        Assert.Equal(@"{""名前"" = 1, ""é"" = 2}", Read("\"名前\" = 1\n\"é\" = 2").Root.ToText());
    }

    [Fact]
    public void HalfOfASurrogatePairIsNotPrintedAsItIs()
    {
        // A TOML file cannot give one (its escapes name scalar values and its text is UTF-8), so it is checked here.
        Assert.Equal(0xD800, DataValue.FirstHidden("a\uD800b"));
        Assert.Equal(0xDC00, DataValue.FirstHidden("\uDC00"));
        Assert.Equal(-1, DataValue.FirstHidden("\U0001F600 a"));
        Assert.Equal(0xE0001, DataValue.FirstHidden("a\U000E0001"));
        Assert.Equal(@"""a\uD800b""", DataValue.KeyText("a\uD800b"));
    }

    [Fact]
    public void AKeyPastTheBoundIsRefused()
    {
        string longest = new('k', DataBounds.MaxKeyLength);
        Assert.NotNull(Read($"[t]\n{longest} = 1").Root["t"]);
        Assert.Equal([$"data/a.toml: t: a key of {DataBounds.MaxKeyLength + 1} characters starts {new string('k', 32)}; "
                      + $"a key is at most {DataBounds.MaxKeyLength} characters"],
                     Refused($"[t]\n{longest}k = 1"));
    }

    [Fact]
    public void NestingPastTheBoundIsRefusedWithItsPath()
    {
        // The document's table is the first level, so 16 dotted keys make 15 tables in it, the last holding a value.
        string deepest = string.Join(".", Enumerable.Range(1, DataBounds.MaxDepth).Select(k => $"k{k}")) + " = 1";
        Assert.Equal(DataBounds.MaxDepth, Read(deepest).Size.Total);
        string past = string.Join(".", Enumerable.Range(1, DataBounds.MaxDepth + 1).Select(k => $"k{k}")) + " = 1";
        string path = string.Join(".", Enumerable.Range(1, DataBounds.MaxDepth).Select(k => $"k{k}"));
        Assert.Equal([$"data/a.toml: {path}: tables and arrays nest deeper than {DataBounds.MaxDepth}"], Refused(past));
        Assert.Equal([$"data/a.toml: a{string.Concat(Enumerable.Repeat("[1]", DataBounds.MaxDepth - 1))}: "
                      + $"tables and arrays nest deeper than {DataBounds.MaxDepth}"],
                     Refused("a = " + new string('[', DataBounds.MaxDepth) + new string(']', DataBounds.MaxDepth)));
        // Past Tomlyn's own bound the parser stops, and the reader says the same thing without a path.
        Assert.Contains(Refused("a = " + new string('[', 100_000)),
                        problem => problem.EndsWith($"tables and arrays nest deeper than {DataBounds.MaxDepth}", StringComparison.Ordinal));
    }

    /// <summary>The size bound counts each value, table and array as one (plan §12.6): a file of empty tables or empty
    /// arrays has a size, and past the bound the read stops before Tomlyn builds anything.</summary>
    [Fact]
    public void TheSizeBoundCountsEachValueTableAndArray()
    {
        string tooLarge = $"data/a.toml: the document holds more than {DataBounds.MaxValues:N0} values, counting each table "
                          + "and array as one; a data file holds at most that many";
        Assert.Equal(tooLarge, "data/a.toml: " + DataDocumentReader.TooLarge);
        static string Zeros(int count) => "a = [" + string.Join(",", Enumerable.Repeat("0", count)) + "]";
        Assert.Equal(DataBounds.MaxValues, Read(Zeros(DataBounds.MaxValues - 1)).Size.Total);
        Assert.Equal([tooLarge], Refused(Zeros(DataBounds.MaxValues)));
        static string Tables(int count) => string.Concat(Enumerable.Range(0, count).Select(k => $"[t{k}]\n"));
        Assert.Equal(DataBounds.MaxValues, Read(Tables(DataBounds.MaxValues)).Size.Total);
        Assert.Equal([tooLarge], Refused(Tables(DataBounds.MaxValues + 1)));
        static string Arrays(int count) => "a = [" + string.Concat(Enumerable.Repeat("[],", count)) + "]";
        Assert.Equal(DataBounds.MaxValues, Read(Arrays(DataBounds.MaxValues - 1)).Size.Total);
        // The review's files: 5.3 million empty arrays and 1.6 million empty tables, read before at 4.1 and 2.6 GB.
        Assert.Equal([tooLarge], Refused(Arrays(5_300_000)));
        Assert.Equal([tooLarge], Refused(Tables(1_600_000)));
        // A header's super-tables count once, and an array of tables counts with each of its tables.
        Assert.Equal(new DataSize(0, 3, 0), Read("[a.b.c]\n[a.b]\n").Size);
        Assert.Equal(new DataSize(0, 2, 1), Read("[[a]]\n[[a]]\n").Size);
        Assert.Equal(new DataSize(0, 1, 0), Read("a = {}").Size);
        // The report's line (plan §12.6): the values, then the tables and arrays they are in.
        Assert.Equal("1 value in 3 tables", Read("[unit.761]\nstats.max_hp = 150\n").Size.Describe());
        Assert.Equal("2 values in 1 array", Read("a = [1, 2]").Size.Describe());
        Assert.Equal("1 value", Read("a = 1").Size.Describe());
        Assert.Equal("no value in 1 table", Read("[a]").Size.Describe());
        Assert.Equal("1,000 values in 1 array", Read(Zeros(1000)).Size.Describe());
    }

    /// <summary>Tomlyn's validating parser took two different keys for one where their hashes met (plan §12.6); the
    /// reader compares keys by their text.</summary>
    [Fact]
    public void ATableOf400000DistinctKeysReadsAndAKeyGivenTwiceInItIsRefusedWithItsLine()
    {
        var text = new StringBuilder();
        for (int k = 0; k < DataBounds.MaxValues; k++) text.Append('k').Append(k).Append(" = 1\n");
        Assert.Equal(DataBounds.MaxValues, Read(text.ToString()).Root.Count);
        text.Remove(text.Length - "k399999 = 1\n".Length, "k399999 = 1\n".Length).Append("k12345 = 2\n");
        Assert.Equal(["data/a.toml:400000: The key `k12345` is already defined at (12346,1) and cannot be redefined"],
                     Refused(text.ToString()));
    }

    [Theory]
    [InlineData("[t]\nk = 1\nk = 2\n", 3, "t.k")]
    [InlineData("t = { k = 1, k = 2 }\n", 1, "t.k")]
    [InlineData("t.u.k = 1\n[t.u]\nk = 2\n", 2, "t.u")]
    [InlineData("[[a]]\nk = 1\n[a]\nk = 2\n", 3, "a")]
    [InlineData("[unit.761]\nstats.max_hp = 1\n[unit.761.stats]\nmax_hp = 2\n", 3, "unit.761.stats")]
    [InlineData("[t]\nk = 1\n'k' = 2\n", 3, "t.k")]
    [InlineData("a = [ { k = 1, k = 2 } ]\n", 1, "a[1].k")]
    [InlineData("[t]\n\"\" = 1\n\"\" = 2\n", 3, "t.\"\"")]
    public void AKeyOrATableGivenTwiceIsRefusedWithItsLine(string text, int line, string path)
    {
        string problem = Assert.Single(Refused(text));
        Assert.StartsWith($"data/a.toml:{line}: The key `{path}` is already defined at ", problem);
    }

    /// <summary>Tomlyn's model would keep the second <c>rows</c> table alone (plan §12.6).</summary>
    [Fact]
    public void AnArrayOfTablesThatAnotherTableInterruptsIsRefusedWithItsLine()
    {
        Assert.Equal(["data/a.toml:4: The array of tables `rows` is interrupted by another table before this header; write "
                      + "an array's tables, with their sub-tables, one after another"],
                     Refused("[[rows]]\nn = 1\n[other]\n[[rows]]\nn = 2\n"));
        // A table inside the array's last table does not interrupt it: rows, its two tables, their n, and inner.
        Assert.Equal(new DataSize(2, 3, 1), Read("[[rows]]\nn = 1\n[rows.inner]\n[[rows]]\nn = 2\n").Size);
    }

    [Fact]
    public void ProblemsAreKeptToABoundAndCounted()
    {
        var text = new StringBuilder();
        for (int k = 0; k < 30; k++) text.Append($"d{k} = 1979-05-27\n");
        DataReadResult result = DataDocumentReader.Read(text.ToString(), "data/a.toml");
        Assert.Equal(30, result.ProblemCount);
        Assert.Equal(DataDocumentReader.ProblemsKept, result.Problems.Count);
        Assert.Equal("d0: a date or a time has no value here", result.Problems[0].Text);
    }

    [Fact]
    public void AValueIsCheckedWhenItIsMade()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataInteger(DataInteger.Limit + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataFloat(double.NaN));
        Assert.Throws<ArgumentException>(() => new DataTable([new("a", new DataBoolean(true)), new("a", new DataBoolean(false))]));
        Assert.Equal(@"{""a b"" = 1.0, ""q\""\\"" = -0.5}",
                     new DataTable([new("a b", new DataFloat(1)), new("q\"\\", new DataFloat(-0.5))]).ToText());
    }
}
