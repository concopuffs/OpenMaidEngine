using System.Text;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Script.Abi;

/// <summary>Language assets (localization spec §12, gate 8; milestone 6): <c>assets/&lt;language&gt;/</c> in the
/// layout, the link and its report, the link cache, and the store that serves them for the selected chain. Packages
/// are synthesized in a temporary folder per test.</summary>
public sealed class LanguageAssetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-lasset-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = new();
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);
    private static readonly string[] BaseAssets = ["SO004.AGF", "TITLE.AGF", "VOICE01.OGG", "SC0100.BIN"];

    public void Dispose()
    {
        foreach (ModPackage mod in _opened) mod.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string English = "[[language]]\ntag = \"en\"\nname = \"English\"\n";
    private const string German = "[[language]]\ntag = \"de\"\nfallback = [\"en\"]\n";

    private string Folder(string id, string tables, IReadOnlyDictionary<string, byte[]> files, int omp = 2)
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = {omp}\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n\n[game]\nprofile = \"himegari\"\n"
            + $"target = \"native\"\n{tables}\n", new UTF8Encoding(false));
        foreach (var (path, bytes) in files)
        {
            string full = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        return folder;
    }

    private ModPackage Mod(string id, string tables, IReadOnlyDictionary<string, byte[]> files)
    {
        ModPackage mod = ModPackage.Open(Folder(id, tables, files));
        _opened.Add(mod);
        return mod;
    }

    private static ModLinkBase Base() => new()
    {
        Profile = "himegari",
        CatalogFingerprint = "sha256:" + new string('1', 64),
        ReadScript = _ => null,
        HasAsset = name => BaseAssets.Contains(name, StringComparer.OrdinalIgnoreCase),
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
    };

    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Png(byte red) => PngWriter.Write(new RgbaImage(2, 1, [red, 0, 0, 255, red, 0, 0, 128]));

    private ModLinkResult Link(params ModPackage[] order)
        => ModLinker.Link(Base(), order, order.Select(mod => mod.Id).ToList());

    /// <summary>The file the table serves for <paramref name="name"/> under <paramref name="chain"/>, as the link has
    /// it: the mod and the language of the file, or null for the store below.</summary>
    private static string? Served(ModLinkResult result, string name, params string[] chain)
    {
        if (!result.LanguageAssets.TryGetValue(name, out var layers)) return null;
        var table = new LanguageAssetTable(new Dictionary<string, IReadOnlyList<LanguageAssetLayer>>
        {
            [name] = layers.Select(layer => new LanguageAssetLayer(layer.ModId, layer.Plain,
                layer.Files.ToDictionary(file => file.Key, file => $"{layer.ModId}:{file.Key}"))).ToList(),
        });
        return table.Resolve(name, chain);
    }

    // ------------------------------------------------------------------------------------------------------ layout

    [Fact]
    public void LanguageFoldersUnderAssetsAreSortedIntoTheLayoutAndAPngStandsForItsAgf()
    {
        ModPackage mod = Mod("lang.en", English, new Dictionary<string, byte[]>
        {
            ["assets/en/SO004.AGF"] = Bytes("agf"),
            ["assets/en/title.png"] = Png(1),
            ["assets/de/SO004.AGF"] = Bytes("agf"),
        });

        Assert.Equal([("de", "SO004.AGF", false), ("en", "SO004.AGF", false), ("en", "title.AGF", true)],
                     mod.Layout.LanguageAssets.Select(file => (file.Language, file.Name, file.Png)));
        Assert.Empty(mod.Layout.Assets);
        Assert.True(mod.Layout.ChangesNoContent);
        Assert.True(mod.PresentationOnly);
    }

    [Theory]
    [InlineData(2, "assets/TITLE.png", "a PNG stands for an image only in assets/<language>/")]
    [InlineData(1, "assets/en/TITLE.AGF", "language assets (assets/<language>/NAME) need omp = 2")]
    [InlineData(2, "assets/EN/TITLE.AGF", "'EN' is not a lowercase language tag a package can carry assets for")]
    [InlineData(2, "assets/ja/TITLE.AGF", "'ja' is not a lowercase language tag")]
    [InlineData(2, "assets/en/sub/TITLE.AGF", "assets/<language>/ by basename under one language folder")]
    public void LanguageAssetLayoutProblemsAreRejectedWithTheirReason(int omp, string path, string reason)
    {
        string folder = Folder("lang.bad", "", new Dictionary<string, byte[]> { [path] = Bytes("x") }, omp);
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(folder));
        Assert.Contains(error.Problems, problem => problem.Contains(reason));
    }

    [Fact]
    public void APngAndAnAgfForOneNameInOneLanguageClash()
    {
        string folder = Folder("lang.en", English, new Dictionary<string, byte[]>
        {
            ["assets/en/TITLE.png"] = Png(1),
            ["assets/en/TITLE.AGF"] = Bytes("agf"),
        });
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(folder));
        Assert.Contains(error.Problems, problem => problem.Contains("both stand for TITLE.AGF in en"));
    }

    // -------------------------------------------------------------------------------------------------------- link

    [Fact]
    public void ALanguageAssetMustNameABaseAssetOrOneOfItsPackagesForADefinedLanguage()
    {
        ModPackage mod = Mod("lang.en", English + "\n[assets]\nnames = [\"ICON.AGF\"]\n", new Dictionary<string, byte[]>
        {
            ["assets/ICON.AGF"] = Bytes("icon"),
            ["assets/en/ICON.AGF"] = Bytes("icon en"),
            ["assets/en/NOSUCH.AGF"] = Bytes("x"),
            ["assets/en/SC0100.BIN"] = Bytes("x"),
            ["assets/en/SO004.png"] = Bytes("not a png"),
            ["assets/fr/TITLE.AGF"] = Bytes("x"),
        });

        ModLinkResult result = Link(mod);

        Assert.False(result.Succeeded);
        string report = result.Report.Render();
        Assert.Contains("assets/en/NOSUCH.AGF: neither the base nor this package's [assets].names has an asset NOSUCH.AGF", report);
        Assert.Contains("assets/en/SC0100.BIN: a script has no language version", report);
        Assert.Contains("lang.en/assets/en/SO004.png: expected a PNG image", report);
        Assert.Contains("assets/fr/ holds language assets for 'fr', which no package in the stack defines", report);
        // Its own new asset is the record $selector$ICON.AGF, which the package's plain file supplies.
        var (name, layers) = Assert.Single(result.LanguageAssets);
        Assert.Matches(@"^\$\d+\$ICON\.AGF$", name);
        LinkedLanguageAssetLayer layer = Assert.Single(layers);
        Assert.True(layer.Plain);
        Assert.Equal("icon en", Encoding.ASCII.GetString(layer.Files["en"].Bytes));
    }

    [Fact]
    public void ALanguageAssetOverridesItsPackagesPlainOneOnlyWhileItsLanguageOrOneFallingBackToItIsSelected()
    {
        ModPackage mod = Mod("lang.en", English + German, new Dictionary<string, byte[]>
        {
            ["assets/SO004.AGF"] = Bytes("plain"),
            ["assets/en/SO004.AGF"] = Bytes("en"),
        });

        ModLinkResult result = Link(mod);

        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal("lang.en:en", Served(result, "SO004.AGF", "en"));
        Assert.Equal("lang.en:en", Served(result, "SO004.AGF", "de", "en"));   // de falls back to en
        Assert.Null(Served(result, "SO004.AGF", "fr"));                        // the plain asset, from the store below
        Assert.Null(Served(result, "SO004.AGF"));
        Assert.Equal("plain", Encoding.ASCII.GetString(result.Assets["SO004.AGF"].Bytes));
    }

    [Fact]
    public void ALaterPackagesPlainAssetWinsOverAnEarlierPackagesLanguageAssetAndTheReportListsIt()
    {
        ModPackage translation = Mod("a.translation", English, new Dictionary<string, byte[]>
        {
            ["assets/en/SO004.AGF"] = Bytes("en"),
            ["assets/en/TITLE.png"] = Png(9),
        });
        ModPackage content = Mod("b.content", "", new Dictionary<string, byte[]> { ["assets/SO004.AGF"] = Bytes("content") });

        ModLinkResult result = Link(translation, content);

        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Null(Served(result, "SO004.AGF", "en"));                        // b.content's plain asset
        Assert.Equal("a.translation:en", Served(result, "TITLE.AGF", "en"));
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.Override
            && line.Message == "b.content's asset hides a.translation's en asset (later in load order); a en version of "
                               + "b.content's asset ships in b.content or in a package that loads after it");

        // Loaded after the content mod, the translation's asset wins again, and the report says so.
        ModLinkResult after = Link(content, translation);
        Assert.Equal("a.translation:en", Served(after, "SO004.AGF", "en"));
        Assert.Contains(after.Report.Lines, line => line.Class == ModReportClass.Override
            && line.Message == "a.translation's en asset overrides b.content's asset while en is selected");
    }

    [Fact]
    public void LoadOrderDecidesBetweenLanguageAssetsAndAnotherLanguagesAssetLeavesTheEarlierOne()
    {
        ModPackage first = Mod("a.first", English, new Dictionary<string, byte[]> { ["assets/en/SO004.AGF"] = Bytes("1") });
        ModPackage second = Mod("b.second", "", new Dictionary<string, byte[]> { ["assets/en/SO004.AGF"] = Bytes("2") });
        ModPackage german = Mod("c.german", German, new Dictionary<string, byte[]> { ["assets/de/SO004.AGF"] = Bytes("3") });

        ModLinkResult result = Link(first, second, german);

        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal("b.second:en", Served(result, "SO004.AGF", "en"));
        Assert.Equal("c.german:de", Served(result, "SO004.AGF", "de", "en"));
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.Override
            && line.Message == "b.second's en asset overrides a.first's (later in load order)");
    }

    // -------------------------------------------------------------------------------------------- cache and store

    private sealed class Below : IAssetStore
    {
        public List<string> Reads { get; } = [];
        public Stream Open(AssetEntry entry) => new MemoryStream(ReadAll(entry));
        public byte[] ReadAll(AssetEntry entry)
        {
            Reads.Add(entry.Name);
            return Bytes("below " + entry.Name);
        }
    }

    [Fact]
    public void TheCacheKeepsTheLanguageAssetsAndTheStoreServesThemForTheSelectedChain()
    {
        ModPackage mod = Mod("lang.en", English + German, new Dictionary<string, byte[]>
        {
            ["assets/en/TITLE.png"] = Png(200),
            ["assets/de/VOICE01.OGG"] = Bytes("Stimme"),
        });
        ModLinkBase linkBase = Base();
        ModLinkResult result = ModLinker.Link(linkBase, [mod], ["lang.en"]);
        Assert.True(result.Succeeded, result.Report.Render());
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "k", linkBase, result);
        MountedModLayer layer = ModLinkCache.TryOpen(cache, "k")!;
        Assert.True(layer.Manifest.Order.Single().Presentation);
        Assert.Equal(["de", "en"], layer.LanguageAssets.Languages);
        Assert.Contains("language assets: en from lang.en", layer.Explain("TITLE.AGF"));

        var below = new Below();
        var store = new LanguageAssetStore(below, layer.LanguageAssets);
        var served = new List<string>();
        store.Served += (entry, path) => served.Add($"{entry.Name} {Path.GetFileName(path)}");
        var title = new AssetEntry("TITLE.AGF", "DATA.ALF", 0, 1);
        var voice = new AssetEntry("voice01.ogg", "DATA.ALF", 0, 1);

        // No language: everything comes from below.
        Assert.Equal((false, "below TITLE.AGF"), (store.ReadImage(title).Png, Encoding.ASCII.GetString(store.ReadImage(title).Bytes)));

        store.Select(["en"]);
        (byte[] bytes, bool png) = store.ReadImage(title);
        Assert.True(png);
        Assert.Equal(new byte[] { 200, 0, 0, 255, 200, 0, 0, 128 }, PngDecoder.Decode(bytes).Pixels);
        Assert.Equal("below voice01.ogg", Encoding.ASCII.GetString(store.ReadAll(voice)));

        // A switch takes effect at the next read; a dub ships the same way as an image.
        store.Select(["de", "en"]);
        Assert.Equal("Stimme", Encoding.ASCII.GetString(store.ReadAll(voice)));
        Assert.True(store.ReadImage(title).Png);
        using (var reader = new StreamReader(store.Open(voice))) Assert.Equal("Stimme", reader.ReadToEnd());
        // Each read a language file serves is reported; reads from below are not.
        Assert.Equal(["TITLE.AGF TITLE.png", "voice01.ogg VOICE01.OGG", "TITLE.AGF TITLE.png", "voice01.ogg VOICE01.OGG"],
                     served);

        // A store that tracks catalog opens passes the PNG mark through.
        var opened = new List<string>();
        var tracking = new CatalogTrackingAssetStore(store, entry => opened.Add(entry.Name));
        Assert.True(tracking.ReadImage(title).Png);
        Assert.Equal(["TITLE.AGF"], opened);

        // A cache entry that lost a language asset is not used.
        File.Delete(layer.LanguageAssets.Resolve("TITLE.AGF", ["en"])!);
        Assert.Null(ModLinkCache.TryOpen(cache, "k"));
    }
}
