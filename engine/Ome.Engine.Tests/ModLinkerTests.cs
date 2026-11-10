using System.Text;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Script.Abi;
using Ome.Script.Patch;

/// <summary>Mod loader spec §6 and §7. Stack resolution and replacements use synthesized packages; gate 2 links the
/// example mods in <c>oms/examples/mods/</c> against the installed SC0000.</summary>
public sealed class ModLinkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-link-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = new();

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);
    private static readonly OpcodeTable HimegariTable = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
    private const string Installed = "sha256:1111111111111111111111111111111111111111111111111111111111111111";

    private static ModLinkBase SyntheticBase(params string[] scripts) => new()
    {
        Profile = "himegari",
        CatalogFingerprint = Installed,
        ReadScript = name => scripts.Contains(name, StringComparer.OrdinalIgnoreCase) ? Encoding.ASCII.GetBytes("base " + name) : null,
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
    };

    public void Dispose()
    {
        foreach (ModPackage mod in _opened) mod.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ModPackage Mod(string id, string extra = "", string version = "1.0.0",
                           IReadOnlyDictionary<string, string>? files = null, string profile = "himegari")
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), $"""
            omp = 1
            id = "{id}"
            name = "{id}"
            version = "{version}"
            {extra}

            [game]
            profile = "{profile}"
            target = "native"
            """.Replace("\n\n[game]", "\n[game]"));
        foreach (var (path, text) in files ?? new Dictionary<string, string>())
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        return mod;
    }

    /// <summary>Top-level manifest tables must follow the [game] table, so they go in a separate argument.</summary>
    private ModPackage ModWithTables(string id, string tables, IReadOnlyDictionary<string, string>? files = null,
                                     string version = "1.0.0", string revisions = "")
    {
        var all = new Dictionary<string, string>(files ?? new Dictionary<string, string>());
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 1\nid = \"{id}\"\nname = \"{id}\"\nversion = \"{version}\"\n\n[game]\nprofile = \"himegari\"\n"
            + $"target = \"native\"\n{revisions}\n{tables}\n");
        foreach (var (path, text) in all)
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        return mod;
    }

    private static List<string> Order(ModLinkResult result) => result.Order.Select(mod => mod.Id).ToList();

    // ------------------------------------------------------------------------------------------- stack resolution

    [Fact]
    public void LoadOrderPutsDependenciesFirstThenHintsThenTheUserOrderThenIds()
    {
        ModPackage library = ModWithTables("lib", "");
        ModPackage user = ModWithTables("app", "[[requires]]\nid = \"lib\"\nversion = \">=1\"");
        ModPackage late = ModWithTables("late", "[order]\nafter = [\"zeta\"]");
        ModPackage zeta = ModWithTables("zeta", "");
        ModPackage alpha = ModWithTables("alpha", "[order]\nbefore = [\"lib\"]");

        ModLinkResult result = ModLinker.Link(SyntheticBase(), [user, late, zeta, library, alpha], ["app", "late", "zeta"]);

        Assert.True(result.Succeeded, result.Report.Render());
        // app is listed first by the user, but lib must precede it and alpha must precede lib.
        Assert.Equal(["zeta", "late", "alpha", "lib", "app"], Order(result));
        Assert.Equal(Order(result), result.Report.Order);
        Assert.StartsWith("load order: zeta -> late -> alpha -> lib -> app", result.Report.Render());
    }

    [Fact]
    public void ACycleIsAnErrorThatListsIt()
    {
        ModPackage a = ModWithTables("a", "[order]\nafter = [\"c\"]");
        ModPackage b = ModWithTables("b", "[[requires]]\nid = \"a\"");
        ModPackage c = ModWithTables("c", "[order]\nafter = [\"b\"]");
        ModPackage free = ModWithTables("free", "");

        ModLinkResult result = ModLinker.Link(SyntheticBase(), [a, b, c, free], []);

        Assert.False(result.Succeeded);
        ModReportLine cycle = Assert.Single(result.Report.Lines, line => line.Message.StartsWith("load-order cycle"));
        Assert.Contains("a -> b -> c -> a", cycle.Message);
        Assert.Equal(["a", "b", "c"], cycle.Mods.Order());
        Assert.Equal("free", Order(result)[0]);
    }

    [Theory]
    [InlineData("[[requires]]\nid = \"missing\"", "requires missing, which is not installed or enabled")]
    [InlineData("[[requires]]\nid = \"lib\"\nversion = \">=2\"", "requires lib >=2, but 1.0.0 is installed")]
    [InlineData("[[conflicts]]\nid = \"lib\"\nreason = \"same award\"", "declares a conflict with lib: same award")]
    public void DependencyAndConflictProblemsStopTheLink(string tables, string reason)
    {
        ModPackage library = ModWithTables("lib", "");
        ModPackage mod = ModWithTables("app", tables);
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [library, mod], []);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Report.Lines, line => line.Stops && line.Message.Contains(reason) && line.Mods.Contains("app"));
    }

    [Fact]
    public void ProfileRevisionAndDuplicateIdProblemsStopTheLink()
    {
        ModPackage other = Mod("other", profile: "kamidori");
        ModPackage revision = ModWithTables("rev", "", revisions:
            "revisions = [\"sha256:2222222222222222222222222222222222222222222222222222222222222222\"]");
        ModPackage first = ModWithTables("dup", "");
        ModPackage second = ModWithTables("dup", "", version: "2.0.0");

        ModLinkReport report = ModLinker.Link(SyntheticBase(), [other, revision, first, second], []).Report;

        Assert.Contains(report.Lines, line => line.Message == "targets profile kamidori, but the game is himegari");
        Assert.Contains(report.Lines, line => line.Mods.SequenceEqual(["rev"]) && line.Message.EndsWith("the installed catalog is " + Installed));
        Assert.Contains(report.Lines, line => line.Message.StartsWith("two packages share the id dup"));
    }

    // ------------------------------------------------------------------------------------------- replacements

    [Fact]
    public void TwoReplacementsConflictUnlessTheLaterNamesTheEarlier()
    {
        var file = new Dictionary<string, string> { ["replace/T.BIN"] = "first" };
        ModPackage first = ModWithTables("first", "", file);
        ModPackage second = ModWithTables("second", "", new Dictionary<string, string> { ["replace/t.bin"] = "second" });

        ModLinkResult conflict = ModLinker.Link(SyntheticBase("T.BIN"), [first, second], ["first", "second"]);
        ModReportLine line = Assert.Single(conflict.Report.Lines, line => line.Class == ModReportClass.HardConflict);
        Assert.Equal(["first", "second"], line.Mods);
        Assert.Contains("over = \"first\"", line.Message);

        ModPackage ordered = ModWithTables("second", "[replace]\n\"T.BIN\" = { over = \"first\" }",
                                           new Dictionary<string, string> { ["replace/T.BIN"] = "second" });
        // The `over` entry orders `first` before `second`, whatever the user order says.
        ModLinkResult linked = ModLinker.Link(SyntheticBase("T.BIN"), [ordered, first], ["second", "first"]);
        Assert.True(linked.Succeeded, linked.Report.Render());
        Assert.Equal(["first", "second"], Order(linked));
        LinkedScript script = linked.Scripts["T.BIN"];
        Assert.Equal("second", Encoding.ASCII.GetString(script.Bytes));
        Assert.Equal("second", script.ReplacedBy);
        Assert.Contains(linked.Report.Lines, l => l.Class == ModReportClass.Override && l.Script == "T.BIN");
    }

    [Fact]
    public void AReplacementMustNameABaseScript()
    {
        ModPackage mod = ModWithTables("new", "", new Dictionary<string, string> { ["replace/NEW.BIN"] = "x" });
        ModLinkResult result = ModLinker.Link(SyntheticBase("T.BIN"), [mod], []);
        Assert.Contains(result.Report.Lines, line => line.Stops && line.Message.Contains("new scripts go in scripts/"));
    }

    [Fact]
    public void SkipFailedModsDropsTheFailingModAndLinksTheRest()
    {
        ModPackage good = ModWithTables("good", "", new Dictionary<string, string> { ["replace/T.BIN"] = "good" });
        ModPackage bad = ModWithTables("bad", "[[requires]]\nid = \"missing\"");

        ModLinkResult strict = ModLinker.Link(SyntheticBase("T.BIN"), [good, bad], []);
        Assert.False(strict.Succeeded);

        ModLinkResult lenient = ModLinker.Link(SyntheticBase("T.BIN"), [good, bad], [], skipFailedMods: true);
        Assert.True(lenient.Succeeded, lenient.Report.Render());
        Assert.Equal(["bad"], lenient.Report.Dropped);
        Assert.Equal(["good"], Order(lenient));
        Assert.Contains("dropped: bad", lenient.Report.Render());
    }

    // A stored plan the reader refuses is an error against its mod, not an exception out of the link or the cache
    // key, which leaves the plan's target out.
    [Theory]
    [InlineData("""{"oms_patch": 1, "script": "T.BIN", "fingerprint": "sha256:00", "abi": "SYS4422", "operations": [null]}""")]
    [InlineData("""{"oms_patch": 1, "script": "T.BIN", "fingerprint": "sha256:00", "abi": "SYS4422", "blocks": [[{"op": 2}]]}""")]
    [InlineData("""{"oms_patch": 1, "script": null, "fingerprint": "sha256:00", "abi": "SYS4422"}""")]
    [InlineData("""{"oms_patch": 1, "script": "T.BIN", "fingerprint": "sha256:00", "abi": "SYS4422", "blocks": [[{"op": 2, "args": [{"lit": 1}]}]]}""")]
    public void AStoredPlanThatCannotBeReadIsAnErrorAgainstItsMod(string plan)
    {
        ModPackage mod = ModWithTables("bad.plan", "", new Dictionary<string, string> { ["patches/fix.omspatch.json"] = plan });

        Assert.Empty(ModLinkCache.TouchedScripts(mod));
        ModLinkResult result = ModLinker.Link(SyntheticBase("T.BIN"), [mod], []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Report.Lines, line => line.Stops && line.Mods.SequenceEqual(["bad.plan"])
                                                     && line.Message.StartsWith("patches/fix.omspatch.json: not a readable transform plan"));
        Assert.Equal(["bad.plan"], result.Report.FailedMods);
    }

    // ------------------------------------------------------------------------------------------- toolchain stamp

    private static OpcodeTable StampTable(string label = "mov", int argc = 2, string? semanticName = null)
        => new(new Dictionary<int, OpcodeDefinition>
        {
            [0x2] = new(0x2, "exit", 0, new HashSet<string>()),
            [0x55] = new(0x55, label, argc, new HashSet<string>(), semanticName),
        }, "TEST-ABI");

    private static OpcodeContracts StampContracts(int jumpTarget = 1, string? semanticName = null)
        => OpcodeContracts.FromParts(new Dictionary<int, int[]> { [0x8c] = [jumpTarget] },
            new Dictionary<(int Opcode, int Operand), string[]>(),
            semanticName == null ? new Dictionary<int, string>() : new Dictionary<int, string> { [0x55] = semanticName });

    [Fact]
    public void TheContractsFingerprintIsDeterministicAndCoversEachContract()
    {
        Assert.Equal(StampContracts().Fingerprint(), StampContracts().Fingerprint());
        Assert.Matches("^sha256:[0-9a-f]{64}$", StampContracts().Fingerprint());
        Assert.NotEqual(StampContracts().Fingerprint(), StampContracts(jumpTarget: 2).Fingerprint());
        Assert.NotEqual(StampContracts().Fingerprint(), StampContracts(semanticName: "assign").Fingerprint());
        Assert.NotEqual(StampContracts().Fingerprint(), Contracts.Fingerprint());
    }

    // A registry rebuilt under the same ABI id (framing, label, semantic name or a contract changed) is another
    // toolchain, so a link compiled against the old one is a different cache key.
    [Fact]
    [Trait("Category", "Workspace")]
    public void TheToolchainStampFollowsTheOpcodeTableAndContractsUnderOneAbiId()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        ModLinkBase Base(OpcodeTable table, OpcodeContracts contracts)
            => ModLinkBase.ForInstall("himegari", Paths.Sys4Ini, catalog, store, table, contracts);
        static string KeyOf(ModLinkBase linkBase) => ModLinkCache.ComputeKey(linkBase, [], new Dictionary<string, int>());

        ModLinkBase reference = Base(StampTable(), StampContracts());
        ModLinkBase same = Base(StampTable(), StampContracts());
        Assert.Equal(reference.ToolchainStamp, same.ToolchainStamp);
        Assert.Equal(KeyOf(reference), KeyOf(same));

        var changed = new (string What, ModLinkBase Base)[]
        {
            ("argc", Base(StampTable(argc: 3), StampContracts())),
            ("label", Base(StampTable(label: "move"), StampContracts())),
            ("table semantic name", Base(StampTable(semanticName: "assign"), StampContracts())),
            ("code target", Base(StampTable(), StampContracts(jumpTarget: 2))),
            ("contract semantic name", Base(StampTable(), StampContracts(semanticName: "assign"))),
        };
        foreach (var (what, linkBase) in changed)
        {
            Assert.Equal(reference.Table.AbiId, linkBase.Table.AbiId);
            Assert.True(reference.ToolchainStamp != linkBase.ToolchainStamp, $"{what}: the stamp did not change");
            Assert.True(KeyOf(reference) != KeyOf(linkBase), $"{what}: the cache key did not change");
        }
    }

    // ------------------------------------------------------------------------------------------- gate 2

    private static string Examples => Path.Combine(Paths.Repo, "oms", "examples", "mods");

    private static ModLinkBase InstalledBase()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        return ModLinkBase.ForInstall("himegari", Paths.Sys4Ini, catalog, store, HimegariTable, Contracts,
            Paths.GlobalsJson("himegari"), Paths.OmsModules("himegari"), Paths.PatchPointsJson("himegari"));
    }

    private ModPackage Example(string id)
    {
        ModPackage mod = ModPackage.Open(Path.Combine(Examples, id));
        _opened.Add(mod);
        return mod;
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Gate2TwoModsPatchingDifferentSitesCompose()
    {
        ModLinkBase linkBase = InstalledBase();
        ModLinkResult result = ModLinker.Link(linkBase,
            [Example("example.sc0000-page4"), Example("example.sc0000-page3")], []);

        Assert.True(result.Succeeded, result.Report.Render());
        LinkedScript sc0000 = Assert.Single(result.Scripts.Values);
        Assert.Equal("SC0000.BIN", sc0000.Name);
        Assert.Equal(["example.sc0000-page3", "example.sc0000-page4"], sc0000.PatchedBy);
        Assert.Equal(2, result.Report.Lines.Count(line => line.Message.Contains("at load time (no stored plan)")));

        // The same as applying both fragments' plans to the pristine scene; each mod's text is in the result.
        var script = Ome.Script.Ir.IrDecoder.Decode(sc0000.Bytes, HimegariTable, Contracts, "SC0000.BIN");
        byte[] original = linkBase.ReadScript("SC0000.BIN")!;
        Assert.True(script.Code.Count > Ome.Script.Ir.IrDecoder.Decode(original, HimegariTable, Contracts, "SC0000.BIN").Code.Count);
        // The base's 320 read-message entries keep their indices; each mod's line adds one.
        Assert.Equal(322, Sys4Loader.Parse(sc0000.Bytes, HimegariTable, "SC0000.BIN", 0x22).ReadMessageOffsets.Count);
        Assert.Contains(script.Data, item => item.Text?.Contains("一つ目の MOD") == true);
        Assert.Contains(script.Data, item => item.Text?.Contains("二つ目の MOD") == true);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Gate2AThirdModThatOverlapsBothStopsTheLinkNamingAllThree()
    {
        ModLinkResult result = ModLinker.Link(InstalledBase(),
            [Example("example.sc0000-page3"), Example("example.sc0000-page4"), Example("example.sc0000-merge")], []);

        Assert.False(result.Succeeded);
        var conflicts = result.Report.Lines.Where(line => line.Class == ModReportClass.HardConflict).ToList();
        Assert.Contains(conflicts, line => line.Mods.Order().SequenceEqual(["example.sc0000-merge", "example.sc0000-page3"]));
        Assert.Contains(conflicts, line => line.Mods.Order().SequenceEqual(["example.sc0000-merge", "example.sc0000-page4"]));
        Assert.Equal(["example.sc0000-merge", "example.sc0000-page3", "example.sc0000-page4"], result.Report.FailedMods);
        Assert.All(conflicts, line => Assert.Equal("SC0000.BIN", line.Script));
        Assert.Contains("inside the region example.sc0000-merge (patches/merge) replaces", result.Report.Render());
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Gate2TheSameStackInAnotherUserOrderLinksIdenticallyWhereDependenciesFixTheOrder()
    {
        // page4 declares page3 as a dependency, so the user's order cannot change which loads first.
        string folder = Path.Combine(_root, "page4-requires");
        CopyDirectory(Path.Combine(Examples, "example.sc0000-page4"), folder);
        File.AppendAllText(Path.Combine(folder, "manifest.toml"), "\n[[requires]]\nid = \"example.sc0000-page3\"\n");
        ModPackage dependent = ModPackage.Open(folder);
        _opened.Add(dependent);
        ModLinkBase linkBase = InstalledBase();

        ModLinkResult forward = ModLinker.Link(linkBase, [Example("example.sc0000-page3"), dependent],
                                               ["example.sc0000-page3", "example.sc0000-page4"]);
        ModLinkResult reverse = ModLinker.Link(linkBase, [dependent, Example("example.sc0000-page3")],
                                               ["example.sc0000-page4", "example.sc0000-page3"]);

        Assert.True(forward.Succeeded && reverse.Succeeded, forward.Report.Render() + reverse.Report.Render());
        Assert.Equal(Order(forward), Order(reverse));
        Assert.Equal(forward.Scripts["SC0000.BIN"].Bytes, reverse.Scripts["SC0000.BIN"].Bytes);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void AMatchingStoredPlanIsUsedAndAStaleOneIsRecompiledFromSource()
    {
        ModLinkBase linkBase = InstalledBase();
        string folder = Path.Combine(_root, "page3-plan");
        CopyDirectory(Path.Combine(Examples, "example.sc0000-page3"), folder);
        ModLinkResult compiled = ModLinker.Link(linkBase, [Track(ModPackage.Open(folder))], []);
        Assert.True(compiled.Succeeded, compiled.Report.Render());

        // Store the plan the loader compiled: the next link uses it and compiles nothing.
        var unit = Ome.Script.Syntax.Parser.ParseScript(
            File.ReadAllText(Path.Combine(folder, "patches", "page3.oms")), "page3.oms");
        OmsPatchPlan plan = OmsPatchCompiler.Compile(unit, linkBase.ReadScript("SC0000.BIN")!, HimegariTable, Contracts,
            linkBase.Intrinsics, linkBase.Modules, linkBase.Idioms);
        string planPath = Path.Combine(folder, "patches", "page3.omspatch.json");
        File.WriteAllText(planPath, plan.ToJson());
        ModLinkResult stored = ModLinker.Link(linkBase, [Track(ModPackage.Open(folder))], []);
        Assert.True(stored.Succeeded, stored.Report.Render());
        Assert.DoesNotContain(stored.Report.Lines, line => line.Message.Contains("at load time"));
        Assert.Equal(compiled.Scripts["SC0000.BIN"].Bytes, stored.Scripts["SC0000.BIN"].Bytes);

        // A plan for another revision of the scene is recompiled from the source.
        File.WriteAllText(planPath, plan.ToJson().Replace(plan.Fingerprint, "sha256:" + new string('3', 64)));
        ModLinkResult stale = ModLinker.Link(linkBase, [Track(ModPackage.Open(folder))], []);
        Assert.True(stale.Succeeded, stale.Report.Render());
        Assert.Contains(stale.Report.Lines, line => line.Message.Contains("its stored plan targets sha256:3333"));

        // Without the source, the stale plan is an error.
        File.Delete(Path.Combine(folder, "patches", "page3.oms"));
        ModLinkResult planOnly = ModLinker.Link(linkBase, [Track(ModPackage.Open(folder))], []);
        Assert.Contains(planOnly.Report.Lines, line => line.Stops && line.Message.Contains("the plan was compiled against sha256:3333"));
    }

    private ModPackage Track(ModPackage mod)
    {
        _opened.Add(mod);
        return mod;
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
