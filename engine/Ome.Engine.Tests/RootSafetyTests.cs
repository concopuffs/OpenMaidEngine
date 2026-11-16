using System.Diagnostics;
using System.Text;
using Ome.Engine.Profiles;
using Ome.Mods.Launch;

/// <summary>Launcher spec §5.2 and §12 (part of gate 14): a profile root holds no link, and a network path its file
/// names is opened only once this machine's player approved it. Links are junctions on Windows (no privilege needed)
/// and symbolic links elsewhere. No test opens a network path.</summary>
public sealed class RootSafetyTests : IDisposable
{
    private const string Host = @"\\ome-test-host.invalid\share";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-root-safety-tests", Guid.NewGuid().ToString("N"));

    public RootSafetyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        // Remove links first, so the delete never walks into a target.
        foreach (string entry in Directory.EnumerateFileSystemEntries(_root, "*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = 0 }).OrderByDescending(path => path.Length))
        {
            var info = new FileInfo(entry);
            if (info.LinkTarget == null) continue;
            if (Directory.Exists(entry) || (info.Attributes & FileAttributes.Directory) != 0) Directory.Delete(entry);
            else File.Delete(entry);
        }
        Directory.Delete(_root, recursive: true);
    }

    private static void DirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private string Install(GameCatalogIdentity identity)
    {
        string install = Path.Combine(_root, "install");
        Directory.CreateDirectory(install);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var header = new byte[264];
        Encoding.ASCII.GetBytes(identity.CatalogRevision.PadRight(8)).CopyTo(header, 0);
        Encoding.GetEncoding(932).GetBytes(identity.Title).CopyTo(header, 8);
        File.WriteAllBytes(Path.Combine(install, "SYS4INI.BIN"), header);
        return install;
    }

    private string Profile(string toml, string name = "profile")
    {
        string profile = Path.Combine(_root, name);
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, ProfileFile.FileName), toml);
        return profile;
    }

    private const string Local = """
        ome_profile = 1
        game = "himegari"
        mode = "native"
        [install]
        root = "../install"
        """;

    private static string OnShare(string share) => $"""
        ome_profile = 1
        game = "himegari"
        mode = "native"
        [install]
        root = '{share}\install'
        overlays = ['{share}\patch']
        [mods]
        folders = ['{share}\mods', "mods"]
        """;

    private string MachineFolder(string name = "machine")
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Theory]
    [InlineData(@"\\host\share\x", true, true)]
    [InlineData("//host/share/x", true, true)]
    [InlineData(@"\\?\UNC\host\share\x", true, true)]
    [InlineData(@"\\.\UNC\host\share", true, true)]
    [InlineData(@"\\?\C:\Games", true, false)]
    [InlineData(@"\\.\C:\Games", true, false)]
    [InlineData(@"C:\Games", true, false)]
    [InlineData(@"Z:\mapped\share", true, false)]
    [InlineData("relative", true, false)]
    [InlineData(@"\\host\share\x", false, false)]
    [InlineData("//host/share", false, false)]
    [InlineData("/mnt/nas/games", false, false)]
    public void OnlyAWindowsNetworkPathIsOne(string path, bool windows, bool network)
        => Assert.Equal(network, RootSafety.IsNetworkPath(path, windows));

    [Fact]
    public void ALinkAnywhereInsideTheRootIsFound()
    {
        string root = Path.Combine(_root, "walk");
        Directory.CreateDirectory(Path.Combine(root, "SAVE", "deep"));
        File.WriteAllText(Path.Combine(root, "SAVE", "deep", "SAVE01.DAT"), "x");
        Assert.Null(RootSafety.FindLink(root));

        string target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(target);
        string link = Path.Combine(root, "SAVE", "deep", "redirect");
        DirectoryLink(link, target);
        Assert.Equal(link, RootSafety.FindLink(root));
    }

    [Fact]
    public void AHiddenLinkIsFoundToo()
    {
        string root = Path.Combine(_root, "hidden");
        Directory.CreateDirectory(root);
        string target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(target);
        string link = Path.Combine(root, OperatingSystem.IsWindows() ? "mods" : ".mods");
        DirectoryLink(link, target);
        if (OperatingSystem.IsWindows())
            new DirectoryInfo(link).Attributes |= FileAttributes.Hidden | FileAttributes.System;

        Assert.Equal(link, RootSafety.FindLink(root));
    }

    [Fact]
    public void AFileLinkIsFoundOnLinux()
    {
        if (OperatingSystem.IsWindows()) return;   // a file link needs a privilege on Windows; junctions cover it there
        string root = Path.Combine(_root, "file-link");
        Directory.CreateDirectory(root);
        string link = Path.Combine(root, "OME.INI");
        File.CreateSymbolicLink(link, Path.Combine(_root, "victim.txt"));

        Assert.Equal(link, RootSafety.FindLink(root));
    }

    [Fact]
    public void AProfileRootWithALinkStopsBeforeItsFileIsRead()
    {
        string profile = Profile("this is [not TOML");
        string target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(target);
        DirectoryLink(Path.Combine(profile, "SAVE"), target);

        var error = Assert.Throws<RunResolutionException>(
            () => RunResolver.Resolve(["--data-root", profile], _root, machineFolder: MachineFolder()));

        Assert.Contains($"{Path.Combine(profile, "SAVE")} is a symbolic link or junction", error.Message);
    }

    [Fact]
    public void ARootWithoutAProfileFileIsNotChecked()
    {
        string root = Path.Combine(_root, "sandbox");
        Directory.CreateDirectory(root);
        string target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(target);
        DirectoryLink(Path.Combine(root, "SAVE"), target);

        ResolvedRun run = RunResolver.Resolve(["--data-root", root], _root, machineFolder: MachineFolder());

        Assert.Null(run.ProfileFile);
    }

    [Fact]
    public void ALocalProfileNeedsNoApproval()
    {
        Install(new GameCatalogIdentity("S4IC422", "姫狩りダンジョンマイスター"));
        string profile = Profile(Local);
        string machine = MachineFolder();

        ResolvedRun run = RunResolver.Resolve(["--data-root", profile], _root, machineFolder: machine);

        Assert.NotNull(run.ProfileFile);
        Assert.False(File.Exists(Path.Combine(machine, MachineState.FileName)));
    }

    [Fact]
    public void ANetworkPathThePlayerDidNotApproveStopsBeforeItIsOpened()
    {
        if (!OperatingSystem.IsWindows()) return;   // network paths are a Windows notion (spec §12)
        string profile = Profile(OnShare(Host));

        var error = Assert.Throws<RunResolutionException>(
            () => RunResolver.Resolve(["--data-root", profile], _root, machineFolder: MachineFolder()));

        // The install is read after this check, so its message proves nothing on the share was opened.
        Assert.Contains($@"install.root names the network path {Host}\install", error.Message);
        Assert.Contains($@"install.overlays names the network path {Host}\patch", error.Message);
        Assert.Contains($@"mods.folders names the network path {Host}\mods", error.Message);
        Assert.Contains("--allow-network-paths for this run", error.Message);
        Assert.DoesNotContain(Path.Combine(profile, "mods"), error.Message);
    }

    [Fact]
    public void AnApprovedLocationAndTheRunFlagLetThePathsThrough()
    {
        if (!OperatingSystem.IsWindows()) return;
        string profile = Profile(OnShare(Host));
        ProfileFile file = ProfileFile.TryRead(profile)!;
        string folder = MachineFolder();
        MachineState Machine() => MachineState.Load(folder);

        Assert.Throws<RunResolutionException>(() => RootSafety.RequireApproved(file, [], Machine, windows: true));
        RootSafety.RequireApproved(file, ["--allow-network-paths"], Machine, windows: true);

        MachineState.Load(folder).Approve(Host);
        Assert.Empty(RootSafety.UnapprovedNetworkPaths(file, Machine, windows: true));
        RootSafety.RequireApproved(file, [], Machine, windows: true);
        Assert.Equal([Host], MachineState.Load(folder).ApprovedNetworkLocations);
    }

    [Fact]
    public void ALocationIsApprovedWithWhatLiesUnderIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        MachineState state = MachineState.Load(MachineFolder()).Approve(Host + @"\games");

        Assert.True(state.IsApproved(Host + @"\games"));
        Assert.True(state.IsApproved(Host + @"\GAMES\kamidori\patch"));
        Assert.False(state.IsApproved(Host + @"\games-other"));
        Assert.False(state.IsApproved(Host));
        Assert.False(state.IsApproved(@"\\ome-other-host.invalid\share\games"));
    }

    [Fact]
    public void AProfileRootOrAnOmeHomeFromElsewhereBringsNoApproval()
    {
        if (!OperatingSystem.IsWindows()) return;
        string home = Path.Combine(_root, "OpenMaidEngine");
        string profile = Path.Combine(home, "profiles", "received");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, ProfileFile.FileName), OnShare(Host));
        string approval = $"ome_machine = 1\n[network]\napproved = ['{Host}']\n";
        File.WriteAllText(Path.Combine(profile, MachineState.FileName), approval);
        File.WriteAllText(Path.Combine(home, MachineState.FileName), approval);

        var error = Assert.Throws<RunResolutionException>(
            () => RunResolver.Resolve(["--data-root", profile], _root, machineFolder: MachineFolder()));

        Assert.Contains("which this machine's player has not approved", error.Message);
    }

    [Fact]
    public void MachineStateRoundTripsAndIsStrict()
    {
        string folder = MachineFolder();
        Assert.Empty(MachineState.Load(folder).ApprovedNetworkLocations);
        MachineState.Load(folder).Approve(Path.Combine(_root, "a")).Approve(Path.Combine(_root, "a")).Approve(Path.Combine(_root, "b"));
        Assert.Equal([Path.Combine(_root, "a"), Path.Combine(_root, "b")], MachineState.Load(folder).ApprovedNetworkLocations);

        File.WriteAllText(Path.Combine(folder, MachineState.FileName), "ome_machine = 2\n");
        Assert.Contains("format version 2 is not supported",
                        Assert.Throws<InvalidDataException>(() => MachineState.Load(folder)).Message);
        File.WriteAllText(Path.Combine(folder, MachineState.FileName), "ome_machine = 1\nextra = 1\n");
        Assert.Contains("extra: unknown key", Assert.Throws<InvalidDataException>(() => MachineState.Load(folder)).Message);
    }

    [Fact]
    public void TheDefaultFolderFollowsGodotsRule()
    {
        string folder = MachineState.DefaultFolder();
        if (OperatingSystem.IsWindows())
            Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("APPDATA") is { Length: > 0 } appData
                                          && Path.IsPathRooted(appData)
                                          ? appData
                                          : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      "Godot", "app_userdata", "Open Maid Engine"), folder);
        else
            Assert.EndsWith(Path.Combine("godot", "app_userdata", "Open Maid Engine"), folder);
    }

    [Fact]
    public void OnWindowsTheDefaultFolderFollowsTheAppDataVariableAsGodotDoes()
    {
        // validate.py's runtime level sets APPDATA for the Godot process; Godot's user folder moves with it.
        string isolated = Path.Combine(Path.GetTempPath(), "isolated-appdata");
        Assert.Equal(Path.Combine(isolated, "Godot", "app_userdata", "Open Maid Engine"),
                     MachineState.DefaultFolder(name => name == "APPDATA" ? isolated : null, windows: true));
        foreach (string? unusable in new[] { null, "", "relative" })
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      "Godot", "app_userdata", "Open Maid Engine"),
                         MachineState.DefaultFolder(name => name == "APPDATA" ? unusable : null, windows: true));
    }
}
