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

    /// <summary>A program built beside this assembly in the same configuration and framework:
    /// <c>launcher/Ome.Launcher</c>'s, or the stub engine's.</summary>
    public static string Built(string project, string program)
    {
        string output = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string configuration = Path.GetFileName(Path.GetDirectoryName(output))!;
        string folder = Path.Combine(Paths.Repo, project == "Ome.Launcher" ? "launcher" : "engine", project, "bin",
                                     configuration, Path.GetFileName(output));
        string path = Path.Combine(folder, program + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(path), $"{program} is not built at {path}");
        return path;
    }

    public static string Launcher => Built("Ome.Launcher", "OME-Launcher");
    public static string Stub => Built("Ome.Launcher.StubEngine", "Ome.Launcher.StubEngine");

    /// <summary>The stub engine with <paramref name="arguments"/> before the <c>--</c>.</summary>
    public static EngineProgram StubEngine(params string[] arguments) => new(Stub, arguments);

    /// <summary>A synthetic install of <paramref name="game"/>: a catalog with the game's revision and title and the
    /// scripts its OMS modules run.</summary>
    public string Install(string game, string? folder = null, (string Key, string Value)[]? settings = null)
    {
        string root = Path.Combine(Root, "installs", folder ?? game);
        Directory.CreateDirectory(root);
        Catalog(root, game, settings: settings);
        return root;
    }

    /// <summary>Writes <paramref name="install"/>'s catalog for <paramref name="game"/>; with <paramref name="extra"/>
    /// a file more, so the same game's catalog has another fingerprint. <paramref name="settings"/> are its startup
    /// settings (<c>SAVEPATH</c>…); none by default.</summary>
    public static void Catalog(string install, string game, string? extra = null,
                               (string Key, string Value)[]? settings = null)
    {
        GameCatalogIdentity identity = GameProfileRegistry.BuiltIn.Find(game)!.CatalogIdentities[0];
        string[] files = SyntheticCatalog.ScriptsTheModulesRun(game);
        if (extra != null) files = [.. files, extra];
        File.WriteAllBytes(Path.Combine(install, Installs.CatalogFile), SyntheticCatalog.Build(
            files, settings ?? [], identity.CatalogRevision.PadRight(8), identity.Title));
    }

    /// <summary>A package of <paramref name="game"/> in the shared library, whose one new script runs one the game's
    /// catalog holds; <paramref name="target"/> <c>extended</c> needs Extended mode. <paramref name="tables"/> ends the
    /// manifest (<c>[[requires]]</c>, <c>[order]</c>…); <paramref name="parent"/> puts it in another folder.</summary>
    public string Package(string game, string id, string target = "native", string tables = "", string? parent = null)
    {
        string folder = Path.Combine(parent ?? Home.ModsDirectory(game), id);
        string script = "S" + new string(id.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        string runs = Path.GetFileNameWithoutExtension(SyntheticCatalog.ScriptsTheModulesRun(game)[0]);
        Directory.CreateDirectory(Path.Combine(folder, "scripts"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = {(target == "native" ? 1 : 3)}\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.2.0\"\n"
            + $"[scripts]\nnames = [\"{script}\"]\n[game]\nprofile = \"{game}\"\ntarget = \"{target}\"\n" + tables);
        File.WriteAllText(Path.Combine(folder, "scripts", script + ".oms"),
            $"oms 1\nscript \"{script}\"\nuse {game}.scripts\n\n    run {runs}\n    exit\n");
        return folder;
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
    /// <summary>The desktop the window's shortcuts go to, inside the temporary home.</summary>
    public string Desktop => Path.Combine(Root, "desktop");

    public LauncherSession Session(WindowReason reason = WindowReason.Requested, string language = "en",
                                   ProfileEntry? profile = null, LaunchResult? launch = null, EngineProgram? engine = null)
    {
        var start = new WindowStart(Home, engine ?? new EngineProgram(Path.Combine(Root, "engine", "OME"), []), profile,
                                    launch);
        return new LauncherSession(start, reason, Path.Combine(Root, "launcher", "OME-Launcher"), Environment,
                                   CheckOptions, CultureInfo.GetCultureInfo(language), Desktop);
    }
}
