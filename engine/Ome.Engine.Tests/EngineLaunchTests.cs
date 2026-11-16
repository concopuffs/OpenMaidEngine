using System.Text.Json;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;

/// <summary>
/// Launcher spec §8.5 (launcher plan §5.11): the one builder of the engine command, the Shortcut dialog's two forms,
/// and the launch, with a stub in the engine's place on gate 6's synthetic Kamidori install. Gate 9 runs the same
/// through the launcher's command line (§5.12).
/// </summary>
public sealed class EngineLaunchTests : IDisposable
{
    private const string KamidoriRevision = "S4IC433 ", KamidoriTitle = "神採りアルケミーマイスター";
    private static readonly CheckOptions Options = new(MachineRegistry: (_, _) => null);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-launch-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;
    private readonly string _install;

    public EngineLaunchTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_install);
        File.WriteAllBytes(Path.Combine(_install, "SYS4INI.BIN"), Sys4StartupSettingsTests.BuildCatalog(
            SharedLinkBaseTests.ScriptsTheModulesRun("kamidori"), KamidoriRevision, KamidoriTitle));
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>The stub engine's program, built beside this assembly in the same configuration.</summary>
    private static string Stub()
    {
        string relative = Path.GetRelativePath(Path.Combine(Paths.Repo, "engine", "Ome.Engine.Tests"), AppContext.BaseDirectory);
        string path = Path.GetFullPath(Path.Combine(Paths.Repo, "engine", "Ome.Launcher.StubEngine", relative,
                                                    "Ome.Launcher.StubEngine" + (OperatingSystem.IsWindows() ? ".exe" : "")));
        Assert.True(File.Exists(path), $"the stub engine is not built at {path}");
        return path;
    }

    private ProfileEntry Profile(Func<ProfileDocument, ProfileDocument>? change = null)
    {
        string fingerprint = Installs.Recognize(_install, GameProfileRegistry.BuiltIn).Fingerprint!;
        ProfileDocument document = ProfileDocument.New("Launch", "kamidori", _install, fingerprint, _home);
        return ProfileStore.Create(_home, LauncherSettings.Defaults, "launch", change?.Invoke(document) ?? document).Profile;
    }

    [Fact]
    public void TheCommandPutsGodotsFlagsBeforeTheDashDashAndEveryPathIsAbsolute()
    {
        ProfileEntry profile = Profile(document => document with
        {
            Launcher = document.Launcher with { GraphicsApi = "d3d12" },
        });
        EngineProgram engine = EngineProgram.Development(Path.Combine("tools", "godot.exe"), ["--path", "/repo/godot"], _root);
        string tools = Path.Combine(_root, "tools");

        EngineCommand windows = EngineCommands.For(engine, profile, ["--windowed"], OmePlatform.Windows);
        Assert.Equal(Path.Combine(tools, "godot.exe"), windows.Program);
        Assert.Equal(["--path", "/repo/godot", "--rendering-driver", "d3d12", "--", "--data-root", profile.Root, "--windowed"],
                     windows.Arguments);
        Assert.Equal(tools, windows.WorkingDirectory);
        Assert.Empty(windows.Environment);

        // Direct3D 12 is a Windows flag; on Linux the engine reads PWD, so it is set to the working folder.
        EngineCommand linux = EngineCommands.For(engine, profile, [], OmePlatform.Linux);
        Assert.Equal(["--path", "/repo/godot", "--", "--data-root", profile.Root], linux.Arguments);
        Assert.Equal(tools, linux.Environment["PWD"]);

        using (JsonDocument json = JsonDocument.Parse(windows.ToJson()))
        {
            Assert.Equal(windows.Program, json.RootElement.GetProperty("program").GetString());
            Assert.Equal(windows.Arguments, json.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(tools, json.RootElement.GetProperty("working_directory").GetString());
        }

        Assert.Throws<ArgumentException>(() => EngineProgram.Development("godot", ["--path", "x", "--"], _root));
        Assert.Throws<ArgumentException>(() => EngineCommands.For(engine, profile, ["--data-root", _root], OmePlatform.Windows));
        Assert.Equal(Path.Combine(_root, "OME.exe"), EngineProgram.BesideLauncher(_root, OmePlatform.Windows).Program);
        Assert.Equal(Path.Combine(_root, "OME"), EngineProgram.BesideLauncher(_root, OmePlatform.Linux).Program);
    }

    [Fact]
    public void TheShortcutsFirstFormNamesAHomeOnlyWhenOneWasNamedAndItsSecondIsTheEngineCommand()
    {
        ProfileEntry profile = Profile();
        EngineProgram engine = EngineProgram.BesideLauncher(_root, OmePlatform.Windows);
        string launcher = Path.Combine(_root, "OME-Launcher.exe");
        ShortcutForms forms = EngineCommands.Shortcut(launcher, _home, profile, engine, OmePlatform.Windows);
        Assert.Equal(launcher, forms.ThroughLauncher.Program);
        Assert.Equal(["--launch", "launch", "--home", _home.Root], forms.ThroughLauncher.Arguments);
        Assert.Equal(EngineCommands.For(engine, profile, [], OmePlatform.Windows).ToJson(), forms.Direct.ToJson());
        Assert.Equal(["--launch", "launch"], EngineCommands.Shortcut(launcher, _home with { Kind = OmeHomeKind.Portable },
                                                                     profile, engine, OmePlatform.Windows).ThroughLauncher.Arguments);
    }

    [Fact]
    public void ALaunchCapturesBothStreamsAndAFailedStartRunsTheChecksAgain()
    {
        ProfileEntry profile = Profile();
        var stub = new EngineProgram(Stub(), []);
        string working = Path.GetDirectoryName(stub.Program)!;

        LaunchResult started = EngineLaunch.Run(profile, EngineCommands.For(stub, profile, ["--windowed"]), Options);
        Assert.True(started is { Started: true, Failed: false }, started.Refusal);
        string log = File.ReadAllText(started.LogPath!);
        Assert.Equal(Path.Combine(profile.Root, "diagnostics", "engine.log"), started.LogPath);
        Assert.Contains($"stub cwd: {working}", log);
        Assert.Contains($"stub out: {profile.Root}", log);
        Assert.Contains("stub err: --windowed", log);
        Assert.Contains("[launcher] the engine exited with 0", log);
        if (OperatingSystem.IsLinux()) Assert.Contains($"stub PWD: {working}", log);

        // The stub takes the install's catalog away and exits with 3: the start failed, and the checks run again find it.
        string catalog = Path.Combine(_install, "SYS4INI.BIN");
        byte[] bytes = File.ReadAllBytes(catalog);
        LaunchResult failed = EngineLaunch.Run(profile, EngineCommands.For(stub, profile,
                                                                           ["--delete-file", catalog, "--exit-code", "3"]), Options);
        Assert.True(failed is { Started: true, Failed: true, ExitCode: 3 });
        Assert.Contains("stub err: 3", failed.LogTail);
        Assert.Equal(CheckStatus.Failed, failed.Checks[ProfileCheckKind.Install].Status);

        // A refused start opens no log, so the last session's stays whole.
        string kept = File.ReadAllText(failed.LogPath!);
        LaunchResult refused = EngineLaunch.Run(profile, EngineCommands.For(stub, profile, []), Options);
        Assert.False(refused.Started);
        Assert.Contains("Install: ", refused.Refusal);
        File.WriteAllBytes(catalog, bytes);
        Assert.True(RunLock.TryAcquire(profile.Root, markStart: true, out RunLock? running, out _));
        using (running)
        {
            LaunchResult busy = EngineLaunch.Run(profile, EngineCommands.For(stub, profile, []), Options);
            Assert.Equal("the profile is running", busy.Refusal);
        }
        Assert.Equal(kept, File.ReadAllText(failed.LogPath!));
    }
}
