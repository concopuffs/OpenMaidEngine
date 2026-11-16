using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Script.Abi;

/// <summary>Launcher spec §5.4 (gate 3): a <c>native</c> profile runs only what native AGE runs. Its cache part is in
/// <see cref="ProfileModsTests"/>.</summary>
public sealed class ModeCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-mode-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = [];

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);

    private string Library => Path.Combine(_root, "library");

    public void Dispose()
    {
        foreach (ModPackage package in _opened) package.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A package: <paramref name="target"/> null leaves out <c>[game]</c> (a library); <paramref name="lua"/>
    /// gives it <c>[lua]</c> and <c>lua/init.lua</c>; <paramref name="script"/> one new script, so it links.</summary>
    private string Package(string id, string? target = "native", bool lua = false, string requires = "",
                           bool script = false, string? parent = null)
    {
        string folder = Path.Combine(parent ?? Library, id);
        Directory.CreateDirectory(folder);
        string name = "S" + new string(id.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = {(target == "native" && !lua ? 1 : 3)}\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n"
            + (script ? $"[scripts]\nnames = [\"{name}\"]\n" : "")
            + (target != null ? $"[game]\nprofile = \"himegari\"\ntarget = \"{target}\"\n" : "")
            + (lua ? "[lua]\napi = 1\n" : "") + requires);
        if (lua)
        {
            Directory.CreateDirectory(Path.Combine(folder, "lua"));
            File.WriteAllText(Path.Combine(folder, "lua", "init.lua"), "return {}");
        }
        if (script)
        {
            Directory.CreateDirectory(Path.Combine(folder, "scripts"));
            File.WriteAllText(Path.Combine(folder, "scripts", name + ".oms"), $"oms 1\nscript \"{name}\"\n\n    exit\n");
        }
        return folder;
    }

    private static ProfileMods Mods(string[] folders, params string[] order)
        => new(folders, order.Select(id => new ProfileModEntry(id, true)).ToList(), false);

    private ProfileMods Mods(params string[] order) => Mods([Library], order);

    private (ModDiscovery Discovery, ModLinkReport Report) Discover(ProfileMods table, string? mode, params string[] extra)
    {
        var report = new ModLinkReport();
        ModDiscovery discovery = ModLaunch.DiscoverTable(table, Path.Combine(_root, "none"), "himegari", extra, report, mode);
        _opened.AddRange(discovery.Packages);
        return (discovery, report);
    }

    private static ModReportLine Refusal(ModLinkReport report, string id)
        => Assert.Single(report.Lines, line => line.Stops && line.Mods.SequenceEqual([id]));

    [Fact]
    public void ANativeProfileRefusesAnExtendedPackageAndALuaLibraryByName()
    {
        Package("plain");
        Package("ext", target: "extended");
        Package("names-lib", target: null, lua: true);

        var (discovery, report) = Discover(Mods("plain", "ext", "names-lib"), ModeCheck.Native);

        Assert.True(discovery.FailedToOpen);
        Assert.Equal(["plain"], discovery.Packages.Select(package => package.Id));
        Assert.Equal(["plain"], discovery.UserOrder);
        Assert.Contains("the profile's mode is native", Refusal(report, "ext").Message);
        Assert.Contains("it targets extended", Refusal(report, "ext").Message);
        Assert.Contains("it ships Lua", Refusal(report, "names-lib").Message);
    }

    [Fact]
    public void ANativeProfileRefusesAModPackageOfEitherKind()
    {
        string ext = Package("dev-ext", target: "extended", parent: Path.Combine(_root, "dev"));
        string lib = Package("dev-lib", target: null, lua: true, parent: Path.Combine(_root, "dev"));

        var (discovery, report) = Discover(Mods(), ModeCheck.Native, ext, lib);

        Assert.Empty(discovery.Packages);
        Assert.Contains("it targets extended", Refusal(report, "dev-ext").Message);
        Assert.Contains("it ships Lua", Refusal(report, "dev-lib").Message);
    }

    [Fact]
    public void ANativePackageThatRequiresABaseModIsRefusedUnderItsOwnName()
    {
        Package("app", requires: "[[requires]]\nid = \"himegari-base\"\n");

        var (discovery, report) = Discover(Mods("app"), ModeCheck.Native);

        Assert.Empty(discovery.Packages);
        Assert.Contains("it requires himegari-base, which the native mode refuses", Refusal(report, "app").Message);
        Assert.DoesNotContain(report.Lines, line => line.Mods.Contains("himegari-base"));
    }

    [Fact]
    public void ARefusalReachesThePackagesThatRequireIt()
    {
        Package("ext", target: "extended");
        Package("app", requires: "[[requires]]\nid = \"ext\"\n");
        Package("top", requires: "[[requires]]\nid = \"app\"\n");

        var (discovery, report) = Discover(Mods("top", "app", "ext"), ModeCheck.Native);

        Assert.Empty(discovery.Packages);
        Assert.Contains("it requires ext", Refusal(report, "app").Message);
        Assert.Contains("it requires app", Refusal(report, "top").Message);
    }

    [Fact]
    public void TheSameProfileAsExtendedAndARunWithoutAProfileFileAdmitThem()
    {
        Package("ext", target: "extended");
        Package("names-lib", target: null, lua: true);
        Package("app", requires: "[[requires]]\nid = \"himegari-base\"\n");

        foreach (string? mode in new[] { ModeCheck.Extended, null })
        {
            var (discovery, report) = Discover(Mods("ext", "names-lib", "app"), mode);
            Assert.False(discovery.FailedToOpen, report.Render());
            Assert.Equal(["app", "ext", "himegari-base", "names-lib"], discovery.Packages.Select(package => package.Id).Order());
        }
    }

    [Fact]
    public void AProfileWithoutAModsTableStillHasItsMode()
    {
        string mods = Path.Combine(_root, "profile", ModLaunch.ModsFolder);
        Package("plain", parent: mods);
        Package("ext", target: "extended", parent: mods);
        var report = new ModLinkReport();

        ModDiscovery discovery = ModLaunch.Discover(mods, [], report, ModeCheck.Native);
        _opened.AddRange(discovery.Packages);

        Assert.True(discovery.FailedToOpen);
        Assert.Equal(["plain"], discovery.Packages.Select(package => package.Id));
        Assert.Contains("it targets extended", Refusal(report, "ext").Message);
    }

    [Fact]
    public void ARefusedPackageStopsTheStartUnlessFailedModsAreDropped()
    {
        Package("plain", script: true);
        Package("ext", target: "extended", script: true);
        string profile = Path.Combine(_root, "profile");
        ModLinkBase linkBase = new()
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
            ProfileMode = ModeCheck.Native,
        };
        ModLaunchResult Link(bool skip) => ModLaunch.Run(linkBase, profile, Path.Combine(profile, "diagnostics"),
                                                         new ModLaunchOptions([], false, skip, []), Mods("plain", "ext"));

        Assert.True(Link(false).Failed);
        ModLaunchResult skipped = Link(true);
        Assert.False(skipped.Failed, skipped.Report.Render());
        Assert.Equal(["plain"], skipped.Stack.Select(mod => mod.Id));
    }
}
