using System.Text;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Localization;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;
using Ome.Script.Abi;
using Ome.Script.Patch;

/// <summary>Language packages (localization spec §8, §9; milestone 4): the version 2 manifest and its
/// <c>[[language]]</c> tables, the <c>text/</c> and <c>fonts/</c> folders, the text part of a link and the link
/// cache, presentation-only save stamps, and the offset tables that move site entries through patched scripts.
/// Packages are synthesized in a temporary folder per test.</summary>
public sealed class LanguagePackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-lang-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = new();

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);
    private const string Installed = "sha256:1111111111111111111111111111111111111111111111111111111111111111";

    public void Dispose()
    {
        foreach (ModPackage mod in _opened) mod.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string ManifestText(string id, string tables = "", int omp = 2)
        => $"omp = {omp}\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n\n[game]\nprofile = \"himegari\"\n"
           + $"target = \"native\"\n{tables}\n";

    private string Folder(string id, string tables, IReadOnlyDictionary<string, string>? files = null, int omp = 2,
                          IReadOnlyDictionary<string, byte[]>? binary = null)
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), ManifestText(id, tables, omp), new UTF8Encoding(false));
        foreach (var (path, text) in files ?? new Dictionary<string, string>())
            Write(Path.Combine(folder, path), new UTF8Encoding(false).GetBytes(text));
        foreach (var (path, bytes) in binary ?? new Dictionary<string, byte[]>())
            Write(Path.Combine(folder, path), bytes);
        return folder;
    }

    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private ModPackage Mod(string id, string tables, IReadOnlyDictionary<string, string>? files = null,
                           IReadOnlyDictionary<string, byte[]>? binary = null)
    {
        ModPackage mod = ModPackage.Open(Folder(id, tables, files, binary: binary));
        _opened.Add(mod);
        return mod;
    }

    private static ModLinkBase SyntheticBase(params (string Name, byte[] Bytes)[] scripts) => new()
    {
        Profile = "himegari",
        CatalogFingerprint = Installed,
        ReadScript = name => scripts.FirstOrDefault(script => script.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Bytes,
        InstalledScripts = scripts.Select(script => (script.Name, (Func<byte[]>)(() => script.Bytes))).ToList(),
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
    };

    private const string English = """
        [[language]]
        tag = "en"
        name = "English"
        glyph_delay_scale = 0.5

        [[language.font]]
        requested = ["*"]
        use = ["fonts/Body.ttf", "Georgia"]
        """;

    // ------------------------------------------------------------------------------------------- manifest and layout

    [Fact]
    public void AVersionTwoManifestDeclaresItsLanguages()
    {
        ModManifest manifest = ManifestReader.Parse(ManifestText("lang.en",
            English.Replace("glyph_delay_scale = 0.5", "glyph_delay_scale = 0.5\nfallback = [\"en-gb\"]")));

        Assert.Equal(2, manifest.OmpVersion);
        LanguageSettings language = Assert.Single(manifest.Languages);
        Assert.Equal(("en", "English", 0.5), (language.Tag, language.Name, language.GlyphDelayScale));
        Assert.Equal(["en-gb"], language.Fallback);
        Assert.Equal(["en-gb"], language.Fallback);
        LanguageFont font = Assert.Single(language.Fonts!);
        Assert.Equal(["*"], font.Requested);
        Assert.Equal(["fonts/Body.ttf", "Georgia"], font.Use);
        Assert.True(font.Applies("ＭＳ ゴシック"));
        Assert.Equal([true, false], font.Use.Select(LanguageFont.IsBundled));
    }

    [Theory]
    [InlineData(1, "[[language]]\ntag = \"en\"", "language: [[language]] needs omp = 2")]
    [InlineData(2, "[[language]]\ntag = \"x-pseudo\"", "tag 'x-pseudo' is not a language a package can define")]
    [InlineData(2, "[[language]]\ntag = \"en\"\n[[language]]\ntag = \"EN\"", "language 'en' is declared twice")]
    [InlineData(2, "[[language]]\ntag = \"en\"\nglyph_delay_scale = 0", "glyph_delay_scale must be above 0 and at most 4")]
    [InlineData(2, "[[language]]\ntag = \"en\"\nmin_fit_scale = 1.5", "min_fit_scale must be above 0 and at most 1")]
    [InlineData(2, "[[language]]\ntag = \"en\"\n[[language.font]]\nrequested = []\nuse = [\"Georgia\"]",
                "requested must name at least one face")]
    [InlineData(2, "[[language]]\ntag = \"en\"\n[[language.font]]\nrequested = [\"*\"]\nuse = [\"fonts/a/b.ttf\"]",
                "'fonts/a/b.ttf' is not a font file under fonts/")]
    [InlineData(2, "[[language]]\ntag = \"en\"\nfont_size = 3", "unknown key 'font_size'")]
    public void BadLanguageTablesAreRejectedWithTheirReason(int omp, string tables, string reason)
    {
        var error = Assert.Throws<ModManifestException>(() => ManifestReader.Parse(ManifestText("lang.en", tables, omp)));
        Assert.Contains(error.Problems, problem => problem.Contains(reason));
    }

    [Fact]
    public void TextAndFontsAreSortedIntoTheLayoutAndMakeAPresentationPackage()
    {
        ModPackage mod = Mod("lang.en", English, new Dictionary<string, string>
        {
            ["text/en/sub/SC0100.toml"] = "",
            ["text/en/_global.toml"] = "",
            ["text/de/a.toml"] = "",
        }, new Dictionary<string, byte[]> { ["fonts/Body.ttf"] = [1, 2, 3] });

        Assert.Equal([("de", "a.toml"), ("en", "_global.toml"), ("en", "sub/SC0100.toml")],
                     mod.Layout.Text.Select(file => (file.Language, file.Name)));
        Assert.Equal("Body.ttf", Assert.Single(mod.Layout.Fonts).Name);
        Assert.True(mod.Layout.ChangesNoContent);
        Assert.True(mod.PresentationOnly);

        ModPackage content = Mod("lang.plus", English, new Dictionary<string, string>
        {
            ["text/en/a.toml"] = "",
            ["assets/SYSTEM.AGF"] = "override",
        }, new Dictionary<string, byte[]> { ["fonts/Body.ttf"] = [1] });
        Assert.False(content.PresentationOnly);
        Assert.False(Mod("plain", "").PresentationOnly);
    }

    [Theory]
    [InlineData(2, "text/EN/a.toml", "'EN' is not a lowercase language tag a package can carry text for")]
    [InlineData(2, "text/x-pseudo/a.toml", "'x-pseudo' is not a lowercase language tag")]
    [InlineData(2, "text/en/a.txt", "text/ holds catalogs at text/<language>/*.toml")]
    [InlineData(2, "text/a.toml", "text/ holds catalogs at text/<language>/*.toml")]
    [InlineData(2, "fonts/sub/a.ttf", "fonts/ holds only font files")]
    [InlineData(2, "fonts/readme.txt", "fonts/ holds only font files")]
    [InlineData(1, "text/en/a.toml", "text/ needs omp = 2")]
    [InlineData(1, "fonts/a.ttf", "fonts/ needs omp = 2")]
    public void TextAndFontLayoutProblemsAreRejectedWithTheirReason(int omp, string path, string reason)
    {
        string folder = Folder("lang.bad", "", new Dictionary<string, string> { [path] = "x" }, omp);
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(folder));
        Assert.Contains(error.Problems, problem => problem.Contains(reason));
    }

    [Fact]
    public void ABundledFontALanguageNamesMustBeInThePackage()
    {
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(Folder("lang.en", English)));
        Assert.Contains(error.Problems, problem => problem.Contains("'fonts/Body.ttf' is not in the package"));
    }

    // ------------------------------------------------------------------------------------------- link and cache

    private (ModPackage Base, ModPackage Fix) EnglishStack()
    {
        ModPackage baseLanguage = Mod("a.lang", English, new Dictionary<string, string>
        {
            ["text/en/_global.toml"] = "[[text]]\nsource = \"はい\"\ntext = \"Yes\"\n\n[[text]]\nsource = \"いいえ\"\ntext = \"No\"\n",
        }, new Dictionary<string, byte[]> { ["fonts/Body.ttf"] = [1, 2, 3] });
        ModPackage fix = Mod("b.fix", "[[language]]\ntag = \"en\"\nglyph_delay_scale = 0.75\n", new Dictionary<string, string>
        {
            ["text/en/fix.toml"] = "[[text]]\nsource = \"はい\"\ntext = \"Yeah\"\n\n[[text]]\nsource = \"まだ\"\n",
        });
        return (baseLanguage, fix);
    }

    [Fact]
    public void TheStacksTextMergesInLoadOrderWithTheLaterPackageWinning()
    {
        var (baseLanguage, fix) = EnglishStack();

        ModLinkResult result = ModLinker.Link(SyntheticBase(), [fix, baseLanguage], ["a.lang", "b.fix"]);

        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal(["en/000.a.lang/_global.toml", "en/001.b.fix/fix.toml"], result.Text.Files.Select(file => file.Path));
        Assert.Equal("fonts/a.lang/Body.ttf", Assert.Single(result.Text.Fonts).CachePath);
        LinkedLanguage language = Assert.Single(result.Text.Languages);
        Assert.Equal(["a.lang", "b.fix"], language.DefinedBy);
        Assert.Equal(("English", 0.75), (language.Settings.Name, language.Settings.GlyphDelayScale));
        Assert.Equal(["fonts/a.lang/Body.ttf", "Georgia"], Assert.Single(language.Settings.Fonts!).Use);

        string report = result.Report.Render();
        Assert.Contains("language en: b.fix sets glyph_delay_scale over a.lang", report);
        Assert.Contains("text en: 2 entries from 2 files, 1 without text", report);
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.Override
            && line.Message.StartsWith("text en: 1 entry of a.lang replaced by b.fix's"));
    }

    [Fact]
    public void TheCacheKeepsTheTextFontsAndLanguagesAndLoadsThemAsATextSource()
    {
        var (baseLanguage, fix) = EnglishStack();
        ModLinkBase linkBase = SyntheticBase();
        ModLinkResult result = ModLinker.Link(linkBase, [baseLanguage, fix], ["a.lang", "b.fix"]);
        Assert.True(result.Succeeded, result.Report.Render());
        string cache = Path.Combine(_root, "cache");

        ModLinkCache.Write(cache, "k", linkBase, result);
        MountedModLayer layer = ModLinkCache.TryOpen(cache, "k")!;

        Assert.NotNull(layer);
        Assert.All(layer.Manifest.Order, mod => Assert.True(mod.Presentation));
        TextSource source = layer.TextSource!;
        var report = new TextLoadReport();
        TextLanguage loaded = TextRootLoader.Load([source], "en", TextSiteBinder.For(_ => null, layer), report);
        var presenter = new TextPresenter(loaded);
        Assert.Equal("Yeah", presenter.Present("はい", new TextSite("SC0100", 0x10))?.Text);
        Assert.Equal("No", presenter.Present("いいえ", new TextSite("SC0100", 0x10))?.Text);
        Assert.Null(presenter.Present("まだ", new TextSite("SC0100", 0x10)));
        Assert.Equal((0.75, TextLanguage.DefaultMinFitScale), (loaded.GlyphDelayScale, loaded.MinFitScale));
        string font = Assert.Single(loaded.Fonts).Use[0];
        Assert.True(Path.IsPathFullyQualified(font) && File.Exists(font), font);
        Assert.Equal("mods:en/000.a.lang/_global.toml:1", Assert.Single(report.Overrides).Replaced.Origin);

        // A cache entry that lost a catalog is not used.
        File.Delete(Path.Combine(layer.TextRoot, "en", "001.b.fix", "fix.toml"));
        Assert.Null(ModLinkCache.TryOpen(cache, "k"));
    }

    [Fact]
    public void TextForALanguageNoPackageDefinesStopsTheLink()
    {
        ModPackage mod = Mod("lang.fr", "", new Dictionary<string, string>
        {
            ["text/fr/a.toml"] = "[[text]]\nsource = \"はい\"\ntext = \"Oui\"\n",
        });

        ModLinkResult result = ModLinker.Link(SyntheticBase(), [mod], []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Report.Lines, line => line.Stops && line.Mods.SequenceEqual(["lang.fr"])
            && line.Message.StartsWith("text/fr/ adds entries to language 'fr', which no package in the stack defines"));
    }

    [Fact]
    public void ACatalogThatDoesNotReadStopsTheLinkWithItsFileAndLine()
    {
        ModPackage mod = Mod("lang.en", "[[language]]\ntag = \"en\"\n", new Dictionary<string, string>
        {
            ["text/en/bad.toml"] = "[[text]]\nsource = \"はい\"\ntext = \"Yes\"\ncolour = \"red\"\n",
        });

        ModLinkResult result = ModLinker.Link(SyntheticBase(), [mod], []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Report.Lines, line => line.Stops
            && line.Message == "lang.en/text/en/bad.toml:1: unknown key 'colour'");
    }

    [Fact]
    public void TheLinkNotesEntriesThatMatchNothingAndSitesThatDoNotBind()
    {
        var asm = new TextImportTests.Asm();
        asm.Say("はい");
        asm.Op(0x2);
        byte[] script = asm.Bytes();
        ModPackage mod = Mod("lang.en", "[[language]]\ntag = \"en\"\n", new Dictionary<string, string>
        {
            ["text/en/_global.toml"] = "[[text]]\nsource = \"はい\"\ntext = \"Yes\"\n\n[[text]]\nsource = \"ない\"\ntext = \"None\"\n",
            ["text/en/SC0100.toml"] = $"script = \"SC0100\"\nfingerprint = \"{Installed}\"\n\n"
                                      + "[[text]]\nsource = \"はい\"\nat = 0x0\ntext = \"Yes!\"\n",
        });

        ModLinkResult result = ModLinker.Link(SyntheticBase(("SC0100.BIN", script)), [mod], []);

        Assert.True(result.Succeeded, result.Report.Render());
        string report = result.Report.Render();
        Assert.Contains("lang.en/text/en/_global.toml: 1 entry matches nothing in the installed scripts", report);
        Assert.Contains($"lang.en/text/en/SC0100.toml: 1 site entry does not bind: the file names {Installed}, the base "
                        + $"script is {OmsPatchPlan.FingerprintOf(script)}", report);
    }

    // ------------------------------------------------------------------------------------------- save stamps

    [Fact]
    public void AStampMarksPresentationPackagesAndTheComparisonSaysSo()
    {
        var saved = new ModSaveStamp([new ModStackEntry("lang.en", "1.0.0", "sha256:aa", Presentation: true),
                                      new ModStackEntry("gone.lang", "1.0.0", "sha256:cc", Presentation: true)],
                                     new Dictionary<string, int>(), "key1");
        ModSaveStamp reread = ModSaveStamp.TryParse(saved.ToToml())!;
        Assert.Equal(saved.Mods, reread.Mods);

        var current = new ModSaveStamp([new ModStackEntry("lang.en", "1.1.0", "sha256:ab", Presentation: true),
                                        new ModStackEntry("content", "1.0.0", "sha256:dd")],
                                       new Dictionary<string, int>(), "key2");
        IReadOnlyList<string> differences = ModSaveStamp.Compare(saved, current);
        Assert.Contains("lang.en 1.0.0 -> 1.1.0 (presentation only)", differences);
        Assert.Contains("removed gone.lang 1.0.0 (presentation only)", differences);
        Assert.Contains("added content 1.0.0", differences);
    }

    // ------------------------------------------------------------------------------------------- offset tables

    [Fact]
    public void AnOffsetTableMovesBaseOffsetsRunByRun()
    {
        var map = new ScriptOffsetMap([new OffsetRun(0, 0x10, 0), new OffsetRun(0x14, 0x40, 6), new OffsetRun(0x40, 0x50, -2)]);

        Assert.Equal(0x8, map.Map(0x8));
        Assert.Null(map.Map(0x12));
        Assert.Equal(0x26, map.Map(0x20));
        Assert.Equal(0x3e, map.Map(0x40));
        Assert.Null(map.Map(0x50));
        Assert.Equal(map.Runs, ScriptOffsetMap.FromArrays(map.ToArrays()).Runs);
        Assert.Throws<ArgumentException>(() => new ScriptOffsetMap([new OffsetRun(0x10, 0x20, 0), new OffsetRun(0x18, 0x30, 0)]));
        Assert.Throws<ArgumentException>(() => new ScriptOffsetMap([new OffsetRun(0x10, 0x10, 0)]));
        Assert.Throws<InvalidDataException>(() => ScriptOffsetMap.FromArrays([[1, 2]]));
    }

    // Each base instruction the table maps lands on the same instruction in the linked script; those it does not map
    // are the ones a patch replaced.
    [Fact]
    [Trait("Category", "Workspace")]
    public void APatchedScriptsTableMapsEachKeptInstructionToItself()
    {
        OpcodeTable himegari = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        ModLinkBase linkBase = ModLinkBase.ForInstall("himegari", Paths.Sys4Ini, catalog, store, himegari, Contracts,
            Paths.GlobalsJson("himegari"), Paths.OmsModules("himegari"), Paths.PatchPointsJson("himegari"));
        ModPackage example = ModPackage.Open(Path.Combine(Paths.Repo, "oms", "examples", "mods", "example.sc0000-page3"));
        _opened.Add(example);

        ModLinkResult result = ModLinker.Link(linkBase, [example], []);

        Assert.True(result.Succeeded, result.Report.Render());
        LinkedScript linked = result.Scripts["SC0000.BIN"];
        ScriptOffsetMap map = Assert.IsType<ScriptOffsetMap>(linked.Offsets);
        Ome.Engine.Model.Script before = Sys4Loader.Parse(linkBase.ReadScript("SC0000.BIN")!, himegari, "SC0000.BIN");
        Ome.Engine.Model.Script after = Sys4Loader.Parse(linked.Bytes, himegari, "SC0000.BIN");
        int kept = 0, moved = 0;
        foreach (Instruction instruction in before.Instructions)
        {
            if (map.Map(instruction.Offset) is not { } offset) continue;
            kept++;
            if (offset != instruction.Offset) moved++;
            Instruction there = after.Instructions[after.IndexByOffset[offset]];
            Assert.Equal((instruction.Opcode, instruction.Args.Count), (there.Opcode, there.Args.Count));
        }
        Assert.True(kept > before.Instructions.Count - 20, $"{kept} of {before.Instructions.Count} kept");
        Assert.True(moved > 0);
        Assert.True(map.Runs.Count > 1);
    }

    // ------------------------------------------------------------------------------------------- engine text (milestone 5)

    private const string German = "[[language]]\ntag = \"de\"\nname = \"Deutsch\"\nfallback = [\"en\"]\n";

    /// <summary>A version 2 package without [game] (localization spec §11.1).</summary>
    private string ProfilelessFolder(string id, string tables, IReadOnlyDictionary<string, string>? files = null)
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 2\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n{tables}\n", new UTF8Encoding(false));
        foreach (var (path, text) in files ?? new Dictionary<string, string>())
            Write(Path.Combine(folder, path), new UTF8Encoding(false).GetBytes(text));
        return folder;
    }

    private ModPackage ProfilelessMod(string id, string tables, IReadOnlyDictionary<string, string>? files = null)
    {
        ModPackage mod = ModPackage.Open(ProfilelessFolder(id, tables, files));
        _opened.Add(mod);
        return mod;
    }

    private static readonly Dictionary<string, string> GermanShell = new()
    {
        ["text/de/_engine.toml"] = "[[engine]]\nkey = \"command.skip\"\ntext = \"Ü&berspringen\"\n\n"
                                   + "[[engine]]\nkey = \"exit.title\"\ntext = \"Beenden\"\n\n"
                                   + "[[engine]]\nkey = \"command.auto\"\nnote = \"ja: ｵｰﾄﾓｰﾄﾞ\"\n",
    };

    [Fact]
    public void AVersionTwoManifestMayLeaveOutGameAndThenTargetsEveryProfile()
    {
        ModManifest manifest = ManifestReader.Parse("omp = 2\nid = \"shell.de\"\nname = \"de\"\nversion = \"1.0.0\"\n" + German);

        Assert.True(manifest.TargetsAnyProfile);
        Assert.Null(manifest.Profile);
        Assert.Null(manifest.Target);
        Assert.False(ManifestReader.Parse(ManifestText("lang.en")).TargetsAnyProfile);
        var version1 = Assert.Throws<ModManifestException>(() =>
            ManifestReader.Parse("omp = 1\nid = \"shell.de\"\nname = \"de\"\nversion = \"1.0.0\"\n"));
        Assert.Contains("game: required", version1.Problems);
    }

    [Theory]
    [InlineData("[scripts]\nnames = [\"NEWSCENE\"]", "scripts: a package without [game] holds no scripts or assets")]
    [InlineData("[assets]\nnames = [\"A.PNG\"]", "assets: a package without [game] holds no scripts or assets")]
    [InlineData("[replace]\n\"SC0100.BIN\" = { over = \"x.y\" }", "replace: a package without [game] holds no scripts")]
    public void APackageWithoutGameHasNoGameTables(string tables, string reason)
    {
        var error = Assert.Throws<ModManifestException>(() =>
            ManifestReader.Parse($"omp = 2\nid = \"shell.de\"\nname = \"de\"\nversion = \"1.0.0\"\n{tables}\n"));
        Assert.Contains(error.Problems, problem => problem.StartsWith(reason));
    }

    [Theory]
    [InlineData("patches/SC0100.oms")]
    [InlineData("assets/A.PNG")]
    [InlineData("replace/SC0100.BIN")]
    public void APackageWithoutGameHoldsOnlyTextAndFonts(string path)
    {
        var error = Assert.Throws<ModPackageException>(() =>
            ModPackage.Open(ProfilelessFolder("shell.de", German, new Dictionary<string, string> { [path] = "x" })));
        Assert.Contains($"'{path}': a package without [game] holds only text/ and fonts/", error.Message);
    }

    [Fact]
    public void AnEngineOnlyPackageLinksForAnyProfileAndItsCacheLoadsAsEngineText()
    {
        ModPackage shell = ProfilelessMod("shell.de", German, GermanShell);
        ModLinkBase linkBase = SyntheticBase();

        ModLinkResult result = ModLinker.Link(linkBase, [shell], []);

        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Contains("text de: 0 entries from 1 files, 2 engine keys", result.Report.Render());
        Assert.True(Assert.Single(result.Order).Layout.ChangesNoContent);
        ModLinkCache.Write(Path.Combine(_root, "cache"), "k", linkBase, result);
        MountedModLayer layer = ModLinkCache.TryOpen(Path.Combine(_root, "cache"), "k")!;
        var launch = new LanguageLaunch([layer.TextSource!], TextSiteBinder.For(_ => null, layer), null);
        var report = new TextLoadReport();

        LanguageSelection selection = launch.Load("de", report);

        Assert.Null(selection.Presenter);                          // no game text: drawn as written (spec §10)
        Assert.Equal(["de", "en"], selection.Lookup);
        EngineTextLayer german = Assert.Single(selection.Engine);
        Assert.Equal("de", german.Language);
        Assert.Equal(["command.skip", "exit.title"], german.Entries.Keys.Order(StringComparer.Ordinal));   // no template
        Assert.Equal("Beenden", german.Entries["exit.title"].Text);
        Assert.Contains("engine text de: 2 keys", report.Lines());
        Assert.Equal([("de", "Deutsch")], launch.Languages());
        Assert.True(launch.DefinesLanguage);
    }

    [Fact]
    public void GameTextInAPackageWithoutGameStopsTheLink()
    {
        ModPackage shell = ProfilelessMod("shell.de", German, new Dictionary<string, string>
        {
            ["text/de/a.toml"] = "[[text]]\nsource = \"はい\"\ntext = \"Ja\"\n",
        });

        ModLinkResult result = ModLinker.Link(SyntheticBase(), [shell], []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Report.Lines, line => line.Stops && line.Message.StartsWith(
            "shell.de/text/de/a.toml:1: a package without [game] holds only [[engine]] entries"));
    }

    [Fact]
    public void ExportLeavesAnEngineOnlyPackageOutWithANote()
    {
        string folder = ProfilelessFolder("shell.de", German, GermanShell);
        string output = Path.Combine(_root, "export");
        var console = new StringWriter();

        Assert.True(Ome.Mods.Tools.OmpCommands.Export(SyntheticBase(), null, [folder], output, console) == 0,
                    console.ToString());
        Assert.Empty(Directory.GetFiles(output));
        Assert.Contains("note: text/, fonts/, assets/<language>/ and [[language]] of shell.de are not exported", console.ToString());
    }

    [Fact]
    public void EngineEntriesMergeAlongTheChainWithTheLaterSourceWinning()
    {
        string first = Path.Combine(_root, "first"), second = Path.Combine(_root, "second");
        Write(Path.Combine(first, "languages.toml"), Encoding.UTF8.GetBytes(German));
        Write(Path.Combine(first, "de", "a.toml"), Encoding.UTF8.GetBytes(
            "[[engine]]\nkey = \"exit.title\"\ntext = \"Ende\"\n\n[[text]]\nsource = \"はい\"\ntext = \"Ja\"\n"));
        Write(Path.Combine(first, "en", "a.toml"), Encoding.UTF8.GetBytes("[[engine]]\nkey = \"exit.title\"\ntext = \"Leave\"\n"));
        Write(Path.Combine(second, "de", "b.toml"), Encoding.UTF8.GetBytes("[[engine]]\nkey = \"exit.title\"\ntext = \"Beenden\"\n"));
        Write(Path.Combine(second, "fr", "b.toml"), Encoding.UTF8.GetBytes("[[engine]]\nkey = \"exit.title\"\ntext = \"Quitter\"\n"));
        var launch = new LanguageLaunch([new TextSource(first), new TextSource(second)], new TextSiteBinder(_ => null), null);

        LanguageSelection selection = launch.Load("de", new TextLoadReport());

        Assert.NotNull(selection.Presenter);                       // de has game text
        Assert.Equal(["de", "en"], selection.Engine.Select(layer => layer.Language));
        Assert.Equal("Beenden", selection.Engine[0].Entries["exit.title"].Text);
        Assert.Equal("de/b.toml:1", selection.Engine[0].Entries["exit.title"].Origin);
        Assert.Equal("Leave", selection.Engine[1].Entries["exit.title"].Text);
        Assert.Equal([("de", "Deutsch"), ("en", null), ("fr", null)], launch.Languages());
        Assert.Single(launch.Load("de", new TextLoadReport()).Presenter!.Language.Chain);   // en has engine text only
        Assert.Null(launch.Load("fr", new TextLoadReport()).Presenter);
    }
}
