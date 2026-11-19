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
        // Each string, boolean and number counts once, and an array's elements each: 2 + 3 + 5 + 2.
        Assert.Equal(12, document.Values);
    }

    [Fact]
    public void AnEmptyFileIsAnEmptyDocument()
    {
        DataDocument document = Read("# nothing yet\n");
        Assert.Empty(document.Root.Entries);
        Assert.Equal(0, document.Values);
    }

    [Theory]
    [InlineData("a = 1\nb = \nc = 3", "data/a.toml:2: Unexpected token end-of-line found while expecting a value")]
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

    [Fact]
    public void AKeyWithAControlCharacterIsRefusedAndPrintedEscaped()
    {
        Assert.Equal([@"data/a.toml: t.""a\u0001b"": a key holds a control character (U+0001)"],
                     Refused("[t]\n\"a\\u0001b\" = 1"));
        Assert.Equal([@"data/a.toml: ""\u0085"": a key holds a control character (U+0085)"], Refused("\"\\u0085\" = 1"));
        // A string value may hold one: only keys are printed.
        Assert.Equal("a\u0001b\n", Assert.IsType<DataString>(Read("a = \"a\\u0001b\\n\"").Root["a"]).Value);
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
        Assert.Equal(1, Read(deepest).Values);
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

    [Fact]
    public void TheValueBoundCountsEachArrayElement()
    {
        static string Zeros(int count) => "a = [" + string.Join(",", Enumerable.Repeat("0", count)) + "]";
        Assert.Equal(DataBounds.MaxValues, Read(Zeros(DataBounds.MaxValues)).Values);
        Assert.Equal([$"data/a.toml: the document holds more than {DataBounds.MaxValues:N0} values; a data file holds at most that many"],
                     Refused(Zeros(DataBounds.MaxValues) + "\nb = 1"));
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
