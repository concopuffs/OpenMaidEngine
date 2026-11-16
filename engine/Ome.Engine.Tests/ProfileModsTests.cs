using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Script.Abi;

/// <summary>Launcher spec §5.3 (gate 2): a profile's <c>[mods]</c> table names its packages. Two profile roots share
/// one synthetic library; only the ids <c>order</c> enables are opened and hashed.</summary>
public sealed class ProfileModsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-profile-mods-" + Guid.NewGuid().ToString("N"));
    private readonly List<FileStream> _held = [];

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

    private string Library => Path.Combine(_root, "library");

    public void Dispose()
    {
        foreach (FileStream stream in _held) stream.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A package folder named <paramref name="folder"/> (default: its id) with one new script, so it owns a
    /// selector.</summary>
    private string Package(string parent, string id, string game = "himegari", string version = "1.0.0",
                           string requires = "", string? folder = null)
    {
        string path = Path.Combine(parent, folder ?? id);
        Directory.CreateDirectory(Path.Combine(path, "scripts"));
        string script = "S" + new string(id.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        File.WriteAllText(Path.Combine(path, "manifest.toml"),
            $"omp = 1\nid = \"{id}\"\nname = \"{id}\"\nversion = \"{version}\"\n[scripts]\nnames = [\"{script}\"]\n"
            + $"[game]\nprofile = \"{game}\"\ntarget = \"native\"\n" + requires);
        File.WriteAllText(Path.Combine(path, "scripts", script + ".oms"), $"oms 1\nscript \"{script}\"\n\n    exit\n");
        return path;
    }

    /// <summary>Makes a package unreadable past its manifest: a file the hash would read is held for exclusive use,
    /// so opening and hashing it fails while the manifest still reads.</summary>
    private void Unhashable(string package)
    {
        string file = Path.Combine(package, "scripts", "LOCKED.bin");
        File.WriteAllText(file, "x");
        _held.Add(new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }

    private static ProfileMods Mods(IEnumerable<string> folders, params string[] order)
        => new(folders.ToList(), order.Select(id => id.StartsWith('-') ? new ProfileModEntry(id[1..], false)
                                                                       : new ProfileModEntry(id, true)).ToList(), false);

    private ProfileMods Mods(params string[] order) => Mods([Library], order);

    private string ProfileRoot(string name) => Path.Combine(_root, name);

    private static ModLaunchResult Link(string profileRoot, ProfileMods table, bool skipFailed = false)
        => ModLaunch.Run(SyntheticBase(), profileRoot, Path.Combine(profileRoot, "diagnostics"),
                         new ModLaunchOptions([], false, skipFailed, []), table);

    private static ModDiscovery Discover(ProfileMods table, ModLinkReport report, params string[] extra)
        => ModLaunch.DiscoverTable(table, Path.Combine(Path.GetTempPath(), "ome-no-mods"), "himegari", extra, report);

    [Fact]
    public void TwoProfilesSharingALibraryLinkTheirOwnStacksAndSelectors()
    {
        Package(Library, "alpha");
        Package(Library, "beta");
        Package(Library, "gamma");
        string one = ProfileRoot("one"), two = ProfileRoot("two");

        ModLaunchResult first = Link(one, Mods("gamma", "alpha"));
        ModLaunchResult second = Link(two, Mods("beta", "-alpha"));

        Assert.False(first.Failed, first.Report.Render());
        Assert.False(second.Failed, second.Report.Render());
        Assert.Equal(["gamma", "alpha"], first.Stack.Select(mod => mod.Id));
        Assert.Equal(["beta"], second.Stack.Select(mod => mod.Id));
        string oneSelectors = File.ReadAllText(Path.Combine(one, ModLaunch.ModsFolder, SelectorStore.FileName));
        string twoSelectors = File.ReadAllText(Path.Combine(two, ModLaunch.ModsFolder, SelectorStore.FileName));
        Assert.Contains("\"gamma\"", oneSelectors);
        Assert.DoesNotContain("\"beta\"", oneSelectors);
        Assert.Contains("\"beta\"", twoSelectors);
        Assert.DoesNotContain("\"gamma\"", twoSelectors);
        Assert.False(File.Exists(Path.Combine(Library, SelectorStore.FileName)), "the library holds no profile's state");
        Assert.Contains(second.Report.Lines, line => line.Mods.SequenceEqual(["alpha"]) && line.Message == "disabled by profile.toml");
    }

    [Fact]
    public void AddingAPackageToTheLibraryChangesNeitherLink()
    {
        Package(Library, "alpha");
        Package(Library, "beta");
        string one = ProfileRoot("one"), two = ProfileRoot("two");
        string oneKey = Link(one, Mods("alpha")).Layer!.Key, twoKey = Link(two, Mods("beta")).Layer!.Key;

        Package(Library, "newcomer");
        File.WriteAllText(Path.Combine(Library, "broken.omp"), "not a zip");

        ModLaunchResult oneAgain = Link(one, Mods("alpha")), twoAgain = Link(two, Mods("beta"));
        Assert.True(oneAgain.FromCache, oneAgain.Report.Render());
        Assert.True(twoAgain.FromCache, twoAgain.Report.Render());
        Assert.Equal(oneKey, oneAgain.Layer!.Key);
        Assert.Equal(twoKey, twoAgain.Layer!.Key);
    }

    [Fact]
    public void PackagesTheProfileDoesNotEnableAreNotHashedWithFailedModsDroppedOrNot()
    {
        Package(Library, "alpha");
        Unhashable(Package(Library, "heavy"));
        File.WriteAllText(Path.Combine(Library, "broken.omp"), "not a zip");
        string one = ProfileRoot("one"), two = ProfileRoot("two");

        foreach (bool skip in new[] { false, true })
        {
            ModLaunchResult result = Link(skip ? two : one, Mods("alpha", "-heavy"), skip);
            Assert.False(result.Failed, result.Report.Render());
            Assert.Equal(["alpha"], result.Stack.Select(mod => mod.Id));
            Assert.DoesNotContain(result.Report.Lines, line => line.Stops);
            Assert.Contains(result.Report.Lines, line => line.Mods.SequenceEqual(["broken.omp"])
                                                         && line.Class == ModReportClass.Note);
        }

        // The same package enabled is opened and hashed, so its locked file fails it.
        ModLaunchResult enabled = Link(ProfileRoot("three"), Mods("alpha", "heavy"));
        Assert.True(enabled.Failed);
        Assert.Contains(enabled.Report.Lines, line => line.Stops && line.Message.Contains("LOCKED.bin"));
    }

    [Fact]
    public void AnEnabledIdInNoFolderStopsAtDiscovery()
    {
        Package(Library, "alpha");
        var report = new ModLinkReport();

        ModDiscovery discovery = Discover(Mods("alpha", "absent"), report);

        Assert.True(discovery.FailedToOpen);
        Assert.Contains(report.Lines, line => line.Stops && line.Mods.SequenceEqual(["absent"])
                                              && line.Message.Contains("none of its mod folders holds"));
        ModLaunchResult strict = Link(ProfileRoot("one"), Mods("alpha", "absent"));
        Assert.True(strict.Failed);
        ModLaunchResult skipped = Link(ProfileRoot("two"), Mods("alpha", "absent"), skipFailed: true);
        Assert.False(skipped.Failed, skipped.Report.Render());
        Assert.Equal(["alpha"], skipped.Stack.Select(mod => mod.Id));
        foreach (ModPackage package in discovery.Packages) package.Dispose();
    }

    [Fact]
    public void AnEnabledPackageWithADisabledDependencyStopsWithItsName()
    {
        Package(Library, "lib");
        Package(Library, "app", requires: "[[requires]]\nid = \"lib\"\n");
        var report = new ModLinkReport();

        ModDiscovery discovery = Discover(Mods("app", "-lib"), report);

        Assert.True(discovery.FailedToOpen);
        Assert.Empty(discovery.Packages);
        ModReportLine line = Assert.Single(report.Lines, line => line.Stops);
        Assert.Equal(["app"], line.Mods);
        Assert.Contains($"requires lib, which is in {Path.Combine(Library, "lib")} but not enabled in profile.toml", line.Message);

        ModDiscovery both = Discover(Mods("app", "lib"), new ModLinkReport());
        Assert.False(both.FailedToOpen);
        Assert.Equal(["app", "lib"], both.UserOrder);
        foreach (ModPackage package in both.Packages) package.Dispose();
    }

    [Fact]
    public void AnEnabledIdFoundTwiceStops()
    {
        string other = Path.Combine(_root, "other");
        Package(Library, "alpha");
        Package(other, "alpha", version: "1.1.0");
        Package(Library, "beta");
        Package(Library, "beta", version: "2.0.0", folder: "beta-2");
        var report = new ModLinkReport();

        ModDiscovery discovery = Discover(Mods([Library, other], "alpha", "beta"), report);

        Assert.True(discovery.FailedToOpen);
        Assert.Empty(discovery.Packages);
        Assert.Contains(report.Lines, line => line.Stops && line.Mods.SequenceEqual(["alpha"]) && line.Message.Contains("found 2 times"));
        Assert.Contains(report.Lines, line => line.Stops && line.Mods.SequenceEqual(["beta"]) && line.Message.Contains("found 2 times"));
    }

    [Fact]
    public void AnotherGamesCandidateIsLeftOutUnlessItsIdIsEnabledAlone()
    {
        Package(Library, "shared");
        Package(Library, "shared", game: "kamidori", folder: "shared-kamidori");
        Package(Library, "theirs", game: "kamidori");
        var report = new ModLinkReport();

        ModDiscovery discovery = Discover(Mods("shared"), report);
        Assert.False(discovery.FailedToOpen, report.Render());
        Assert.Equal(Path.Combine(Library, "shared"), Assert.Single(discovery.Packages).Location);
        foreach (ModPackage package in discovery.Packages) package.Dispose();

        // Enabled, the other game's package is opened, and the link names the game it targets.
        ModLaunchResult linked = Link(ProfileRoot("one"), Mods("theirs"));
        Assert.True(linked.Failed);
        Assert.Contains(linked.Report.Lines, line => line.Stops && line.Message.Contains("targets profile kamidori"));
    }

    [Fact]
    public void BaseModsJoinWhenRequiredAndAFolderCopyOnlyWhenEnabled()
    {
        const string requiresBase = "[[requires]]\nid = \"himegari-base\"\n";
        Package(Library, "app", requires: requiresBase);
        Package(Library, "himegari-base", version: "1.0.0");

        // A folder's base mod that order does not enable leaves the engine's own in place, and is no error.
        var report = new ModLinkReport();
        ModDiscovery engineOwn = Discover(Mods("app"), report);
        Assert.False(engineOwn.FailedToOpen, report.Render());
        Assert.Equal(ShippedPackages.LocationOf("himegari-base"),
                     engineOwn.Packages.Single(package => package.Id == "himegari-base").Location);
        foreach (ModPackage package in engineOwn.Packages) package.Dispose();

        // Enabled, the folder's copy is used instead.
        var enabledReport = new ModLinkReport();
        ModDiscovery folderCopy = Discover(Mods("app", "himegari-base"), enabledReport);
        Assert.False(folderCopy.FailedToOpen, enabledReport.Render());
        Assert.Equal(Path.Combine(Library, "himegari-base"),
                     folderCopy.Packages.Single(package => package.Id == "himegari-base").Location);
        Assert.Contains(enabledReport.Lines, line => line.Message.Contains("is used instead of the engine's own"));
        foreach (ModPackage package in folderCopy.Packages) package.Dispose();

        // A base mod's id in order with no such package is a note and changes nothing.
        var noteReport = new ModLinkReport();
        ModDiscovery nothing = Discover(Mods([Path.Combine(_root, "empty")], "kamidori-base"), noteReport);
        Assert.False(nothing.FailedToOpen);
        Assert.Empty(nothing.Packages);
        Assert.Empty(nothing.UserOrder);
        Assert.Contains(noteReport.Lines, line => line.Class == ModReportClass.Note && line.Mods.SequenceEqual(["kamidori-base"])
                                                  && line.Message.Contains("changes nothing"));
    }

    [Fact]
    public void TheLoadOrderFileIsNotReadAndExtraPackagesComeLast()
    {
        Package(Library, "alpha");
        Package(Library, "beta");
        string profile = ProfileRoot("one");
        string mods = Path.Combine(profile, ModLaunch.ModsFolder);
        Package(mods, "inroot");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(mods, ModLaunch.LoadOrderFile), "inroot\n-alpha\n");
        string extra = Package(Path.Combine(_root, "dev"), "dev");
        var report = new ModLinkReport();

        ModDiscovery discovery = ModLaunch.DiscoverTable(Mods("beta", "alpha"), mods, "himegari", [extra], report);

        Assert.False(discovery.FailedToOpen, report.Render());
        Assert.Equal(["beta", "alpha", "dev"], discovery.UserOrder);
        Assert.DoesNotContain(discovery.Packages, package => package.Id == "inroot");
        Assert.Contains(report.Lines, line => line.Class == ModReportClass.Note && line.Message.Contains("load-order.txt is not read"));
        foreach (ModPackage package in discovery.Packages) package.Dispose();
    }

    [Fact]
    public void TheIndexReadsAManifestWithoutTheRestOfThePackage()
    {
        string package = Package(Library, "app", requires: "[[requires]]\nid = \"lib\"\n");
        Unhashable(package);

        PackageIndexEntry entry = PackageIndex.Read(package);

        Assert.Equal(("app", "1.0.0", "himegari", "native", false), (entry.Id, entry.Version.ToString(), entry.Game, entry.Target, entry.ShipsLua));
        Assert.Equal(["lib"], entry.Requires.Select(requirement => requirement.Id));
        Assert.Throws<ModPackageException>(() => ModPackage.Open(package).Dispose());
    }

    [Fact]
    public void TheHostsEarlyTestFollowsTheTable()
    {
        string mods = Path.Combine(ProfileRoot("one"), ModLaunch.ModsFolder);
        Package(mods, "inroot");

        Assert.True(ModLaunch.MayHavePackages(mods, null));
        Assert.False(ModLaunch.MayHavePackages(mods, Mods("-alpha")));
        Assert.True(ModLaunch.MayHavePackages(Path.Combine(_root, "none"), Mods("alpha")));
    }
}
