using Ome.Engine.Profiles;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

/// <summary>
/// Launcher spec §8.5 (launcher plan §5.9, gate 8): the checks of a profile. The link check's report equals
/// <c>omp check</c>'s for the same profile, run as a process, and no check writes a file. On a synthetic Kamidori
/// install, as gate 6 builds it.
/// </summary>
public sealed class ProfileChecksTests : IDisposable
{
    private const string KamidoriRevision = "S4IC433 ", KamidoriTitle = "神採りアルケミーマイスター";
    private static readonly CheckOptions Options = new(MachineRegistry: (_, _) => null);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-checks-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;
    private readonly string _install;
    private readonly string _fingerprint;

    public ProfileChecksTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_install);
        File.WriteAllBytes(Path.Combine(_install, "SYS4INI.BIN"), Sys4StartupSettingsTests.BuildCatalog(
            [.. SharedLinkBaseTests.ScriptsTheModulesRun("kamidori"), "GATESIX.BIN"], KamidoriRevision, KamidoriTitle));
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
        _fingerprint = Installs.Recognize(_install, GameProfileRegistry.BuiltIn).Fingerprint!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A Kamidori mod whose one new script runs <paramref name="runs"/>.</summary>
    private void Package(string id, string runs, string target = "native")
    {
        string folder = Path.Combine(_home.ModsDirectory("kamidori"), id);
        string script = "S" + new string(id.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        Directory.CreateDirectory(Path.Combine(folder, "scripts"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = {(target == "native" ? 1 : 3)}\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n[scripts]\nnames = [\"{script}\"]\n"
            + $"[game]\nprofile = \"kamidori\"\ntarget = \"{target}\"\n");
        File.WriteAllText(Path.Combine(folder, "scripts", script + ".oms"),
            $"oms 1\nscript \"{script}\"\nuse kamidori.scripts\n\n    run {runs}\n    exit\n");
    }

    private ProfileEntry Profile(string id, Func<ProfileDocument, ProfileDocument> change)
        => ProfileStore.Create(_home, LauncherSettings.Defaults, id,
                               change(ProfileDocument.New("Gate 8", "kamidori", _install, _fingerprint, _home))).Profile;

    private static ProfileModEntry On(string id) => new(id, true);

    /// <summary>Every file and folder under the test root, with each file's size and last-write time.</summary>
    private List<string> Snapshot()
        => Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => File.Exists(path)
                ? $"{path} {new FileInfo(path).Length} {File.GetLastWriteTimeUtc(path):O}"
                : path)
            .ToList();

    [Fact]
    public void TheLinkCheckReportsWhatOmpCheckPrintsAndNoCheckWritesAFile()
    {
        Package("gate.one", "GATESIX");
        ProfileEntry profile = Profile("gate", document => document with
        {
            Mods = document.Mods! with { Order = [On("gate.one")] },
        });

        List<string> before = Snapshot();
        ProfileCheckResult result = ProfileChecks.Run(profile, Options);
        Assert.Equal(before, Snapshot());
        Assert.All(result.Checks, check => Assert.True(check.Status == CheckStatus.Passed, check.ToString()));
        Assert.True(result.CanPlay);
        Assert.False(result.Running);

        var cli = OmeCliProcess.Run(_root, "omp", "check", RunResolver.DataRootOption, profile.Root);
        Assert.True(cli.Exit == 0, $"exit {cli.Exit}\n{cli.Output}\n{cli.Error}");
        Assert.Equal(cli.Output.ReplaceLineEndings("\n"), result.Link!.Text.ReplaceLineEndings("\n"));
        Assert.Contains("load order: gate.one", result.Link.Text);
    }

    [Fact]
    public void TheChecksNameWhatStopsPlayAndWhatASkippedLinkDrops()
    {
        Package("gate.one", "GATESIX");
        Package("gate.bad", "NOSUCHSCRIPT");
        Package("gate.ext", "GATESIX", target: "extended");
        ProfileEntry profile = Profile("states", document => document with
        {
            Install = document.Install with { Fingerprint = "sha256:" + new string('0', 64) },
            Mods = document.Mods! with
            {
                Order = [On("gate.one"), On("gate.bad"), On("gate.ext"), On("gate.gone")],
                SkipFailed = true,
            },
        });

        ProfileCheckResult result;
        Assert.True(RunLock.TryAcquire(profile.Root, markStart: true, out RunLock? running, out _));
        using (running) result = ProfileChecks.Run(profile, Options);
        Assert.False(result.CanPlay);
        Assert.True(result.Running);
        Assert.Equal(CheckStatus.Passed, result[ProfileCheckKind.Install].Status);
        Assert.Equal(CheckStatus.Warning, result[ProfileCheckKind.Fingerprint].Status);
        Assert.Equal(CheckStatus.Passed, result[ProfileCheckKind.Root].Status);
        ProfileCheck packages = result[ProfileCheckKind.Packages];
        Assert.Equal(CheckStatus.Failed, packages.Status);
        Assert.Contains("gate.ext: Compatibility mode refuses it", packages.Message);
        Assert.Contains("gate.gone: the profile lists it", packages.Message);
        Assert.DoesNotContain("gate.bad", packages.Message);
        ProfileCheck link = result[ProfileCheckKind.Link];
        Assert.True(link.Status == CheckStatus.Warning, link + "\n" + result.Link!.Text);
        Assert.Equal(["gate.bad", "gate.ext", "gate.gone"], result.Link!.Dropped);
        Assert.Equal(["gate.one"], result.Link.Order);

        // "Accept the install as it is now" records the installed fingerprint.
        ProfileChecks.AcceptInstall(profile, result.Install.Fingerprint!, _home);
        Assert.Equal(_fingerprint, ProfileDocuments.Read(profile.Root)!.Document.Install.Fingerprint);
    }

    [Fact]
    public void AMissingInstallStopsPlayAndARootWithoutALockFileIsProbedWithoutAFileLeft()
    {
        ProfileEntry profile = Profile("moved", document => document with
        {
            Install = document.Install with { Root = Path.Combine(_root, "nowhere") },
        });
        File.Delete(Path.Combine(profile.Root, RunLock.FileName));
        List<string> before = Snapshot();
        ProfileCheckResult result = ProfileChecks.Run(profile, Options);
        Assert.Equal(before, Snapshot());
        Assert.False(result.CanPlay);
        Assert.False(result.Running);
        Assert.Equal(CheckStatus.Failed, result[ProfileCheckKind.Install].Status);
        Assert.EndsWith("holds no SYS4INI.BIN; locate the install", result[ProfileCheckKind.Install].Message);
        Assert.Equal(CheckStatus.NotRun, result[ProfileCheckKind.Fingerprint].Status);
        Assert.Equal(CheckStatus.NotRun, result[ProfileCheckKind.Link].Status);
        Assert.Equal(CheckStatus.Passed, result[ProfileCheckKind.Root].Status);
        Assert.Equal(CheckStatus.Passed, result[ProfileCheckKind.Lock].Status);
    }
}
