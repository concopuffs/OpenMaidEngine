using System.Text;
using Ome.Engine.Frontend.Shell;
using Ome.Launcher.Core;
using Ome.Mods;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Localization;
using Ome.Mods.Manifest;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>
/// TOML forbids a key or a table given twice. Tomlyn's serializer keeps the last of them without a word (data patches
/// plan §10.3), so every reader of the engine and the launcher parses through <c>Ome.Mods.TomlDocument</c>, which
/// checks the text's structure too, comparing keys by their text (plan §12.6). Each reader refuses a repeated key as it
/// refuses a syntax error, with the line of the second one: a player's damaged file fails the way a file with a syntax
/// error fails, and nothing crashes.
/// </summary>
public sealed class RepeatedTomlKeyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-repeated-toml-" + Guid.NewGuid().ToString("N"));

    public RepeatedTomlKeyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string Manifest = "omp = 1\nid = \"example.twice\"\nname = \"Twice\"\nversion = \"1.0.0\"\n";
    private const string Game = "[game]\nprofile = \"himegari\"\ntarget = \"native\"\n";

    [Fact]
    public void TheManifestReaderRefusesAKeyGivenTwice()
    {
        var error = Assert.Throws<ModManifestException>(() => ManifestReader.Parse(Manifest + "version = \"2.0.0\"\n" + Game));
        string problem = Assert.Single(error.Problems);
        Assert.StartsWith("not valid TOML: (5,1) : error : The key `version` is already defined", problem);
    }

    [Fact]
    public void TheManifestReaderRefusesATableGivenTwice()
    {
        var error = Assert.Throws<ModManifestException>(() => ManifestReader.Parse(Manifest + Game + Game));
        Assert.StartsWith("not valid TOML: (8,1) : error : The key `game` is already defined", Assert.Single(error.Problems));
    }

    [Fact]
    public void TheManifestReaderRefusesAnArrayOfTablesThatAnotherTableInterrupts()
    {
        // Tomlyn's model alone keeps the second requirement and drops the first without a word (plan §12.6).
        var error = Assert.Throws<ModManifestException>(() => ManifestReader.Parse(
            Manifest + "[[requires]]\nid = \"a\"\n" + Game + "[[requires]]\nid = \"b\"\n"));
        Assert.StartsWith("not valid TOML: (10,1) : error : The array of tables `requires` is interrupted by another table",
                          Assert.Single(error.Problems));
    }

    [Fact]
    public void TheEventDeclarationsRefuseAKeyGivenTwice()
    {
        var error = Assert.Throws<ModManifestException>(() => ManifestReader.ParseEvents(
            "[[event]]\nname = \"a.b\"\nraised_by = \"lua\"\nname = \"a.c\"\n", "events/x.toml"));
        Assert.StartsWith("not valid TOML: (4,1) : error : The key `event[1].name` is already defined", Assert.Single(error.Problems));
    }

    [Fact]
    public void ATextCatalogRefusesAKeyGivenTwice()
    {
        var error = Assert.Throws<TextCatalogException>(() => TextCatalogFileReader.Parse(
            "[[text]]\nsource = \"a\"\ntext = \"b\"\ntext = \"c\"\n", "twice.toml"));
        Assert.StartsWith("twice.toml:4: not valid TOML: (4,1) : error : The key `text[1].text` is already defined",
                          Assert.Single(error.Problems));
    }

    [Fact]
    public void ALanguageListRefusesAKeyGivenTwice()
    {
        var error = Assert.Throws<TextCatalogException>(() => TextCatalogFileReader.ParseLanguages(
            "[[language]]\ntag = \"en\"\ntag = \"fr\"\n", "languages.toml"));
        Assert.StartsWith("languages.toml:3: not valid TOML", Assert.Single(error.Problems));
    }

    [Fact]
    public void APlayerProfileRefusesAKeyGivenTwice()
    {
        var error = Assert.Throws<ProfileFileException>(() => ProfileFile.Parse(
            "ome_profile = 1\ngame = \"kamidori\"\nmode = \"native\"\ngame = \"himegari\"\n[install]\nroot = \"i\"\n",
            _root, "p/profile.toml"));
        Assert.StartsWith("p/profile.toml: ", error.Message);
        Assert.Contains("not valid TOML: (4,1) : error : The key `game` is already defined", error.Message);
    }

    [Fact]
    public void ASaveStampWithAKeyGivenTwiceIsUnreadable()
    {
        const string stamp = "stamp = 1\nkey = \"a\"\n[selectors]\n\"example.a\" = 1\n";
        Assert.NotNull(ModSaveStamp.TryParse(stamp));
        Assert.Null(ModSaveStamp.TryParse(stamp + "\"example.a\" = 2\n"));
    }

    [Fact]
    public void TheSelectorStoreRefusesAKeyGivenTwice()
    {
        var error = Assert.Throws<InvalidDataException>(() => SelectorStore.Parse(
            "[selectors]\n\"example.a\" = 1\n\"example.a\" = 2\n", "selectors.toml"));
        Assert.StartsWith(@"selectors.toml: (3,1) : error : The key `selectors.""example.a""` is already defined", error.Message);
    }

    [Fact]
    public void TheMachineStateRefusesAKeyGivenTwice()
    {
        File.WriteAllText(Path.Combine(_root, MachineState.FileName), "ome_machine = 1\nome_machine = 1\n");
        var error = Assert.Throws<InvalidDataException>(() => MachineState.Load(_root));
        Assert.Contains("machine.toml: not valid TOML: (2,1) : error : The key `ome_machine` is already defined",
                        error.Message);
    }

    [Fact]
    public void TheLaunchersSettingsWithAKeyGivenTwiceAreUnreadable()
    {
        var home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
        Directory.CreateDirectory(home.Root);
        File.WriteAllText(home.SettingsPath, "ome_launcher = 1\ntheme = \"dark\"\ntheme = \"light\"\n");
        LauncherSettingsFile file = LauncherSettingsStore.Load(home);
        Assert.True(file.Unreadable);
        Assert.Contains("not valid TOML: (3,1) : error : The key `theme` is already defined", Assert.Single(file.Warnings));
    }

    [Fact]
    public void TheLaunchersProfileTableWithAKeyGivenTwiceStopsTheReadAsTheEnginesReaderDoes()
    {
        string root = Path.Combine(_root, "profile");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ProfileFile.FileName),
            "ome_profile = 1\ngame = \"kamidori\"\nmode = \"native\"\n[install]\nroot = \"i\"\n"
            + "[launcher]\nnotes = \"a\"\nnotes = \"b\"\n");
        var error = Assert.Throws<ProfileFileException>(() => ProfileDocuments.Read(root));
        Assert.Contains("not valid TOML: (8,1) : error : The key `launcher.notes` is already defined", error.Message);

        var warnings = new List<string>();
        Assert.Throws<Tomlyn.TomlException>(() => ProfileDocuments.ReadLauncherTable(
            "[launcher]\nnotes = \"a\"\nnotes = \"b\"\n", root, warnings));
    }

    /// <summary>Tomlyn's validating parser took two different keys for one where their hashes met, more often the more
    /// keys a table held (data patches plan §12.6); <c>TomlDocument</c> compares keys by their text, in both its forms.</summary>
    [Fact]
    public void ATableOf400000DistinctKeysReadsAndAKeyGivenTwiceInItIsFoundWithItsLine()
    {
        var text = new StringBuilder("[t]\n");
        for (int k = 0; k < 400_000; k++) text.Append('k').Append(k).Append(" = 1\n");
        Assert.Equal(400_000, Assert.IsType<TomlTable>(TomlDocument.Parse(text.ToString())!["t"]).Count);
        Assert.Equal(400_000, Assert.IsType<TomlTable>(TomlDocument.Parse(text.ToString(), out _)!["t"]).Count);
        text.Append("k12345 = 2\n");
        Assert.StartsWith("(400002,1) : error : The key `t.k12345` is already defined at (12347,1)",
                          Assert.Throws<TomlException>(() => TomlDocument.Parse(text.ToString())).Message);
        Assert.StartsWith("(400002,1) : error : The key `t.k12345` is already defined at (12347,1)",
                          Assert.Throws<TomlException>(() => TomlDocument.Parse(text.ToString(), out _)).Message);
    }

    /// <summary>The forms of a key or a table given twice that the independent review of 2026-11-19 tried, each refused
    /// with the line of the second definition (data patches plan §12.6).</summary>
    [Theory]
    [InlineData("[t]\nk = 1\nk = 2\n", 3, "t.k")]
    [InlineData("t.k = 1\nt.k = 2\n", 2, "t.k")]
    [InlineData("t = { k = 1, k = 2 }\n", 1, "t.k")]
    [InlineData("[t]\nk = 1\n[t]\nj = 2\n", 3, "t")]
    [InlineData("[t.u]\nk = 1\n[t]\nu.k = 2\n", 4, "t.u")]
    [InlineData("t.u.k = 1\n[t.u]\nk = 2\n", 2, "t.u")]
    [InlineData("[[a]]\nk = 1\n[a]\nk = 2\n", 3, "a")]
    [InlineData("a = 1\na.b = 2\n", 2, "a")]
    [InlineData("[unit.761]\nstats.max_hp = 1\n[unit.761.stats]\nmax_hp = 2\n", 3, "unit.761.stats")]
    [InlineData("[unit.761.stats]\nmax_hp = 2\n[unit.761]\nstats.max_hp = 1\n", 4, "unit.761.stats")]
    [InlineData("[t]\nk = 1\n\"k\" = 2\n", 3, "t.k")]
    [InlineData("[t]\nk = 1\n\"\\u006b\" = 2\n", 3, "t.k")]
    [InlineData("[t]\nk = 1\n'k' = 2\n", 3, "t.k")]
    [InlineData("t = { u = { k = 1 } }\n[t.u]\nj = 2\n", 2, "t")]
    [InlineData("[unit.761]\nstats = { max_hp = 1 }\nstats.max_sp = 2\n", 3, "unit.761.stats")]
    [InlineData("a = [ { k = 1, k = 2 } ]\n", 1, "a[1].k")]
    [InlineData("[t]\n\"\" = 1\n\"\" = 2\n", 3, "t.\"\"")]
    [InlineData("a = [1]\n[[a]]\n", 2, "a")]
    [InlineData("[[a]]\n[[a.b]]\nk = 1\n[a.b]\n", 4, "a[1].b")]
    public void EachFormOfAKeyOrATableGivenTwiceIsRefusedWithItsLine(string text, int line, string path)
    {
        var error = Assert.Throws<TomlException>(() => TomlDocument.Parse(text));
        Assert.StartsWith($"({line},", error.Message);
        Assert.Contains($") : error : The key `{path}` is already defined at ", error.Message);
        Assert.Equal(line, error.Line);
    }

    /// <summary>Tomlyn's model reads an array of tables that another table interrupts afresh, losing its earlier
    /// tables, or makes it a table (plan §12.6): <c>TomlDocument</c> refuses it at the header that comes back to it.</summary>
    [Theory]
    [InlineData("[[a]]\nk = 1\n[b]\n[[a]]\nk = 2\n", 4, "a")]
    [InlineData("[[a]]\nk = 1\n[b]\n[a.c]\nx = 1\n", 4, "a")]
    [InlineData("[[a]]\n[[a.b]]\nk = 1\n[a.x]\n[[a.b]]\nk = 2\n", 5, "a[1].b")]
    [InlineData("[[requires]]\nid = \"a\"\n[[data]]\nkind = \"k\"\n[[requires]]\nid = \"b\"\n", 5, "requires")]
    public void AnArrayOfTablesThatAnotherTableInterruptsIsRefusedWithItsLine(string text, int line, string path)
    {
        var error = Assert.Throws<TomlException>(() => TomlDocument.Parse(text));
        Assert.StartsWith($"({line},1) : error : The array of tables `{path}` is interrupted by another table before this "
                          + "header; write an array's tables, with their sub-tables, one after another", error.Message);
    }

    /// <summary>What TOML 1.0 takes beside the forms above: a header under dotted keys' table, a super-table defined
    /// after its sub-table, an array of tables restarting under each element, an array's tables with their sub-tables
    /// between them (TOML's own example), and one key in two spellings of two tables.</summary>
    [Theory]
    [InlineData("[fruit]\napple.color = \"red\"\n[fruit.apple.texture]\nsmooth = true\n")]
    [InlineData("[a.b.c]\nx = 1\n[a]\ny = 2\n")]
    [InlineData("[[a]]\n[[a.b]]\n[[a]]\n[[a.b]]\n")]
    [InlineData("[[fruits]]\nname = \"apple\"\n[fruits.physical]\ncolor = \"red\"\n[[fruits.varieties]]\nname = \"x\"\n"
                + "[[fruits]]\nname = \"banana\"\n")]
    [InlineData("[t]\n\"k.j\" = 1\nk.j = 2\n")]
    public void WhatTomlTakesIsRead(string text)
    {
        Assert.NotNull(TomlDocument.Parse(text));
    }
}
