using Ome.Engine.Frontend.Shell;
using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Localization;
using Ome.Mods.Manifest;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>
/// TOML forbids a key or a table given twice. Tomlyn's serializer keeps the last of them without a word (data patches
/// plan §10.3), so every reader of the engine and the launcher parses through <c>Ome.Mods.TomlDocument</c>, which
/// validates the text too. Each reader refuses a repeated key as it refuses a syntax error, with the line of the second
/// one: a player's damaged file fails the way a file with a syntax error fails, and nothing crashes.
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
    public void TheEventDeclarationsRefuseAKeyGivenTwice()
    {
        var error = Assert.Throws<ModManifestException>(() => ManifestReader.ParseEvents(
            "[[event]]\nname = \"a.b\"\nraised_by = \"lua\"\nname = \"a.c\"\n", "events/x.toml"));
        Assert.StartsWith("not valid TOML: (4,1) : error : The key `event.name` is already defined", Assert.Single(error.Problems));
    }

    [Fact]
    public void ATextCatalogRefusesAKeyGivenTwice()
    {
        var error = Assert.Throws<TextCatalogException>(() => TextCatalogFileReader.Parse(
            "[[text]]\nsource = \"a\"\ntext = \"b\"\ntext = \"c\"\n", "twice.toml"));
        Assert.StartsWith("twice.toml:4: not valid TOML: (4,1) : error : The key `text.text` is already defined",
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
        Assert.StartsWith("selectors.toml: (3,1) : error : The key `selectors.example.a` is already defined", error.Message);
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
}
