using System.Globalization;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Launcher;
using Ome.Launcher.Core;

/// <summary>
/// A temporary OME home for the window's tests (launcher plan §6.1), with synthetic installs of both games, profiles
/// on them, and its own machine folder and application data, so nothing outside the test's folder is read or written.
/// </summary>
internal sealed class TestHome : IDisposable
{
    public TestHome()
    {
        Directory.CreateDirectory(Root);
        Home = new OmeHome(Path.Combine(Root, "home"), OmeHomeKind.Explicit);
        AppData = Path.Combine(Root, "appdata");
        Environment = new LauncherEnvironment(LauncherEnvironment.Current.Platform,
                                              name => name == "APPDATA" ? AppData : null, AppData, Root);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ome-launcher-ui-" + Guid.NewGuid().ToString("N"));
    public OmeHome Home { get; }
    public string AppData { get; }
    public LauncherEnvironment Environment { get; }
    public CheckOptions CheckOptions => new(MachineFolder: Path.Combine(AppData, "machine"));

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    /// <summary>A program built beside this assembly in the same configuration: <c>launcher/Ome.Launcher</c>'s, or the
    /// stub engine's (net8.0).</summary>
    public static string Built(string project, string program, string? framework = null)
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

    public static string Launcher => Built("Ome.Launcher", "OME-Launcher");
    public static string Stub => Built("Ome.Launcher.StubEngine", "Ome.Launcher.StubEngine", "net8.0");

    /// <summary>The stub engine with <paramref name="arguments"/> before the <c>--</c>.</summary>
    public static EngineProgram StubEngine(params string[] arguments) => new(Stub, arguments);

    /// <summary>A synthetic install of <paramref name="game"/>: a catalog with the game's revision and title and the
    /// scripts its OMS modules run.</summary>
    public string Install(string game, string? folder = null)
    {
        GameCatalogIdentity identity = GameProfileRegistry.BuiltIn.Find(game)!.CatalogIdentities[0];
        string root = Path.Combine(Root, "installs", folder ?? game);
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, Installs.CatalogFile), SyntheticCatalog.Build(
            SyntheticCatalog.ScriptsTheModulesRun(game), identity.CatalogRevision.PadRight(8), identity.Title));
        return root;
    }

    /// <summary>A profile on <paramref name="install"/>, made as the wizard makes one, in this home or in
    /// <paramref name="home"/>.</summary>
    public ProfileEntry Profile(string id, string game, string install, string? name = null, OmeHome? home = null)
    {
        home ??= Home;
        string fingerprint = Installs.Recognize(install, GameProfileRegistry.BuiltIn).Fingerprint!;
        ProfileDocument document = ProfileDocument.New(name, game, install, fingerprint, home);
        return ProfileStore.Create(home, LauncherSettingsStore.Load(home).Settings, id, document).Profile;
    }

    /// <summary>The window's session on this home. Its engine is <paramref name="engine"/>, else a program that is not
    /// there.</summary>
    public LauncherSession Session(WindowReason reason = WindowReason.Requested, string language = "en",
                                   ProfileEntry? profile = null, LaunchResult? launch = null, EngineProgram? engine = null)
    {
        var start = new WindowStart(Home, engine ?? new EngineProgram(Path.Combine(Root, "engine", "OME"), []), profile,
                                    launch);
        return new LauncherSession(start, reason, Path.Combine(Root, "launcher", "OME-Launcher"), Environment,
                                   CheckOptions, CultureInfo.GetCultureInfo(language));
    }
}
