using System.Globalization;
using Ome.Engine.Profiles;
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

    /// <summary>A profile on <paramref name="install"/>, made as the wizard makes one.</summary>
    public ProfileEntry Profile(string id, string game, string install, string? name = null)
    {
        string fingerprint = Installs.Recognize(install, GameProfileRegistry.BuiltIn).Fingerprint!;
        ProfileDocument document = ProfileDocument.New(name, game, install, fingerprint, Home);
        return ProfileStore.Create(Home, LauncherSettingsStore.Load(Home).Settings, id, document).Profile;
    }

    /// <summary>The window's session on this home, with the stub's path as its engine.</summary>
    public LauncherSession Session(WindowReason reason = WindowReason.Requested, string language = "en",
                                   ProfileEntry? profile = null, LaunchResult? launch = null)
    {
        var start = new WindowStart(Home, new EngineProgram(Path.Combine(Root, "engine", "OME"), []), profile, launch);
        return new LauncherSession(start, reason, Path.Combine(Root, "launcher", "OME-Launcher"), Environment,
                                   CheckOptions, CultureInfo.GetCultureInfo(language));
    }
}
