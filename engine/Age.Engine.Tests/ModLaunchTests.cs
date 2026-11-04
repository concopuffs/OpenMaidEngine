using Age.Engine.Model;
using Age.Engine.Persistence;
using Age.Engine.Sys4;
using Age.Mods.Launch;
using Age.Mods.Linking;
using Age.Mods.Tools;
using Age.Script.Abi;

/// <summary>Mod loader spec §8–§10 (plan M6): startup discovery and flags, save stamps, the save sidecar store, and
/// <c>omp build</c>; M7 <c>omp export</c>.</summary>
public sealed class ModLaunchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "age-launch-" + Guid.NewGuid().ToString("N"));

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);

    private static ModLinkBase SyntheticBase() => new()
    {
        Profile = "himegari",
        CatalogFingerprint = "sha256:1111111111111111111111111111111111111111111111111111111111111111",
        ReadScript = _ => null,
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
        InstallSelectors = new HashSet<int> { 1 },
        CatalogTitle = "TEST",
        ToolchainStamp = "test",
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Package(string parent, string id, bool script = true, string target = "native")
    {
        string folder = Path.Combine(parent, id);
        Directory.CreateDirectory(Path.Combine(folder, "scripts"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 1\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n"
            + (script ? "[scripts]\nnames = [\"AUTO\"]\n" : "") + $"[game]\nprofile = \"himegari\"\ntarget = \"{target}\"\n");
        if (script) File.WriteAllText(Path.Combine(folder, "scripts", "AUTO.oms"), "oms 1\nscript \"AUTO\"\n\n    exit\n");
        return folder;
    }

    [Fact]
    public void OptionsParseTheModFlags()
    {
        ModLaunchOptions options = ModLaunchOptions.Resolve(
            ["--scene", "SC0000", "--mod", "a.omp", "--skip-failed-mods", "--forget-mod", "old.mod", "--mod", "b"], _root);
        Assert.Equal([Path.Combine(_root, "a.omp"), Path.Combine(_root, "b")], options.ExtraPackages);
        Assert.True(options.SkipFailedMods);
        Assert.False(options.NoMods);
        Assert.Equal(["old.mod"], options.ForgetMods);
        Assert.Throws<ArgumentException>(() => ModLaunchOptions.Resolve(["--mod", "--no-mods"], _root));
    }

    [Fact]
    public void DiscoveryAppliesTheLoadOrderFileAndOrdersExtraPackagesLast()
    {
        string profile = Path.Combine(_root, "profile");
        string mods = Path.Combine(profile, ModLaunch.ModsFolder);
        Package(mods, "zeta");
        Package(mods, "alpha");
        Package(mods, "beta");
        Package(mods, "off");
        Directory.CreateDirectory(Path.Combine(mods, ".cache", "junk"));          // never a candidate
        File.WriteAllText(Path.Combine(mods, ModLaunch.LoadOrderFile), "# mine\nzeta\n\n-off\nmissing\n");
        string extra = Package(Path.Combine(_root, "dev"), "dev");

        var report = new ModLinkReport();
        ModDiscovery discovery = ModLaunch.Discover(mods, [extra], report);
        try
        {
            Assert.False(discovery.FailedToOpen);
            Assert.Equal(["alpha", "beta", "dev", "zeta"], discovery.Packages.Select(mod => mod.Id).Order());
            Assert.Equal(["zeta", "missing", "alpha", "beta", "dev"], discovery.UserOrder);
            Assert.Contains(report.Lines, line => line.Mods.SequenceEqual(["off"]) && line.Message.Contains("disabled"));
        }
        finally
        {
            foreach (var mod in discovery.Packages) mod.Dispose();
        }
    }

    [Fact]
    public void RunLinksCachesWritesTheReportAndForgetsSelectors()
    {
        string profile = Path.Combine(_root, "profile");
        string mods = Path.Combine(profile, ModLaunch.ModsFolder);
        string diagnostics = Path.Combine(profile, "diagnostics");
        Package(mods, "first.mod");
        Package(mods, "second.mod");
        var options = new ModLaunchOptions([], false, false, []);

        ModLaunchResult linked = ModLaunch.Run(SyntheticBase(), profile, diagnostics, options);
        Assert.False(linked.Failed, linked.Report.Render());
        Assert.False(linked.FromCache);
        Assert.Equal(["first.mod", "second.mod"], linked.Stack.Select(mod => mod.Id));
        Assert.StartsWith("load order: first.mod -> second.mod", File.ReadAllText(linked.ReportPath!));

        ModLaunchResult cached = ModLaunch.Run(SyntheticBase(), profile, diagnostics, options);
        Assert.True(cached.FromCache);
        Assert.Equal(linked.Layer!.Key, cached.Layer!.Key);

        // Removing a package keeps its selector until --forget-mod.
        Directory.Delete(Path.Combine(mods, "first.mod"), recursive: true);
        ModLaunch.Run(SyntheticBase(), profile, diagnostics, options);
        Assert.Contains("\"first.mod\" = 2", File.ReadAllText(Path.Combine(mods, SelectorStore.FileName)));
        ModLaunchResult forgotten = ModLaunch.Run(SyntheticBase(), profile, diagnostics, options with { ForgetMods = ["first.mod"] });
        Assert.DoesNotContain("first.mod", File.ReadAllText(Path.Combine(mods, SelectorStore.FileName)));
        Assert.Contains(forgotten.Report.Lines, line => line.Message.Contains("released its selector"));

        Assert.Null(ModLaunch.Run(SyntheticBase(), profile, diagnostics, options with { NoMods = true }).Layer);
    }

    [Fact]
    public void AnUnreadablePackageStopsStartupUnlessFailingModsAreSkipped()
    {
        string profile = Path.Combine(_root, "profile");
        string mods = Path.Combine(profile, ModLaunch.ModsFolder);
        Package(mods, "good.mod");
        File.WriteAllText(Path.Combine(mods, "broken.omp"), "not a zip");
        var options = new ModLaunchOptions([], false, false, []);

        ModLaunchResult strict = ModLaunch.Run(SyntheticBase(), profile, Path.Combine(profile, "diagnostics"), options);
        Assert.True(strict.Failed);
        Assert.Contains("broken.omp", File.ReadAllText(strict.ReportPath!));

        ModLaunchResult lenient = ModLaunch.Run(SyntheticBase(), profile, Path.Combine(profile, "diagnostics"),
                                                options with { SkipFailedMods = true });
        Assert.False(lenient.Failed);
        Assert.Equal(["good.mod"], lenient.Stack.Select(mod => mod.Id));
    }

    [Fact]
    public void StampsRoundTripAndCompareReportsEveryKindOfDifference()
    {
        var saved = new ModSaveStamp(
            [new ModStackEntry("a", "1.0.0", "sha256:aa"), new ModStackEntry("b", "1.0.0", "sha256:bb"),
             new ModStackEntry("gone", "2.0.0", "sha256:cc")],
            new Dictionary<string, int> { ["a"] = 2, ["gone"] = 3 }, "key1");
        ModSaveStamp reread = ModSaveStamp.TryParse(saved.ToToml())!;
        Assert.Equal(saved.Mods, reread.Mods);
        Assert.Equal(saved.Selectors, reread.Selectors);
        Assert.Equal("key1", reread.Key);
        Assert.Empty(ModSaveStamp.Compare(saved, reread));

        var current = new ModSaveStamp(
            [new ModStackEntry("b", "1.1.0", "sha256:b2"), new ModStackEntry("a", "1.0.0", "sha256:a2"),
             new ModStackEntry("new", "1.0.0", "sha256:dd")],
            new Dictionary<string, int> { ["a"] = 4 }, "key2");
        IReadOnlyList<string> differences = ModSaveStamp.Compare(saved, current);
        Assert.Contains("added new 1.0.0", differences);
        Assert.Contains("removed gone 2.0.0", differences);
        Assert.Contains("b 1.0.0 -> 1.1.0", differences);
        Assert.Contains("a 1.0.0 changed (package hash differs)", differences);
        Assert.Contains("reordered: a -> b became b -> a", differences);
        Assert.Contains(differences, line => line.StartsWith("a's selector 2 -> 4"));

        Assert.Equal(["added a 1.0.0", "added b 1.0.0", "added gone 2.0.0"], ModSaveStamp.Compare(null, saved));
        Assert.Empty(ModSaveStamp.Compare(null, null));
        Assert.Null(ModSaveStamp.TryParse("[[mods]]\nid = 3\n"));
    }

    [Fact]
    public void TheSidecarStoreWritesCarriesAndReportsTheStamp()
    {
        string saves = Path.Combine(_root, "SAVE");
        var identity = new NativeSaveIdentity(NativeSaveMagic.S4SD, 1, "test", 3, 10);
        var inner = new DirectoryNativeDatStore(saves, identity);
        var loads = new List<(int Slot, string? Text)>();
        var stamped = new SidecarNativeDatStore(inner, saves, ".mods.toml", "stack A", (slot, text) => loads.Add((slot, text)));
        var time = new NativeSystemTime(2026, 11, 3, 2, 12, 0, 0, 0);

        stamped.SaveNumberedFile(3, new byte[16], [], time, 0);
        Assert.Equal("stack A", File.ReadAllText(Path.Combine(saves, "SAVE03.mods.toml")));
        stamped.CopyNumberedPair(3, 4);
        Assert.True(File.Exists(Path.Combine(saves, "SAVE04.mods.toml")));
        stamped.LoadNumberedFile(4);
        Assert.Equal((4, "stack A"), Assert.Single(loads));

        // Without a stack a save clears the stale stamp, and a load reports none.
        var plain = new SidecarNativeDatStore(inner, saves, ".mods.toml", null, (slot, text) => loads.Add((slot, text)));
        plain.SaveNumberedFile(4, new byte[16], [], time, 0);
        Assert.False(File.Exists(Path.Combine(saves, "SAVE04.mods.toml")));
        plain.LoadNumberedFile(4);
        Assert.Equal((4, null), loads[^1]);
        plain.DeleteNumberedPair(3);
        Assert.False(File.Exists(Path.Combine(saves, "SAVE03.mods.toml")));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void OmpBuildWritesADeterministicPackageWithStoredPlans()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        ModLinkBase linkBase = ModLinkBase.ForInstall("himegari", Paths.Sys4Ini, catalog, store, Table, Contracts,
            Paths.GlobalsJson("himegari"), Paths.OmsModules("himegari"), Paths.PatchPointsJson("himegari"));
        string source = Path.Combine(Paths.Repo, "oms", "examples", "mods", "example.sc0000-page3");
        string first = Path.Combine(_root, "a.omp"), second = Path.Combine(_root, "b.omp");
        Directory.CreateDirectory(_root);

        Assert.Equal(0, OmpCommands.Build(linkBase, source, first, [], TextWriter.Null));
        Assert.Equal(0, OmpCommands.Build(linkBase, source, second, [], TextWriter.Null));
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));

        using var package = Age.Mods.Packages.ModPackage.Open(first);
        Assert.NotNull(package.Layout.Patches.Single().Plan);
        ModLinkResult linked = ModLinker.Link(linkBase, [package], []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        Assert.DoesNotContain(linked.Report.Lines, line => line.Message.Contains("at load time"));
    }

    [Fact]
    public void OmpExportWritesTheAppendCatalogAndLooseFilesOnly()
    {
        string extra = Package(_root, "example.export");
        string output = Path.Combine(_root, "export");
        var console = new StringWriter();

        Assert.True(OmpCommands.Export(SyntheticBase(), null, [extra], output, console) == 0, console.ToString());
        Assert.Equal(["APPEND02.AAI", "APPEND02.ALF"],
            Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Contains("omp export: 1 mods, 2 files", console.ToString());
    }

    [Fact]
    public void OmpExportRefusesAPortTargetMod()
    {
        string extra = Package(_root, "example.portonly", target: "port");
        string output = Path.Combine(_root, "export");
        var console = new StringWriter();

        Assert.True(OmpCommands.Export(SyntheticBase(), null, [extra], output, console) == 1, console.ToString());
        Assert.False(Directory.Exists(output));
        Assert.Contains("game.target: 'port' is not supported", console.ToString());
    }
}
