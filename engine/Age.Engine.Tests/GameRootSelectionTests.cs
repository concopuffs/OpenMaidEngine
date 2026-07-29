using Age.Engine.Sys4;

public sealed class GameRootSelectionTests
{
    [Fact]
    public void ExplicitRelativeOverrideWinsAndResolvesAgainstCurrentDirectory()
    {
        using var tree = new TempTree();
        string executableDirectory = tree.CreateDirectory("exe", withCatalog: true);
        string currentDirectory = tree.CreateDirectory("working", withCatalog: true);
        string selectedDirectory = tree.CreateDirectory("selected", withCatalog: true);

        GameRootSelection selection = GameRootSelection.Resolve(
            ["--scene", "SYSTEM4", "--game-root", Path.Combine("..", "selected")],
            Path.Combine(executableDirectory, "OpenMaidEngine.exe"),
            currentDirectory);

        Assert.Equal(selectedDirectory, selection.Root);
        Assert.Equal(GameRootSource.LaunchArgument, selection.Source);
        Assert.Equal("launch-argument", selection.SourceName);
    }

    [Fact]
    public void DuplicateOverrideUsesLastDirectory()
    {
        using var tree = new TempTree();
        string currentDirectory = tree.CreateDirectory("working");
        tree.CreateDirectory("first", withCatalog: true);
        string second = tree.CreateDirectory("second", withCatalog: true);

        GameRootSelection selection = GameRootSelection.Resolve(
            ["--game-root", Path.Combine("..", "first"),
             "--game-root", Path.Combine("..", "second")],
            null,
            currentDirectory);

        Assert.Equal(second, selection.Root);
    }

    [Fact]
    public void DefaultsToExecutableDirectory()
    {
        using var tree = new TempTree();
        string executableDirectory = tree.CreateDirectory("exe", withCatalog: true);
        string currentDirectory = tree.CreateDirectory("working", withCatalog: true);

        GameRootSelection selection = GameRootSelection.Resolve(
            [], Path.Combine(executableDirectory, "OpenMaidEngine.exe"), currentDirectory);

        Assert.Equal(executableDirectory, selection.Root);
        Assert.Equal(GameRootSource.ExecutableDirectory, selection.Source);
    }

    [Fact]
    public void FallsBackToCurrentDirectory()
    {
        using var tree = new TempTree();
        string executableDirectory = tree.CreateDirectory("exe");
        string currentDirectory = tree.CreateDirectory("working", withCatalog: true);

        GameRootSelection selection = GameRootSelection.Resolve(
            [], Path.Combine(executableDirectory, "OpenMaidEngine"), currentDirectory);

        Assert.Equal(currentDirectory, selection.Root);
        Assert.Equal(GameRootSource.CurrentDirectory, selection.Source);
    }

    [Theory]
    [InlineData("--game-root")]
    [InlineData("--game-root", "--scene", "SYSTEM4")]
    public void MissingOverridePathIsRejected(params string[] arguments)
    {
        using var tree = new TempTree();

        var error = Assert.Throws<ArgumentException>(
            () => GameRootSelection.Resolve(arguments, null, tree.Root));

        Assert.Contains("--game-root requires a directory path", error.Message);
    }

    [Fact]
    public void ExplicitDirectoryWithoutCatalogIsRejectedWithoutFallback()
    {
        using var tree = new TempTree();
        string executableDirectory = tree.CreateDirectory("exe", withCatalog: true);
        string currentDirectory = tree.CreateDirectory("working");
        tree.CreateDirectory("invalid");

        var error = Assert.Throws<ArgumentException>(() => GameRootSelection.Resolve(
            ["--game-root", Path.Combine("..", "invalid")],
            Path.Combine(executableDirectory, "OpenMaidEngine.exe"),
            currentDirectory));

        Assert.Contains("does not contain SYS4INI.BIN", error.Message);
    }

    [Fact]
    public void MissingDefaultsReportSearchedLocationsAndOverride()
    {
        using var tree = new TempTree();
        string executableDirectory = tree.CreateDirectory("exe");
        string currentDirectory = tree.CreateDirectory("working");

        var error = Assert.Throws<ArgumentException>(() => GameRootSelection.Resolve(
            [], Path.Combine(executableDirectory, "OpenMaidEngine"), currentDirectory));

        Assert.Contains(executableDirectory, error.Message);
        Assert.Contains(currentDirectory, error.Message);
        Assert.Contains("--game-root <directory>", error.Message);
    }

    private sealed class TempTree : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "age-game-root-tests", Guid.NewGuid().ToString("N"));

        public TempTree() => Directory.CreateDirectory(Root);

        public string CreateDirectory(string name, bool withCatalog = false)
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            if (withCatalog) File.WriteAllBytes(Path.Combine(path, "SYS4INI.BIN"), []);
            return Path.GetFullPath(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
