using Ome.Engine.Frontend.Shell;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Launcher spec §8.2 and §5.9 (launcher plan §5.4; gate 7's recognition and overlay cases): what a folder is,
/// its overlay candidates, the known installs, and the game version by the engine's rule. On synthetic catalogs; no
/// registry is read.</summary>
public sealed class InstallsTests : IDisposable
{
    private const string Guid = "70724B91-F673-40FB-9EC4-5DE21EA68367";
    private static readonly MachineRegistryReader NoRegistry = (_, _) => null;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-installs-" + System.Guid.NewGuid().ToString("N"));

    public InstallsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Install(string name, string magic, string title, string[] files, params (string, string)[] settings)
    {
        string root = Path.Combine(_root, name);
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, Installs.CatalogFile),
                           Sys4StartupSettingsTests.BuildCatalog(files, settings, magic, title));
        return root;
    }

    private static GameCatalogIdentity Kamidori => GameProfileRegistry.BuiltIn.Find("kamidori")!.CatalogIdentities[0];

    [Fact]
    public void ASupportedGameShowsItsTitlesVersionAndFingerprint()
    {
        string root = Install("kamidori", Kamidori.CatalogRevision.PadRight(8), Kamidori.Title, ["SC0000.BIN"],
                              ("GAMEVERSION", "1.00"), ("VERREGPOS", Guid));
        string? asked = null;
        InstallRecognition found = Installs.Recognize(root, GameProfileRegistry.BuiltIn, (key, name) =>
        {
            asked = $"{key}|{name}";
            return "1.03.0008";
        });
        Assert.Equal(InstallKind.Supported, found.Kind);
        Assert.Null(found.Problem);
        Assert.Equal("kamidori", found.Game!.Id);
        Assert.Equal(["Kamidori Alchemy Meister", "神採りアルケミーマイスター"], found.Titles);
        Assert.Equal("1.03.0008", found.GameVersion);
        Assert.Equal($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\InstallShield_{{{Guid}}}|DisplayVersion", asked);
        Assert.Equal(Ome.Script.Patch.OmsPatchPlan.FingerprintOf(File.ReadAllBytes(Path.Combine(root, Installs.CatalogFile))),
                     found.Fingerprint);
        Assert.Matches("^sha256:[0-9a-f]{64}$", found.Fingerprint!);
    }

    [Fact]
    public void AnUnsupportedAgeGameShowsItsRevisionAndTitle()
    {
        string root = Install("other", "S4IC499 ", "別のゲーム", ["SC0000.BIN"]);
        InstallRecognition found = Installs.Recognize(root, GameProfileRegistry.BuiltIn, NoRegistry);
        Assert.Equal(InstallKind.Unsupported, found.Kind);
        Assert.Equal("S4IC499", found.Identity!.CatalogRevision);
        Assert.Equal("別のゲーム", found.Identity.Title);
        Assert.Contains("does not support", found.Problem);
        Assert.Null(found.Game);
        Assert.Empty(found.Titles);

        // The right revision under another title is another game too.
        string renamed = Install("renamed", Kamidori.CatalogRevision.PadRight(8), "Kamidori", ["SC0000.BIN"]);
        Assert.Equal(InstallKind.Unsupported, Installs.Recognize(renamed, GameProfileRegistry.BuiltIn, NoRegistry).Kind);
    }

    [Fact]
    public void AFolderWithoutAnAgeCatalogIsNotAnInstall()
    {
        Assert.Contains("holds no SYS4INI.BIN",
                        Installs.Recognize(_root, GameProfileRegistry.BuiltIn, NoRegistry).Problem);
        string truncated = Path.Combine(_root, "truncated");
        Directory.CreateDirectory(truncated);
        File.WriteAllBytes(Path.Combine(truncated, Installs.CatalogFile), "S4IC433 "u8.ToArray());
        Assert.Equal(InstallKind.NotAnInstall, Installs.Recognize(truncated, GameProfileRegistry.BuiltIn, NoRegistry).Kind);
        string append = Install("append", "S4AC422 ", "Synthetic", ["SC0000.BIN"]);
        InstallRecognition found = Installs.Recognize(append, GameProfileRegistry.BuiltIn, NoRegistry);
        Assert.Equal(InstallKind.NotAnInstall, found.Kind);
        Assert.Contains("is not an AGE catalog", found.Problem);
    }

    [Fact]
    public void OverlayCandidatesAreSubfoldersOfListedNames()
    {
        string root = Install("kamidori", Kamidori.CatalogRevision.PadRight(8), Kamidori.Title,
                              ["SC0000.BIN", "SC0001.BIN", "BG001.AGF", "SE01.OGG"]);
        void Write(string file, byte[] bytes)
        {
            string path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        Write("english/SC0000.BIN", [1]);
        Write("english/sc0001.bin", [1]);
        Write("english/BG001.AGF", "BM\0\0"u8.ToArray());
        Write("english/readme.txt", [1]);
        Write("english/nested/SE01.OGG", [1]);
        Write("voice/SE01.OGG", [1]);
        Write("voice/BG001.agf", "ACGF"u8.ToArray());
        Write("SAVE/SAVE001.SAV", [1]);

        InstallRecognition found = Installs.Recognize(root, GameProfileRegistry.BuiltIn, NoRegistry);
        IReadOnlyList<OverlayCandidate> candidates = Installs.OverlayCandidates(root, found.Catalog!);
        Assert.Equal(
            [
                new OverlayCandidate(Path.Combine(root, "english"), 3, BmpAsAgf: true),
                new OverlayCandidate(Path.Combine(root, "voice"), 2, BmpAsAgf: false),
            ],
            candidates);
    }

    [Fact]
    public void KnownInstallsAreTheProfilesInstallRoots()
    {
        var home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
        string install = Path.Combine(_root, "games", "kamidori");
        LauncherSettings settings = LauncherSettings.Defaults;
        foreach (string id in new[] { "first", "second" })
            settings = ProfileStore.Create(home, settings, id, ProfileDocument.New(null, "kamidori", install, null)).Settings;
        settings = ProfileStore.Create(home, settings, "hime",
                                       ProfileDocument.New(null, "himegari", Path.Combine(_root, "games", "hime"), null)).Settings;
        IReadOnlyList<ProfileEntry> profiles = ProfileStore.List(home, settings, () => throw new InvalidOperationException());
        IReadOnlyList<KnownInstall> known = Installs.Known(profiles);
        Assert.Equal(2, known.Count);
        Assert.Equal(["first", "second"], known.Single(entry => entry.Game == "kamidori").Profiles);
        Assert.Equal(install, known.Single(entry => entry.Game == "kamidori").Root);
    }

    [Fact]
    public void TheGameVersionRuleIsTheEngines()
    {
        Sys4StartupSettings plain = Sys4StartupSettings.FromPairs([new("GAMEVERSION", "1.00")]);
        Assert.Equal("1.00", GameVersionRule.Of(plain, (_, _) => throw new InvalidOperationException("read")));
        Sys4StartupSettings installer = Sys4StartupSettings.FromPairs([new("GAMEVERSION", "1.00"), new("VERREGPOS", Guid)]);
        Assert.Equal(GameVersionRule.InstallerDefault, GameVersionRule.Of(installer, NoRegistry));
        Assert.Equal("1.03.0008", GameVersionRule.Of(installer, (_, _) => "1.03.0008"));
        Assert.Equal("", GameVersionRule.Of(Sys4StartupSettings.Empty, NoRegistry));
        if (!OperatingSystem.IsWindows()) Assert.Null(GameVersionRule.ReadMachine32(GameVersionRule.UninstallKey(Guid), "x"));
    }
}
