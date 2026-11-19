using System.Text;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Script.Patch;

/// <summary>Launcher spec §4.3 and §5.2 (gate 1): <c>profile.toml</c> and the one resolver of the command line and the
/// file. Every install here is synthetic: a <c>SYS4INI.BIN</c> header with a game's identity.</summary>
public sealed class RunResolverTests : IDisposable
{
    private static readonly GameCatalogIdentity Himegari = new("S4IC422", "姫狩りダンジョンマイスター");
    private static readonly GameCatalogIdentity Kamidori = new("S4IC433", "神採りアルケミーマイスター");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-run-resolver-tests", Guid.NewGuid().ToString("N"));

    public RunResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Install(string name, GameCatalogIdentity identity, params string[] overlays)
    {
        string install = Path.Combine(_root, name);
        Directory.CreateDirectory(install);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var header = new byte[264];
        Encoding.ASCII.GetBytes(identity.CatalogRevision.PadRight(8)).CopyTo(header, 0);
        Encoding.GetEncoding(932).GetBytes(identity.Title).CopyTo(header, 8);
        File.WriteAllBytes(Path.Combine(install, "SYS4INI.BIN"), header);
        foreach (string overlay in overlays) Directory.CreateDirectory(Path.Combine(install, overlay));
        return install;
    }

    private string Profile(string name, string toml)
    {
        string profile = Path.Combine(_root, name);
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, ProfileFile.FileName), toml);
        return profile;
    }

    private ResolvedRun Resolve(params string[] arguments) => RunResolver.Resolve(arguments, _root);

    private const string FullProfile = """
        ome_profile = 1
        name = "Kamidori (test)"
        game = "kamidori"
        mode = "extended"

        [install]
        root = "../install"
        overlays = ["patch", "patch2"]
        allow_bmp_as_agf = true

        [mods]
        folders = []
        order = []
        on_failure = "skip"

        [launch]
        renderer = "software"
        text_backend = "portable"
        lua_errors = "halt"

        [launcher]
        cover = "cover.png"
        graphics_api = "d3d12"
        anything = { nested = 1 }
        """;

    /// <summary>What the host's and <c>Ome.Cli</c>'s selections read from an argument list.</summary>
    private static object Selections(IReadOnlyList<string> arguments, string cwd, GameCatalogIdentity identity)
    {
        SelectedGameProfile game = GameProfileSelection.Resolve(arguments, identity);
        GameRootSelection root = GameRootSelection.Resolve(arguments, executablePath: null, cwd);
        AssetLaunchOptions assets = AssetLaunchOptions.Resolve(arguments, root.Root);
        ModLaunchOptions mods = ModLaunchOptions.Resolve(arguments, cwd);
        return string.Join("|",
            game.Profile.Id, game.SourceName, game.IdentityMatched, game.ProbeMode,
            root.Root, root.SourceName,
            string.Join(",", assets.OverlayRoots), string.Join(",", assets.LooseRoots), assets.AllowBmpAsAgf,
            mods.SkipFailedMods, mods.NoMods, string.Join(",", mods.ExtraPackages),
            RunResolver.LastValue(arguments, "--render-backend"), RunResolver.LastValue(arguments, "--text-backend"),
            RunResolver.LastValue(arguments, "--lua-errors"));
    }

    [Fact]
    public void EachKeyHasTheEffectOfItsFlag()
    {
        string install = Install("install", Kamidori, "patch", "patch2");
        string profile = Profile("profile", FullProfile);

        ResolvedRun run = Resolve("--data-root", profile);

        Assert.NotNull(run.ProfileFile);
        Assert.Equal(ProfileMode.Extended, run.Mode);
        Assert.Equal(profile, run.DataRoot);
        string[] flags =
        [
            "--data-root", profile, "--game", "kamidori", "--game-root", install,
            "--overlay-root", "patch", "--overlay-root", "patch2", "--allow-bmp-as-agf",
            "--render-backend", "software", "--text-backend", "portable", "--lua-errors", "halt", "--skip-failed-mods",
        ];
        Assert.Equal(Selections(flags, _root, Kamidori), Selections(run.Arguments, _root, Kamidori));
        Assert.Empty(run.Warnings);
        Assert.Contains("game=kamidori mode=extended", run.Describe());
        Assert.Contains("renderer=software text-backend=portable lua-errors=halt", run.Describe());
    }

    [Fact]
    public void ADataRootWithoutAProfileFileBehavesAsBefore()
    {
        string root = Path.Combine(_root, "sandbox");
        Directory.CreateDirectory(root);

        ResolvedRun run = Resolve("--data-root", "sandbox", "--game-root", "x", "--mod", "m");

        Assert.Null(run.ProfileFile);
        Assert.Null(run.Mods);
        Assert.Null(run.Mode);
        Assert.Null(run.Describe());
        Assert.Equal(root, run.DataRoot);
        Assert.Equal(["--data-root", root, "--game-root", "x", "--mod", "m"], run.Arguments);
    }

    /// <summary>Launcher spec §5.8: <c>--game</c> and <c>--profile</c> are one option, and each single-valued option
    /// keeps its last value, so a reader that takes the first (<c>Ome.Cli</c>) and one that takes the last (the host)
    /// read the same.</summary>
    [Fact]
    public void RepeatedOptionsKeepTheLastValueForEveryReader()
    {
        ResolvedRun run = Resolve("omp", "build", "DIR", "--profile", "himegari", "--game-root", "a",
                                  "--game", "kamidori", "--mod", "x", "--game-root", "b", "--mod", "y",
                                  "--render-backend", "gpu", "--render-backend", "software");

        Assert.Equal(["omp", "build", "DIR", "--game", "kamidori", "--mod", "x", "--game-root", "b", "--mod", "y",
                      "--render-backend", "software"], run.Arguments);
        foreach (string option in new[] { "--game", "--game-root", "--render-backend" })
        {
            int first = run.Arguments.ToList().IndexOf(option), last = run.Arguments.ToList().LastIndexOf(option);
            Assert.Equal(first, last);
        }
        Assert.Equal("kamidori", GameProfileSelection.RequestedGameId(run.Arguments));
    }

    [Fact]
    public void AnOptionWithoutAValueIsLeftForItsReader()
    {
        ResolvedRun run = Resolve("--game-root", "a", "--game-root");

        Assert.Equal(["--game-root"], run.Arguments);
        Assert.Throws<ArgumentException>(() => GameRootSelection.Resolve(run.Arguments, null, _root));
    }

    [Theory]
    [InlineData("--game", "kamidori")]
    [InlineData("--profile", "kamidori")]
    [InlineData("--game-root", "elsewhere")]
    [InlineData("--overlay-root", "patch")]
    [InlineData("--allow-bmp-as-agf")]
    [InlineData("--probe")]
    public void AProfileFileRefusesTheFlagsThatNameTheGameOrTheInstall(params string[] flag)
    {
        Install("install", Kamidori, "patch", "patch2");
        string profile = Profile("profile", FullProfile);

        var error = Assert.Throws<RunResolutionException>(() => Resolve(["--data-root", profile, .. flag]));

        Assert.StartsWith($"{flag[0]} is refused with a profile file ({Path.Combine(profile, "profile.toml")})",
                          error.Message);
    }

    [Fact]
    public void RunFlagsWinOverTheFileAndTheRestPassThrough()
    {
        Install("install", Kamidori, "patch", "patch2");
        string profile = Profile("profile", FullProfile);

        ResolvedRun run = Resolve("--data-root", profile, "--render-backend", "gpu", "--lua-errors", "continue",
                                  "--mod", "extra", "--no-mods", "--language", "en", "--window-width", "800",
                                  "--fps-counter", "--windowed", "--scene", "SC0000");

        Assert.Equal("gpu", RunResolver.LastValue(run.Arguments, "--render-backend"));
        Assert.Equal("continue", RunResolver.LastValue(run.Arguments, "--lua-errors"));
        Assert.Equal("portable", RunResolver.LastValue(run.Arguments, "--text-backend"));
        Assert.Single(run.Arguments, "--render-backend");
        Assert.Single(run.Arguments, "--lua-errors");
        foreach (string passed in new[] { "--mod", "extra", "--no-mods", "--language", "en", "--window-width", "800",
                                          "--fps-counter", "--windowed", "--scene", "SC0000" })
            Assert.Contains(passed, run.Arguments);
        ModLaunchOptions mods = ModLaunchOptions.Resolve(run.Arguments, _root);
        Assert.True(mods.NoMods);
        Assert.True(mods.SkipFailedMods);
        Assert.Equal([Path.Combine(_root, "extra")], mods.ExtraPackages);
        Assert.Contains("renderer=gpu", run.Describe());
    }

    [Fact]
    public void TheGameMustMatchTheInstall()
    {
        Install("install", Himegari, "patch", "patch2");
        string profile = Profile("profile", FullProfile);

        var error = Assert.Throws<RunResolutionException>(() => Resolve("--data-root", profile));

        Assert.Contains("is S4IC422 / 姫狩りダンジョンマイスター, not the game 'kamidori'", error.Message);
    }

    [Fact]
    public void AnUnknownGameStops()
    {
        Install("install", Kamidori);
        string profile = Profile("profile", """
            ome_profile = 1
            game = "nope"
            mode = "native"
            [install]
            root = "../install"
            """);

        var error = Assert.Throws<RunResolutionException>(() => Resolve("--data-root", profile));

        Assert.Contains("game: unknown game 'nope'; available games: ", error.Message);
        Assert.Contains("himegari, kamidori", error.Message);
    }

    [Fact]
    public void AnInstallWithoutACatalogStops()
    {
        Directory.CreateDirectory(Path.Combine(_root, "install"));
        string profile = Profile("profile", """
            ome_profile = 1
            game = "kamidori"
            mode = "native"
            [install]
            root = "../install"
            """);

        var error = Assert.Throws<RunResolutionException>(() => Resolve("--data-root", profile));

        Assert.Contains($"install.root: {Path.Combine(_root, "install")} holds no readable SYS4INI.BIN", error.Message);
    }

    [Fact]
    public void AChangedFingerprintWarnsAndContinues()
    {
        string install = Install("install", Kamidori);
        string actual = OmsPatchPlan.FingerprintOf(File.ReadAllBytes(Path.Combine(install, "SYS4INI.BIN")));
        string recorded = "sha256:" + new string('0', 64);
        string Toml(string fingerprint) => $"""
            ome_profile = 1
            game = "kamidori"
            mode = "native"
            [install]
            root = "../install"
            fingerprint = "{fingerprint}"
            """;

        ResolvedRun changed = Resolve("--data-root", Profile("changed", Toml(recorded)));
        ResolvedRun same = Resolve("--data-root", Profile("same", Toml(actual)));

        Assert.Equal([$"{changed.ProfileFile!.Path}: install.fingerprint {recorded} differs from the install's "
                      + $"SYS4INI.BIN ({actual}): its catalog changed since the profile accepted it"], changed.Warnings);
        Assert.Empty(same.Warnings);
    }

    [Fact]
    public void PathsResolveAgainstTheProfileRootAndOverlaysAgainstTheInstall()
    {
        string profile = Path.Combine(_root, "home", "profiles", "p");
        ProfileFile file = ProfileFile.Parse("""
            ome_profile = 1
            game = "kamidori"
            mode = "native"
            [install]
            root = "../../installs/k"
            overlays = ["patch", "patch/", "../shared-patch"]
            [mods]
            folders = ["../../mods/kamidori", "../../mods/kamidori/", "./../../mods/kamidori", "../../mods/common"]
            order = ["example.one", "-example.two"]
            """, profile);

        string install = Path.Combine(_root, "home", "installs", "k");
        Assert.Equal(install, file.Install.Root);
        Assert.Equal([Path.Combine(install, "patch"), Path.Combine(_root, "home", "installs", "shared-patch")],
                     file.Install.Overlays);
        Assert.Equal([Path.Combine(_root, "home", "mods", "kamidori"), Path.Combine(_root, "home", "mods", "common")],
                     file.Mods!.Folders);
        Assert.Equal([new ProfileModEntry("example.one", true), new ProfileModEntry("example.two", false)],
                     file.Mods.Order);
        Assert.Equal(["example.one"], file.Mods.Enabled);
        Assert.False(file.Mods.SkipFailed);
        Assert.Equal(new ProfileLaunch(null, null, null), file.Launch);
    }

    [Fact]
    public void AnAbsoluteInstallRootIsKept()
    {
        string install = Path.Combine(_root, "elsewhere", "install");
        ProfileFile file = ProfileFile.Parse($"""
            ome_profile = 1
            game = "himegari"
            mode = "native"
            [install]
            root = '{install}'
            """, Path.Combine(_root, "p"));

        Assert.Equal(install, file.Install.Root);
        Assert.Null(file.Mods);
    }

    [Theory]
    [InlineData("ome_profile = 2\ngame = \"kamidori\"",
                "ome_profile: format version 2 is not supported (this engine reads version 1)")]
    [InlineData("game = \"kamidori\"", "ome_profile: required")]
    [InlineData("ome_profile = \"1\"", "ome_profile: expected an integer")]
    [InlineData(Base + "colour = \"red\"", "colour: unknown key")]
    [InlineData(Base + "[arguments]\nextra = [\"--x\"]", "arguments: unknown key")]
    [InlineData(Base + "[environment]\nPATH = \"x\"", "environment: unknown key")]
    [InlineData(Base + "[install]\nroot = \"i\"\nexe = \"x.exe\"", "install.exe: unknown key")]
    [InlineData(Base + "[install]\nroot = \"i\"\nfingerprint = \"md5:1\"", "install.fingerprint: 'md5:1' is not a sha256")]
    [InlineData(Base + "[install]\nroot = \"i\"\nallow_bmp_as_agf = \"yes\"", "install.allow_bmp_as_agf: expected true or false")]
    [InlineData(Base + "[install]\nroot = \"i\"\n[launch]\nrenderer = \"vulkan\"", "launch.renderer: 'vulkan' is not one of \"gpu\", \"software\"")]
    [InlineData(Base + "[install]\nroot = \"i\"\n[launch]\ntext_backend = \"gdi+\"", "launch.text_backend: 'gdi+' is not one of")]
    [InlineData(Base + "[install]\nroot = \"i\"\n[launch]\nlua_errors = \"ignore\"", "launch.lua_errors: 'ignore' is not one of")]
    [InlineData(Base + "[install]\nroot = \"i\"\n[mods]\non_failure = \"retry\"", "mods.on_failure: 'retry' is not one of \"stop\", \"skip\"")]
    [InlineData(Base + "[install]\nroot = \"i\"\n[mods]\norder = [\"a.b\", \"-a.b\"]", "mods.order: 'a.b' is listed more than once")]
    [InlineData(Base + "[install]\nroot = \"i\"\n[mods]\norder = [\"Not An Id\"]", "mods.order: 'Not An Id' is not a mod id")]
    [InlineData("ome_profile = 1\ngame = \"kamidori\"\nmode = \"compat\"\n[install]\nroot = \"i\"", "mode: 'compat' is not a mode")]
    [InlineData("ome_profile = 1\ngame = \"kamidori\"\n[install]\nroot = \"i\"", "mode: required")]
    [InlineData("ome_profile = 1\ngame = \"kamidori\"\nmode = \"native\"", "install: required")]
    [InlineData(Base + "launcher = 3\n[install]\nroot = \"i\"", "launcher: expected a table")]
    [InlineData("ome_profile = 1\ngame = \"kamidori\"\nmode = \"native\"\n[install]\nroot = \"i\"\n[launch]\nrenderer = 1",
                "launch.renderer: expected a string")]
    [InlineData("ome_profile = 1\n[install", "not valid TOML")]
    public void AProblemStopsWithItsReason(string toml, string reason)
    {
        var error = Assert.Throws<ProfileFileException>(() => ProfileFile.Parse(toml, _root, "p/profile.toml"));

        Assert.StartsWith("p/profile.toml: ", error.Message);
        Assert.Contains(reason, error.Message);
    }

    private const string Base = "ome_profile = 1\ngame = \"kamidori\"\nmode = \"native\"\n";

    [Fact]
    public void AnUnreadableProfileFileStopsTheResolver()
    {
        string profile = Profile("profile", "ome_profile = 7");

        var error = Assert.Throws<RunResolutionException>(() => Resolve("--data-root", profile));

        Assert.Contains("format version 7 is not supported", error.Message);
    }

    [Fact]
    public void TheLauncherTableIsNotRead()
    {
        ProfileFile file = ProfileFile.Parse(Base + """
            [install]
            root = "i"
            [launcher]
            cover = 1
            [launcher.anything]
            deep = ["x", 2]
            """, _root);

        Assert.Equal("kamidori", file.Game);
    }
}
