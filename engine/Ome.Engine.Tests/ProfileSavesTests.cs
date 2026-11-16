using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Launcher spec §8.3 and §8.4 Saves (launcher plan §5.5; gate 7's adoption and selector cases): starting a
/// profile from the bare engine's data or the original game's saves, copying saves in with their selectors, and the
/// counts. On synthetic folders: a fake Godot user folder and a fake local application data folder.</summary>
public sealed class ProfileSavesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-profile-saves-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;
    private readonly string _install;
    private readonly string _godotUser;

    /// <summary>Kamidori's layout: the saves in SAVE beneath the settings folder.</summary>
    private static readonly Sys4StartupSettings Startup = Sys4StartupSettings.FromPairs(
    [
        new("USEAPPDATAFOLDER", "1"),
        new("REGFILEPATH", @"Eushully\Kamidori"),
        new("SAVEPATH", @"Eushully\Kamidori\SAVE"),
    ]);

    public ProfileSavesTests()
    {
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
        _install = Path.Combine(_root, "games", "kamidori");
        _godotUser = Path.Combine(_root, "godot-user");
        Directory.CreateDirectory(_install);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static GameProfileManifest Kamidori => GameProfileRegistry.BuiltIn.Find("kamidori")!;

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A package; <paramref name="lua"/> makes it a Lua library, which has no <c>[game]</c> table.</summary>
    private static void Package(string folder, string id, string target = "native", string requires = "", bool lua = false)
    {
        Write(Path.Combine(folder, id, "manifest.toml"),
              $"omp = {(target == "native" && !lua ? 1 : 3)}\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n"
              + (lua ? "[lua]\napi = 1\n" : $"[game]\nprofile = \"kamidori\"\ntarget = \"{target}\"\n") + requires);
        if (lua) Write(Path.Combine(folder, id, "lua", "init.lua"), "return {}");
    }

    private ProfileDocument Document() => ProfileDocument.New("Kamidori", "kamidori", _install, null, _home);

    private string Profile(string id, params (string File, string Text)[] files)
    {
        string root = ProfileStore.Create(_home, LauncherSettings.Defaults, id, Document()).Profile.Root;
        foreach (var (file, text) in files) Write(Path.Combine(root, file), text);
        return root;
    }

    [Fact]
    public void AdoptingTheBareEnginesDataRunsTheStackItRan()
    {
        BareEngineData data = BareEngineData.For(Kamidori, _godotUser);
        Assert.Equal(Path.Combine(_godotUser, "games", "kamidori"), data.Root);
        string mods = data.ModsDirectory;
        foreach (string id in new[] { "example.alpha", "example.bravo", "example.charlie" }) Package(mods, id);
        Package(mods, "example.delta", target: "extended");
        Write(Path.Combine(mods, "broken", "manifest.toml"), "omp = 1\nid = \n");
        Write(Path.Combine(mods, ModLaunch.LoadOrderFile), "# the bare engine's order\nexample.bravo\n-example.charlie\nexample.gone\n");
        Write(Path.Combine(mods, SelectorStore.FileName), "[selectors]\n\"example.alpha\" = 1\n");
        Write(Path.Combine(data.Root, "SAVE", "SAVE01.DAT"), "save one");
        Write(Path.Combine(data.Root, "SAVE", "SAVE01.mods.toml"), "stamp");
        Write(Path.Combine(data.Root, "SAVE", "SAVE.DAT"), "shared");
        Write(Path.Combine(data.Root, "SYS4REG.INI"), "[display]\nScreenMode=1\n");
        Write(data.SharedOmeIni, "[ui]\nLanguage=en\n[later]\nKept=1\n");

        AdoptionPlan plan = Adoption.Plan(data, Document());
        // load-order.txt's listed ids that are there, in order, then the others by id; disabled ones stay off.
        Assert.Equal(["example.bravo", "example.alpha", "example.delta"], plan.Document.Mods!.Enabled);
        Assert.Equal(ProfileMode.Extended, plan.Document.Mode);
        Assert.Contains("targets extended", plan.NeedsExtended["example.delta"]);
        Assert.Contains("cannot be read", Assert.Single(plan.Notes));
        Assert.Equal([_home.ModsDirectory("kamidori"), _home.CommonModsDirectory, mods], plan.Document.Mods.Folders);

        string root = Adoption.Adopt(_home, LauncherSettings.Defaults, "adopted", data, plan, Startup).Profile.Root;
        ProfileFile file = ProfileFile.TryRead(root)!;
        Assert.Equal(ProfileMode.Extended, file.Mode);
        Assert.Equal(["example.bravo", "example.alpha", "example.delta"], file.Mods!.Enabled);
        Assert.Equal("save one", File.ReadAllText(Path.Combine(root, "SAVE", "SAVE01.DAT")));
        Assert.True(File.Exists(Path.Combine(root, "SAVE", "SAVE01.mods.toml")));
        Assert.True(File.Exists(Path.Combine(root, "SAVE", "SAVE.DAT")));
        Assert.Equal(1, SelectorStore.Load(Path.Combine(root, "mods", SelectorStore.FileName)).Selectors["example.alpha"]);
        Assert.Contains("ScreenMode=1", File.ReadAllText(Path.Combine(root, "SYS4REG.INI")));
        // The shared OME.INI is copied as it is, keys the port does not know included.
        Assert.Equal(File.ReadAllText(data.SharedOmeIni), File.ReadAllText(Path.Combine(root, PortOptions.FileName)));
        Assert.False(File.Exists(Path.Combine(root, "mods", ModLaunch.LoadOrderFile)));
    }

    [Fact]
    public void AStackTheNativeModeRunsStaysInCompatibility()
    {
        BareEngineData data = BareEngineData.For(Kamidori, _godotUser);
        Package(data.ModsDirectory, "example.alpha");
        AdoptionPlan plan = Adoption.Plan(data, Document());
        Assert.Equal(ProfileMode.Native, plan.Document.Mode);
        Assert.Empty(plan.NeedsExtended);
    }

    [Fact]
    public void TheNativeRefusalsAreTheModeChecksRule()
    {
        string library = Path.Combine(_root, "library");
        Package(library, "example.plain");
        Package(library, "example.lua", lua: true);
        Package(library, "example.user", requires: "[[requires]]\nid = \"example.lua\"\n");
        Package(library, "example.base-user", requires: "[[requires]]\nid = \"kamidori-base\"\n");
        IReadOnlyDictionary<string, string> refusals = ModeCheck.NativeRefusals(
            ModLaunch.Candidates(library).Select(path => PackageIndex.Read(path)));
        Assert.False(refusals.ContainsKey("example.plain"));
        Assert.Equal("it ships Lua", refusals["example.lua"]);
        Assert.Contains("requires example.lua", refusals["example.user"]);
        Assert.Contains("requires kamidori-base", refusals["example.base-user"]);
    }

    [Fact]
    public void TheOriginalGamesSavesComeWithTheirSettings()
    {
        string local = Path.Combine(_root, "local-app-data");
        Write(Path.Combine(local, "Eushully", "Kamidori", "SAVE", "SAVE02.DAT"), "native");
        Write(Path.Combine(local, "Eushully", "Kamidori", "SAVE", "SAVE02.STH"), "thumbnail");
        Write(Path.Combine(local, "Eushully", "Kamidori", "SYS4REG.INI"), "[display]\nScreenMode=0\n");
        SaveSource native = SaveSource.Native(Startup, _install, local);
        Assert.Null(native.Selectors);

        string root = ProfileStore.Create(_home, LauncherSettings.Defaults, "from-native", Document(), fill: root =>
            ProfileSaves.CopyInLocked(root, Startup, native, withSettings: true, replace: false)).Profile.Root;
        Assert.Equal("native", File.ReadAllText(Path.Combine(root, "SAVE", "SAVE02.DAT")));
        Assert.True(File.Exists(Path.Combine(root, "SAVE", "SAVE02.STH")));
        Assert.Contains("ScreenMode=0", File.ReadAllText(Path.Combine(root, "SYS4REG.INI")));
        Assert.False(File.Exists(Path.Combine(root, "mods", SelectorStore.FileName)));
    }

    [Fact]
    public void AgreeingSelectorsMergeAndClashingOnesAreRefused()
    {
        string source = Profile("source", ("SAVE/SAVE01.DAT", "one"),
                                ("mods/selectors.toml", "[selectors]\n\"example.alpha\" = 1\n\"example.bravo\" = 2\n"));
        string agrees = Profile("agrees", ("mods/selectors.toml", "[selectors]\n\"example.alpha\" = 1\n\"example.zulu\" = 3\n"));
        SaveCopyResult result = ProfileSaves.CopyIn(agrees, Startup, SaveSource.FromProfile(source, Startup),
                                                    withSettings: false, replace: false);
        Assert.Equal(["SAVE01"], result.Copied);
        Assert.Equal(new Dictionary<string, int> { ["example.alpha"] = 1, ["example.bravo"] = 2, ["example.zulu"] = 3 },
                     SelectorStore.Load(Path.Combine(agrees, "mods", SelectorStore.FileName)).Selectors);

        string twoNumbers = Profile("two-numbers", ("mods/selectors.toml", "[selectors]\n\"example.alpha\" = 5\n"));
        var refused = Assert.Throws<SaveCopyRefusedException>(() => ProfileSaves.CopyIn(
            twoNumbers, Startup, SaveSource.FromProfile(source, Startup), withSettings: false, replace: false));
        Assert.Contains("example.alpha has selector 5 in one and 1 in the other", refused.Message);
        Assert.False(Directory.Exists(Path.Combine(twoNumbers, "SAVE")));

        string twoPackages = Profile("two-packages", ("mods/selectors.toml", "[selectors]\n\"example.yankee\" = 2\n"));
        refused = Assert.Throws<SaveCopyRefusedException>(() => ProfileSaves.CopyIn(
            twoPackages, Startup, SaveSource.FromProfile(source, Startup), withSettings: false, replace: false));
        Assert.Contains("selector 2 is example.yankee's in one and example.bravo's in the other", refused.Message);
        Assert.Equal("[selectors]\n\"example.yankee\" = 2\n",
                     File.ReadAllText(Path.Combine(twoPackages, "mods", SelectorStore.FileName)));
    }

    [Fact]
    public void AReplacedSlotKeepsNoneOfTheOldOnesFiles()
    {
        string source = Profile("source", ("SAVE/SAVE01.DAT", "new"), ("SAVE/SAVE02.DAT", "two"));
        string target = Profile("target", ("SAVE/SAVE01.DAT", "old"), ("SAVE/SAVE01.mods.toml", "old stamp"),
                                ("SAVE/SAVE03.DAT", "kept"));
        SaveSource from = SaveSource.FromProfile(source, Startup);
        Assert.Equal(["SAVE01"], ProfileSaves.WouldReplace(from, Path.Combine(target, "SAVE")));
        var refused = Assert.Throws<SaveCopyRefusedException>(
            () => ProfileSaves.CopyIn(target, Startup, from, withSettings: false, replace: false));
        Assert.Equal(["SAVE01"], refused.Details);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "SAVE", "SAVE01.DAT")));

        SaveCopyResult result = ProfileSaves.CopyIn(target, Startup, from, withSettings: false, replace: true);
        Assert.Equal(["SAVE01", "SAVE02"], result.Copied);
        Assert.Equal(["SAVE01"], result.Replaced);
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "SAVE", "SAVE01.DAT")));
        Assert.False(File.Exists(Path.Combine(target, "SAVE", "SAVE01.mods.toml")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(target, "SAVE", "SAVE03.DAT")));

        SaveCounts counts = ProfileSaves.Count(Path.Combine(target, "SAVE"));
        Assert.Equal(3, counts.Saves);
        Assert.Equal(new DirectoryInfo(Path.Combine(target, "SAVE")).GetFiles("SAVE0?.DAT").Max(file => file.LastWriteTimeUtc),
                     counts.LastWrittenUtc);
        Assert.Equal(new SaveCounts(0, null), ProfileSaves.Count(Path.Combine(_root, "no-saves")));
    }

    [Fact]
    public void SavesAreNotCopiedIntoOrOutOfARunningProfile()
    {
        string source = Profile("source", ("SAVE/SAVE01.DAT", "one"));
        string target = Profile("target");
        foreach (string running in new[] { source, target })
        {
            Assert.True(RunLock.TryAcquire(running, markStart: true, out RunLock? held, out _));
            using (held)
                Assert.Throws<ProfileBusyException>(() => ProfileSaves.CopyIn(
                    target, Startup, SaveSource.FromProfile(source, Startup), withSettings: false, replace: false));
        }
        Assert.False(Directory.Exists(Path.Combine(target, "SAVE")));
    }
}
