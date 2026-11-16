using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;

/// <summary>
/// Gate 9 (launcher spec §8.6, launcher plan §5.12): OME-Launcher's command line, run as a child process. A temporary
/// OME home holds a profile on a synthetic Kamidori install, the stub engine stands in for the engine, and
/// <c>APPDATA</c> points into the test's folder, so nothing outside it is read or written.
/// </summary>
public sealed class CommandLineTests : IDisposable
{
    private const string KamidoriRevision = "S4IC433 ", KamidoriTitle = "神採りアルケミーマイスター";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-launcher-cli-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;
    private readonly string _install;

    public CommandLineTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_install);
        File.WriteAllBytes(Path.Combine(_install, "SYS4INI.BIN"), SyntheticCatalog.Build(
            SyntheticCatalog.ScriptsTheModulesRun("kamidori"), KamidoriRevision, KamidoriTitle));
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A program built beside this assembly in the same configuration: <c>launcher/Ome.Launcher</c>'s, or the
    /// stub engine's (net8.0).</summary>
    private static string Built(string project, string program, string? framework = null)
    {
        string output = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string configuration = Path.GetFileName(Path.GetDirectoryName(output))!;
        string folder = project == "Ome.Launcher"
            ? Path.Combine(Paths.Repo, "launcher", project, "bin", configuration, Path.GetFileName(output))
            : Path.Combine(Paths.Repo, "engine", project, "bin", configuration, framework!);
        string path = Path.Combine(folder, program + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(path), $"{program} is not built at {path}");
        return path;
    }

    private static string Launcher => Built("Ome.Launcher", "OME-Launcher");
    private static string Stub => Built("Ome.Launcher.StubEngine", "Ome.Launcher.StubEngine", "net8.0");

    private ProfileEntry Profile(string id, OmeHome? home = null)
    {
        home ??= _home;
        string fingerprint = Installs.Recognize(_install, GameProfileRegistry.BuiltIn).Fingerprint!;
        ProfileDocument document = ProfileDocument.New("Gate 9", "kamidori", _install, fingerprint, home);
        return ProfileStore.Create(home, LauncherSettings.Defaults, id, document).Profile;
    }

    /// <summary>Runs OME-Launcher to its end with both streams captured and its own <c>APPDATA</c>.</summary>
    private (int Exit, string Output, string Error) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Launcher)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _root,
        };
        start.Environment["APPDATA"] = Path.Combine(_root, "appdata");
        start.Environment["XDG_DATA_HOME"] = Path.Combine(_root, "appdata");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Patience))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"OME-Launcher {string.Join(' ', arguments)} did not exit in {Patience}");
        }
        return (process.ExitCode, output.Result, error.Result);
    }

    private static string Show((int Exit, string Output, string Error) run)
        => $"exit {run.Exit}\n--- stdout\n{run.Output}\n--- stderr\n{run.Error}";

    [Fact]
    public void PrintCommandPrintsTheLaunchsCommandAsJsonAndOutputWritesItToAFileAsWell()
    {
        ProfileEntry profile = Profile("gate");
        string output = Path.Combine(_root, "out", "command.json");

        var run = Run("--print-command", "gate", "--home", _home.Root, "--output", output, "--engine", Stub,
                      "--engine-arg", "--headless", "--", "--locator-hud");

        Assert.True(run.Exit == 0, Show(run));
        Assert.Equal(run.Output, File.ReadAllText(output, Encoding.UTF8));
        using JsonDocument json = JsonDocument.Parse(run.Output);
        Assert.Equal(Stub, json.RootElement.GetProperty("program").GetString());
        Assert.Equal(["--headless", "--", "--data-root", profile.Root, "--locator-hud"],
                     json.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal(Path.GetDirectoryName(Stub), json.RootElement.GetProperty("working_directory").GetString());
    }

    [Fact]
    public void CheckPrintsEachCheckAndItsExitCodeSaysWhetherPlayCanStart()
    {
        Profile("gate");
        string output = Path.Combine(_root, "check.txt");

        var passed = Run("--check", "gate", "--home", _home.Root, "--output", output);
        Assert.True(passed.Exit == 0, Show(passed));
        Assert.Equal(passed.Output, File.ReadAllText(output, Encoding.UTF8));
        foreach (string kind in new[] { "Install", "Fingerprint", "Root", "Packages", "Link", "Lock" })
            Assert.Contains($"passed   {kind}: ", passed.Output);

        File.Delete(Path.Combine(_install, "SYS4INI.BIN"));
        var failed = Run("--check", "gate", "--home", _home.Root);
        Assert.True(failed.Exit == 1, Show(failed));
        Assert.Contains("FAILED   Install: ", failed.Output);

        var unknown = Run("--check", "nobody", "--home", _home.Root);
        Assert.True(unknown.Exit == 2, Show(unknown));
        Assert.Contains("no profile has the id 'nobody'", unknown.Error);
    }

    [Fact]
    public void AMalformedCommandLineExitsWithTwoAndTheUsageAndNoArgumentsAsksForTheWindow()
    {
        var malformed = Run("--check");
        Assert.True(malformed.Exit == 2, Show(malformed));
        Assert.Contains("--check needs a value", malformed.Error);
        Assert.Contains(LauncherCommandLine.Usage, malformed.Error);

        var stray = Run("--check", "gate", "--", "--locator-hud");
        Assert.True(stray.Exit == 2, Show(stray));
        Assert.Contains("-- ARGS goes with --launch or --print-command", stray.Error);

        var window = Run("--home", _home.Root);
        Assert.True(window.Exit == 1, Show(window));
        Assert.Contains("the launcher's window is not built yet", window.Error);
    }

    [Fact]
    public void LaunchStartsTheEngineWithTheRunsArgumentsAndAFailedStartPrintsTheLogsEnd()
    {
        ProfileEntry profile = Profile("gate");
        string log = Path.Combine(profile.Root, EngineLaunch.DiagnosticsFolder, EngineLaunch.LogFile);

        var started = Run("--launch", "gate", "--home", _home.Root, "--engine", Stub, "--", "--locator-hud");
        Assert.True(started.Exit == 0, Show(started));
        string text = File.ReadAllText(log, Encoding.UTF8);
        Assert.Contains($"stub out: {profile.Root}", text);
        Assert.Contains("stub err: --locator-hud", text);
        Assert.Contains("[launcher] the engine exited with 0", text);

        var failed = Run("--launch", "gate", "--home", _home.Root, "--engine", Stub, "--engine-arg", "--exit-code",
                         "--engine-arg", "3");
        Assert.True(failed.Exit == 1, Show(failed));
        Assert.Contains($"the engine exited with 3; the end of {log}:", failed.Output);
        Assert.Contains("stub out: --exit-code", failed.Output);
        Assert.Contains("the checks, run again:", failed.Output);
        Assert.Contains("the window would open at the failed start", failed.Error);
    }

    [Fact]
    public void LaunchOfARootTheLauncherDidNotCreateStartsNothingAndAsksForItsReview()
    {
        ProfileEntry elsewhere = Profile("shared", new OmeHome(Path.Combine(_root, "other-home"), OmeHomeKind.Explicit));
        string log = Path.Combine(elsewhere.Root, EngineLaunch.DiagnosticsFolder, EngineLaunch.LogFile);

        var run = Run("--launch", elsewhere.Root, "--home", _home.Root, "--engine", Stub);

        Assert.True(run.Exit == 1, Show(run));
        Assert.Contains($"{elsewhere.Root} is a profile root the launcher did not create", run.Output);
        Assert.Contains("the window would open at the root's review", run.Error);
        Assert.False(File.Exists(log), "the engine started");
    }
}
