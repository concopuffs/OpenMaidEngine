using Ome.Engine.Sys4;

/// <summary>Launcher spec §5.6 (gate 5, the decision): the engine started with no game hands over to the launcher.
/// The exported engine with a stub launcher beside it joins the package smoke in M4.</summary>
public sealed class LauncherHandOverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-handover-" + Guid.NewGuid().ToString("N"));

    public LauncherHandOverTests()
    {
        Directory.CreateDirectory(Install);
        Directory.CreateDirectory(Working);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Install => Path.Combine(_root, "install");
    private string Working => Path.Combine(_root, "working");
    private string Engine => Path.Combine(Install, "OME.exe");

    private string Launcher(bool windows = true)
    {
        string path = Path.Combine(Install, LauncherHandOver.LauncherName + (windows ? ".exe" : ""));
        File.WriteAllText(path, "");
        return path;
    }

    private static void Catalog(string folder) => File.WriteAllBytes(Path.Combine(folder, "SYS4INI.BIN"), []);

    [Fact]
    public void NoArgumentNoGameAndTheLauncherBesideTheEngineStartTheLauncher()
    {
        string launcher = Launcher();
        Assert.Equal(launcher, LauncherHandOver.Decide([], Engine, Working, windows: true));
    }

    [Fact]
    public void TheLaunchersFileNameFollowsThePlatform()
    {
        string linux = Launcher(windows: false);
        Assert.Equal(linux, LauncherHandOver.Decide([], Path.Combine(Install, "OME"), Working, windows: false));
        Assert.Null(LauncherHandOver.Decide([], Engine, Working, windows: true));
    }

    [Fact]
    public void AGameBesideTheEngineOrInTheWorkingFolderStartsTheEngine()
    {
        Launcher();
        Catalog(Install);
        Assert.Null(LauncherHandOver.Decide([], Engine, Working, windows: true));
        File.Delete(Path.Combine(Install, "SYS4INI.BIN"));
        Catalog(Working);
        Assert.Null(LauncherHandOver.Decide([], Engine, Working, windows: true));
    }

    [Fact]
    public void WithoutTheLauncherTheEngineStartsAsBefore()
        => Assert.Null(LauncherHandOver.Decide([], Engine, Working, windows: true));

    [Fact]
    public void AnyArgumentLeavesTodaysError()
    {
        // A mistyped command must show its own error, not the launcher.
        Launcher();
        foreach (string[] arguments in new[] { new[] { "--data-root" }, new[] { "--gmae", "himegari" }, new[] { "x" } })
        {
            Assert.Null(LauncherHandOver.Decide(arguments, Engine, Working, windows: true));
            var error = Assert.Throws<ArgumentException>(() => GameRootSelection.Resolve(arguments, Engine, Working));
            Assert.StartsWith("game root not found: SYS4INI.BIN was not present in", error.Message);
        }
    }

    [Fact]
    public void ALauncherOnlyInTheWorkingFolderIsNotStarted()
    {
        File.WriteAllText(Path.Combine(Working, LauncherHandOver.LauncherName + ".exe"), "");
        Assert.Null(LauncherHandOver.Decide([], Engine, Working, windows: true));
        Assert.Null(LauncherHandOver.Decide([], null, Working, windows: true));
    }
}
