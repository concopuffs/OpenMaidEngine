using Ome.Launcher.Core;
using Ome.Mods.Launch;

/// <summary>
/// The Advanced page's diagnostics summary (launcher spec §8.4, plan §6.8): what it holds of the profile, its checks
/// and its log's end, and that its paths name only the profile's and the install's roots, with OME home and the
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
    public void AProfileThatHasNotRunSaysSo()
    {
        ProfileEntry profile = Profile();
        string summary = DiagnosticsSummary.Build(profile, null, "dev", _home, _user);
        Assert.Contains("Checks: not run", summary);
        Assert.Contains("engine.log: none yet", summary);
        Assert.Null(DiagnosticsSummary.LogTail(profile.Root));
    }
}
