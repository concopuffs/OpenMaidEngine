using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Localization;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

/// <summary>
/// The Advanced page's diagnostics summary (launcher spec §8.4, plan §6.8): what it holds of the profile, its checks,
/// its packages and link report (plan §10, item 8) and its log's end, and that its paths name only the profile's and the install's roots, with OME home and the
/// player's home folder written by name.
/// </summary>
public sealed class DiagnosticsSummaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-summary-" + Guid.NewGuid().ToString("N"));
    private readonly string _user;
    private readonly OmeHome _home;
    private readonly string _install;
    private const string Recorded = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Now = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public DiagnosticsSummaryTests()
    {
        _user = Path.Combine(_root, "Users", "ann");
        _home = new OmeHome(Path.Combine(_user, "AppData", "Roaming", "OpenMaidEngine"), OmeHomeKind.Explicit);
        _install = Path.Combine(_root, "Games", "Kamidori");
        Directory.CreateDirectory(_install);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ProfileEntry Profile()
    {
        ProfileDocument document = ProfileDocument.New("Kamidori (English)", "kamidori", _install, Recorded, _home);
        document = document with
        {
            Install = document.Install with { Overlays = [Path.Combine(_install, "patch")], AllowBmpAsAgf = true },
            Mods = document.Mods! with
            {
                Order = [new ProfileModEntry("example.alpha", true), new ProfileModEntry("example.bravo", false)],
                SkipFailed = true,
            },
            Launch = new ProfileLaunch("software", null, "halt"),
        };
        return ProfileStore.Create(_home, LauncherSettings.Defaults, "kamidori-english", document).Profile;
    }

    [Fact]
    public void TheSummaryHoldsTheProfileItsChecksAndItsLogsEnd()
    {
        ProfileEntry profile = Profile();
        string log = Path.Combine(profile.Root, "diagnostics", "engine.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        var lines = Enumerable.Range(1, EngineLaunch.TailLines + 5).Select(n => $"line {n}").ToList();
        lines.Add($"[mods] loaded {Path.Combine(_home.Root, "mods", "kamidori", "example.alpha")}");
        lines.Add($"opened {_install.Replace('\\', '/')}/patch/A.AGF");
        lines.Add($"user file {Path.Combine(_user, "Desktop", "shot.png")}");
        lines.Add($"another user {_user}a{Path.DirectorySeparatorChar}notes.txt");
        lines.Add($"its own log {log}");
        File.WriteAllLines(log, lines);

        var checks = new ProfileCheckResult(
            [
                new ProfileCheck(ProfileCheckKind.Install, CheckStatus.Passed, ""),
                new ProfileCheck(ProfileCheckKind.Packages, CheckStatus.Failed,
                                 $"example.alpha is not in {Path.Combine(_home.Root, "mods", "kamidori")}"),
            ],
            new InstallRecognition(_install, InstallKind.Supported, null, Fingerprint: Now), null, null);
        string summary = DiagnosticsSummary.Build(profile, checks, "1.2.3+abc", _home, _user);

        Assert.Contains("Launcher: 1.2.3+abc", summary);
        Assert.Contains("Profile: Kamidori (English) [kamidori-english] at "
                        + Path.Combine("~", "AppData", "Roaming", "OpenMaidEngine", "profiles", "kamidori-english"), summary);
        Assert.Contains($"Install: {_install}", summary);
        Assert.Contains($"  fingerprint {Recorded}, now {Now}", summary);
        Assert.Contains($"  overlays {Path.Combine("<install>", "patch")}, allow_bmp_as_agf = true", summary);
        Assert.Contains("Mode: native", summary);
        Assert.Contains("Mods: on_failure = skip", summary);
        Assert.Contains("  order example.alpha, -example.bravo", summary);
        Assert.Contains($"  folders {Path.Combine("<OME home>", "mods", "kamidori")}, {Path.Combine("<OME home>", "mods", "common")}",
                        summary);
        Assert.Contains("Launch: renderer = software, text_backend = (default), lua_errors = halt, graphics_api = default",
                        summary);
        Assert.Contains("  Install: Passed\n", summary);
        Assert.Contains($"  Packages: Failed - example.alpha is not in {Path.Combine("<OME home>", "mods", "kamidori")}", summary);

        Assert.Contains($"engine.log (the last {EngineLaunch.TailLines} lines):", summary);
        Assert.DoesNotContain("  line 10\n", summary);
        Assert.Contains("  line 45\n", summary);
        Assert.Contains($"  [mods] loaded {Path.Combine("<OME home>", "mods", "kamidori", "example.alpha")}", summary);
        Assert.Contains("  opened <install>/patch/A.AGF", summary);
        Assert.Contains($"  user file {Path.Combine("~", "Desktop", "shot.png")}", summary);
        Assert.Contains($"  its own log {Path.Combine("<profile>", "diagnostics", "engine.log")}", summary);
        // A folder whose name only begins with the home folder's is not the home folder.
        Assert.Contains($"  another user {_user}a", summary);

        // The account's folder is written nowhere: the one line that holds its text names the folder that only shares
        // its name's start. (The install sits outside the home folder here.)
        Assert.Equal(1, summary.Split('\n').Count(line => line.Contains(_user + Path.DirectorySeparatorChar)
                                                       || line.EndsWith(_user, StringComparison.Ordinal)
                                                       || line.Contains(_user + "a")));
    }

    [Fact]
    public void TheSummaryListsEachPackageWithWhatItDoesAndTheLinkReport()
    {
        ProfileEntry profile = Profile();
        string mods = Path.Combine(_home.Root, "mods", "kamidori");
        string folder = Path.Combine(mods, "example.alpha");
        Assert.True(SemanticVersion.TryParse("1.2.0", out SemanticVersion version));
        Assert.True(VersionRange.TryParse(">=1.0", out VersionRange range, out _));
        List<string> assets = Enumerable.Range(0, DiagnosticsSummary.NamesShown + 5).Select(n => $"A{n:D3}.AGF").ToList();
        var manifest = new ModManifest
        {
            Id = "example.alpha", Name = "Alpha", Version = version, Target = ModManifest.ExtendedTarget,
            ScriptNames = ["SALPHA"], AssetNames = assets, Languages = [new LanguageSettings("en")],
            Lua = new ModLuaSettings(1, [], [], []),
        };
        var alpha = new PackageIndexEntry(folder, "example.alpha", version, "kamidori", ModManifest.ExtendedTarget,
                                          [new ModRequirement("kamidori-base", range)], true, manifest);
        var library = new LibraryView(
            [
                new LibraryPackage("example.alpha", alpha, true, true, PackageState.NeedsExtended,
                                   ["Compatibility mode refuses it: it targets extended"], []),
                new LibraryPackage("example.bravo", null, false, true, PackageState.Missing, ["no folder holds it"], []),
            ],
            [new PackageIndexNote("broken.omp", $"{Path.Combine(mods, "broken.omp")} does not read")]);
        var report = new ModLinkReport();
        report.Add(ModReportClass.Note, "example.alpha", "TITLE.BIN", $"compiled {Path.Combine(folder, "patches", "a.oms")}");
        report.Add(ModReportClass.HardConflict, ["example.alpha", "example.charlie"], "SC0001.BIN", "both replace it");
        var checks = new ProfileCheckResult([], new InstallRecognition(_install, InstallKind.Supported, null, Fingerprint: Recorded),
                                            library, new LinkCheck("", new ModLinkReport(), report, [], [], []));
        string summary = DiagnosticsSummary.Build(profile, checks, "dev", _home, _user);

        // Each package, as its "What it does" card says of it, with the folder written from OME home.
        Assert.Contains("Packages (the profile's order, then those it does not list):\n", summary);
        Assert.Contains("  example.alpha 1.2.0 \"Alpha\" - on, NeedsExtended\n", summary);
        Assert.Contains($"    at {Path.Combine("<OME home>", "mods", "kamidori", "example.alpha")}\n", summary);
        Assert.Contains("    target extended; requires kamidori-base >=1.0\n", summary);
        Assert.Contains("    supplies the scripts SALPHA\n", summary);
        Assert.Contains("    supplies the assets " + string.Join(", ", assets.Take(DiagnosticsSummary.NamesShown))
                        + $", ... ({assets.Count} in all)\n", summary);
        Assert.Contains("    defines the languages en\n", summary);
        Assert.Contains("    runs Lua\n", summary);
        Assert.Contains("    reason: Compatibility mode refuses it: it targets extended\n", summary);
        Assert.Contains("  example.bravo - off, Missing\n    reason: no folder holds it\n", summary);
        Assert.Contains($"  not read: broken.omp: {Path.Combine("<OME home>", "mods", "kamidori", "broken.omp")} does not read\n",
                        summary);

        // The link report in mod-link.txt's form: its classes from errors down, paths written from their roots.
        Assert.Contains("Link report (the checks' link, as diagnostics/mod-link.txt holds a start's):\n  load order: (no mods)\n",
                        summary);
        int conflict = summary.IndexOf("  conflict: [example.alpha, example.charlie] SC0001.BIN: both replace it\n",
                                       StringComparison.Ordinal);
        int note = summary.IndexOf("  note: [example.alpha] TITLE.BIN: compiled "
                                   + Path.Combine("<OME home>", "mods", "kamidori", "example.alpha", "patches", "a.oms") + "\n",
                                   StringComparison.Ordinal);
        Assert.True(conflict >= 0 && note > conflict, summary);
        Assert.True(summary.IndexOf("Packages (", StringComparison.Ordinal) < conflict
                    && note < summary.IndexOf("engine.log", StringComparison.Ordinal), summary);
        Assert.DoesNotContain(_user + Path.DirectorySeparatorChar, summary);
    }

    [Fact]
    public void AProfileThatHasNotRunSaysSo()
    {
        ProfileEntry profile = Profile();
        string summary = DiagnosticsSummary.Build(profile, null, "dev", _home, _user);
        Assert.Contains("Checks: not run", summary);
        Assert.Contains("Packages: not listed (the checks have not run on a usable profile)", summary);
        Assert.Contains("Link report: none (the link did not run; the checks say why)", summary);
        Assert.Contains("engine.log: none yet", summary);
        Assert.Null(DiagnosticsSummary.LogTail(profile.Root));
    }
}
