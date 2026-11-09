using System.Text;
using Age.Engine.Model;
using Age.Engine.Sys4;
using Age.Engine.Vm;
using Age.Mods.Linking;
using Age.Mods.Packages;
using Age.Script.Abi;

/// <summary>Mod loader spec §6.2, §6.4 and §6.5 (plan M5, gate 3): selector allocation, mod append catalogs, the link
/// cache, and mounting the linked layer.</summary>
public sealed class ModSelectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "age-sel-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = new();

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);
    private const string Installed = "sha256:1111111111111111111111111111111111111111111111111111111111111111";

    private static ModLinkBase SyntheticBase(params string[] assets) => new()
    {
        Profile = "himegari",
        CatalogFingerprint = Installed,
        ReadScript = _ => null,
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
        CatalogTitle = "TEST",
        InstallSelectors = new HashSet<int> { 1 },
        HasAsset = name => assets.Contains(name, StringComparer.OrdinalIgnoreCase),
        ToolchainStamp = "test",
    };

    public void Dispose()
    {
        foreach (ModPackage mod in _opened) mod.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Script(string body) => $"oms 1\nscript \"X\"\n\n{string.Join("\n", body.Split('\n').Select(line => "    " + line))}\n";

    private ModPackage Mod(string id, string manifest, IReadOnlyDictionary<string, string> files)
    {
        string folder = Path.Combine(_root, "packages", id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 1\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n{manifest}\n[game]\nprofile = \"himegari\"\ntarget = \"native\"\n");
        foreach (var (path, text) in files)
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        return mod;
    }

    private ModPackage ScriptMod(string id, string name = "AUTO", string body = "exit")
        => Mod(id, $"[scripts]\nnames = [\"{name}\"]\n", new Dictionary<string, string> { [$"scripts/{name}.oms"] = Script(body) });

    // ------------------------------------------------------------------------------------------- the store

    [Fact]
    public void TheStoreGivesTheLowestFreeSelectorAboveTheInstallsAndRoundTrips()
    {
        var store = new SelectorStore();
        var report = new ModLinkReport();
        var first = store.Allocate(["b.mod", "a.mod"], new HashSet<int> { 1, 3 }, report);

        Assert.False(report.Stops);
        Assert.Equal(2, first["b.mod"]);
        Assert.Equal(4, first["a.mod"]);
        SelectorStore reread = SelectorStore.Parse(store.ToToml());
        Assert.Equal(store.Selectors, reread.Selectors);
        Assert.True(reread.Forget("b.mod"));
        Assert.Equal(2, reread.Allocate(["c.mod"], new HashSet<int> { 1, 3 }, report)["c.mod"]);
    }

    [Fact]
    public void ExhaustionAndAnInstallCollisionAreErrorsThatNameTheAllocations()
    {
        var store = new SelectorStore();
        var report = new ModLinkReport();
        store.Allocate(Enumerable.Range(0, 126).Select(k => $"m{k:000}"), new HashSet<int> { 1 }, report);
        Assert.False(report.Stops);

        store.Allocate(["late"], new HashSet<int> { 1 }, report);
        ModReportLine exhausted = Assert.Single(report.Lines);
        Assert.Contains("no append-catalog selector is free", exhausted.Message);
        Assert.Contains("m000=2", exhausted.Message);
        Assert.Contains("--forget-mod", exhausted.Message);

        var collision = new ModLinkReport();
        store.Allocate(["m000"], new HashSet<int> { 1, 2 }, collision);
        Assert.Contains("now used by the install's own catalog APPEND02.AAI", Assert.Single(collision.Lines).Message);
    }

    [Theory]
    [InlineData("[selectors]\n\"a\" = 0\n", "must be an integer 1..127")]
    [InlineData("[selectors]\n\"a\" = 2\n\"b\" = 2\n", "allocated to both a and b")]
    [InlineData("other = 1\n", "only a [selectors] table")]
    public void AMalformedStoreIsRejected(string text, string reason)
        => Assert.Contains(reason, Assert.Throws<InvalidDataException>(() => SelectorStore.Parse(text)).Message);

    // ------------------------------------------------------------------------------------------- gate 3

    [Fact]
    public void Gate3SelectorsSurviveReorderRemovalAndReAdding()
    {
        ModLinkBase linkBase = SyntheticBase();
        ModPackage x = ScriptMod("example.x"), y = ScriptMod("example.y");
        var store = new SelectorStore();

        ModLinkResult first = ModLinker.Link(linkBase, [x, y], ["example.y", "example.x"], store);
        Assert.True(first.Succeeded, first.Report.Render());
        Assert.Equal(2, Selector(first, "example.y"));
        Assert.Equal(3, Selector(first, "example.x"));
        Assert.Empty(store.Selectors);  // the caller saves the allocation; the link does not change its input
        store = first.Selectors;

        ModLinkResult reordered = ModLinker.Link(linkBase, [x, y], ["example.x", "example.y"], store);
        Assert.Equal(["example.x", "example.y"], reordered.Report.Order);
        Assert.Equal(2, Selector(reordered, "example.y"));
        Assert.Equal(3, Selector(reordered, "example.x"));

        ModLinkResult removed = ModLinker.Link(linkBase, [x], [], reordered.Selectors);
        Assert.Equal(3, Selector(removed, "example.x"));
        Assert.Equal(2, removed.Selectors.Selectors["example.y"]);  // kept for the removed mod

        ModPackage z = ScriptMod("example.z");
        ModLinkResult added = ModLinker.Link(linkBase, [z, x], [], removed.Selectors);
        Assert.Equal(4, Selector(added, "example.z"));  // 2 still belongs to example.y

        ModLinkResult readded = ModLinker.Link(linkBase, [y, z, x], [], added.Selectors);
        Assert.Equal(2, Selector(readded, "example.y"));
        Assert.Equal(3, Selector(readded, "example.x"));
        Assert.Equal(4, Selector(readded, "example.z"));
    }

    private static int Selector(ModLinkResult result, string id) => result.Catalogs.Single(catalog => catalog.ModId == id).Selector;

    private (ModPackage Library, ModPackage App) CrossModStack()
    {
        ModPackage library = Mod("example.lib", "[scripts]\nnames = [\"AUTO\", \"HELLO\"]\n", new Dictionary<string, string>
        {
            ["scripts/AUTO.oms"] = Script("G[0x100] = 7\nexit"),
            ["scripts/HELLO.oms"] = Script("G[0x102] = G[0x101] + 1\nexit"),
        });
        ModPackage app = Mod("example.app", "[[requires]]\nid = \"example.lib\"\n[scripts]\nnames = [\"MAIN\"]\n",
            new Dictionary<string, string>
            {
                ["scripts/MAIN.oms"] = Script("G[0x101] = G[0x100] * 10\nrun \"example.lib:HELLO\"\nexit"),
            });
        return (library, app);
    }

    [Fact]
    public void Gate3ARunAcrossADeclaredDependencyResolvesToTheOtherModsRecord()
    {
        var (library, app) = CrossModStack();
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [app, library], []);

        Assert.True(result.Succeeded, result.Report.Render());
        LinkedCatalog lib = result.Catalogs.Single(catalog => catalog.ModId == "example.lib");
        LinkedCatalog main = result.Catalogs.Single(catalog => catalog.ModId == "example.app");
        Assert.Equal(2, lib.Selector);
        Assert.Equal(["$2$AUTO.BIN", "$2$HELLO.BIN"], lib.Records.Select(record => record.CatalogName));
        Assert.Equal("$3$MAIN.BIN", Assert.Single(main.Records).CatalogName);
        Script parsed = Sys4Loader.Parse(main.Records[0].Bytes, Table, "$3$MAIN.BIN", main.PackedId(0));
        Assert.Contains(parsed.Instructions, instruction => instruction.Args.Any(operand => operand.Value == lib.PackedId(1)));
    }

    [Fact]
    public void ARunIntoAModOutsideTheScopeIsACompileError()
    {
        ModPackage library = ScriptMod("example.lib", "HELLO");
        ModPackage app = ScriptMod("example.app", "MAIN", "run \"example.lib:HELLO\"\nexit");
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [app, library], []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Report.Lines, line => line.Stops && line.Mods.SequenceEqual(["example.app"])
                                                     && line.Message.Contains("[[requires]]"));
    }

    [Fact]
    public void ARecordZeroThatWaitsForThePlayerLinksWithAWarning()
    {
        ModPackage scene = Mod("example.scene", "[scripts]\nnames = [\"SCENE\", \"AUTO\"]\n", new Dictionary<string, string>
        {
            ["scripts/SCENE.oms"] = Script("show_text(0x0, \"hello\")\nwait_for_input(0x0)\nexit"),
            ["scripts/AUTO.oms"] = Script("wait_for_input(0x0)\nexit"),
        });
        ModPackage boot = Mod("example.boot", "[scripts]\nnames = [\"AUTO\", \"SCENE\"]\n", new Dictionary<string, string>
        {
            ["scripts/AUTO.oms"] = Script("G[0x100] = 7\nexit"),
            ["scripts/SCENE.oms"] = Script("show_text(0x0, \"hello\")\nwait_for_input(0x0)\nexit"),
        });

        ModLinkResult result = ModLinker.Link(SyntheticBase(), [scene, boot], []);

        Assert.True(result.Succeeded, result.Report.Render());
        ModReportLine warning = Assert.Single(result.Report.Lines, line => line.Message.Contains("record 0"));
        Assert.Equal(ModReportClass.Note, warning.Class);
        Assert.Equal(["example.scene"], warning.Mods);
        Assert.Equal($"${Selector(result, "example.scene")}$SCENE.BIN", warning.Script);
        Assert.Contains("uses show-text, wait-for-input;", warning.Message);
    }

    [Fact]
    public void AnAssetOnlyModGetsAStubRecordZeroAndAssetsAreClassifiedAgainstTheBase()
    {
        ModPackage assets = Mod("example.art", "[assets]\nnames = [\"NEW.AGF\"]\n", new Dictionary<string, string>
        {
            ["assets/NEW.AGF"] = "new",
            ["assets/BG001.AGF"] = "override",
        });
        ModPackage later = Mod("example.later", "", new Dictionary<string, string> { ["assets/BG001.AGF"] = "later" });
        ModLinkResult result = ModLinker.Link(SyntheticBase("BG001.AGF"), [later, assets], ["example.art", "example.later"]);

        Assert.True(result.Succeeded, result.Report.Render());
        LinkedCatalog catalog = Assert.Single(result.Catalogs);
        Assert.Equal(["$2$OMPSTUB.BIN", "$2$NEW.AGF"], catalog.Records.Select(record => record.CatalogName));
        Assert.True(catalog.Records[0].Stub);
        Assert.Equal("later", Encoding.ASCII.GetString(result.Assets["BG001.AGF"].Bytes));
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.Override && line.Script == "BG001.AGF");

        ModPackage stray = Mod("example.stray", "", new Dictionary<string, string> { ["assets/NOPE.AGF"] = "x" });
        ModPackage wrong = Mod("example.wrong", "[assets]\nnames = [\"BG001.AGF\"]\n",
                               new Dictionary<string, string> { ["assets/BG001.AGF"] = "x" });
        ModLinkReport report = ModLinker.Link(SyntheticBase("BG001.AGF"), [stray, wrong], []).Report;
        Assert.Contains(report.Lines, line => line.Mods.SequenceEqual(["example.stray"]) && line.Message.Contains("[assets].names"));
        Assert.Contains(report.Lines, line => line.Mods.SequenceEqual(["example.wrong"]) && line.Message.Contains("override"));
    }

    // ------------------------------------------------------------------------------------------- cache and mount

    [Fact]
    public void PrepareWritesTheCacheAndSelectorsOnlyOnSuccessAndHitsTheCacheNextTime()
    {
        var (library, app) = CrossModStack();
        string mods = Path.Combine(_root, "mods");
        ModLinkBase linkBase = SyntheticBase();

        ModStackResult first = ModStack.Prepare(linkBase, [app, library], [], mods);
        Assert.True(first.Succeeded, first.Report.Render());
        Assert.False(first.FromCache);
        Assert.Contains("\"example.lib\" = 2", File.ReadAllText(Path.Combine(mods, SelectorStore.FileName)));
        Assert.True(File.Exists(Path.Combine(first.Layer!.CatalogDirectory, "APPEND02.AAI")));
        Assert.Equal("record of example.app's append catalog", first.Layer.Explain("$3$MAIN.BIN"));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(mods, ModLinkCache.FolderName), "*.tmp-*"));

        ModStackResult second = ModStack.Prepare(linkBase, [library, app], [], mods);
        Assert.True(second.FromCache);
        Assert.Equal(first.Layer.Key, second.Layer!.Key);

        // A changed toolchain is a different key.
        ModStackResult rebuilt = ModStack.Prepare(linkBase with { ToolchainStamp = "other" }, [library, app], [], mods);
        Assert.False(rebuilt.FromCache);
        Assert.NotEqual(first.Layer.Key, rebuilt.Layer!.Key);

        // A failing stack writes neither a cache entry nor an allocation.
        ModPackage broken = ScriptMod("example.broken", "MAIN", "this is not oms");
        string fresh = Path.Combine(_root, "mods-fail");
        ModStackResult failed = ModStack.Prepare(linkBase, [broken], [], fresh);
        Assert.False(failed.Succeeded);
        Assert.False(File.Exists(Path.Combine(fresh, SelectorStore.FileName)));
        Assert.False(Directory.Exists(Path.Combine(fresh, ModLinkCache.FolderName))
                     && Directory.EnumerateFileSystemEntries(Path.Combine(fresh, ModLinkCache.FolderName)).Any());
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Gate3TheMountedLayerRunsBothRecordZerosAtInit2InSelectorOrder()
    {
        var (library, app) = CrossModStack();
        string mods = Path.Combine(_root, "mods");
        var installed = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        ModLinkBase linkBase = SyntheticBase() with
        {
            CatalogTitle = installed.Title,
            AppendMagic = ("S4AC" + installed.Magic[4..]).PadRight(8)[..8],
        };
        ModStackResult prepared = ModStack.Prepare(linkBase, [app, library], [], mods);
        Assert.True(prepared.Succeeded, prepared.Report.Render());

        // The base catalog alone (a copy without the install's APPEND01, whose record 0 needs a full host), with the
        // layer's catalogs mounted after it from their own directory.
        string game = Path.Combine(_root, "game");
        Directory.CreateDirectory(game);
        File.Copy(Paths.Sys4Ini, Path.Combine(game, "SYS4INI.BIN"));
        var catalog = Sys4AssetCatalog.Load(Path.Combine(game, "SYS4INI.BIN"), [prepared.Layer!.CatalogDirectory]);
        Assert.Equal([2, 3], catalog.AppendPacks.Keys.Order());
        var store = new Sys4AssetStore(catalog, game, prepared.Layer.LooseRoot, game);
        Assert.Equal(Path.Combine(prepared.Layer.CatalogDirectory, "APPEND03.ALF"),
                     store.Locate(catalog.ResolvePacked(0x03000000)!).Path);
        var provider = new Sys4ScriptProvider(Table, catalog, store);

        var root = ScriptAssembler.Assemble(Table, "ROOT", new List<(int, Operand[])>
        {
            (0x143, Array.Empty<Operand>()),
            (0x2, Array.Empty<Operand>()),
        }, Array.Empty<string>());
        var vm = new VirtualMachine(root, Table, new RecordingHost(), provider: provider);
        vm.Run();

        // example.lib's AUTO (selector 2) ran before example.app's MAIN (selector 3), which called HELLO across mods.
        Assert.Equal(7, vm.Globals.GetValueOrDefault(0x100));
        Assert.Equal(70, vm.Globals.GetValueOrDefault(0x101));
        Assert.Equal(71, vm.Globals.GetValueOrDefault(0x102));
        Assert.Equal("exit", vm.HaltReason);

        // A second mounted directory reusing a selector is an error, not the native silent replace.
        Assert.Throws<InvalidDataException>(() => Sys4AssetCatalog.Load(Path.Combine(game, "SYS4INI.BIN"),
            [prepared.Layer.CatalogDirectory, prepared.Layer.CatalogDirectory]));
    }
}
