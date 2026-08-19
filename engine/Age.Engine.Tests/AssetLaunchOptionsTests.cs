using Age.Engine.Sys4;

namespace Age.Engine.Tests;

public sealed class AssetLaunchOptionsTests
{
    [Fact]
    public void ResolvesRepeatableOverlayRootsAgainstGameRootInArgumentOrder()
    {
        string temp = Path.Combine(Path.GetTempPath(), "age-overlays-" + Guid.NewGuid().ToString("N"));
        string gameRoot = Path.Combine(temp, "game");
        string first = Path.Combine(gameRoot, "first");
        string second = Path.Combine(temp, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            AssetLaunchOptions options = AssetLaunchOptions.Resolve(
                ["--overlay-root", "first", "--overlay-root", second,
                 "--overlay-root", "first", "--allow-bmp-as-agf"],
                gameRoot);

            Assert.Equal([Path.GetFullPath(first), Path.GetFullPath(second)], options.OverlayRoots);
            Assert.Equal(
                [Path.GetFullPath(first), Path.GetFullPath(second), Path.GetFullPath(gameRoot)],
                options.LooseRoots);
            Assert.True(options.AllowBmpAsAgf);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void DefaultsToGameRootOnlyWithBmpCompatibilityDisabled()
    {
        string root = Path.GetFullPath(Path.GetTempPath());
        AssetLaunchOptions options = AssetLaunchOptions.Resolve([], root);

        Assert.Empty(options.OverlayRoots);
        Assert.Equal([root], options.LooseRoots);
        Assert.False(options.AllowBmpAsAgf);
    }

    [Theory]
    [InlineData("--overlay-root")]
    [InlineData("--overlay-root", "--allow-bmp-as-agf")]
    public void RejectsMissingOverlayPath(params string[] arguments)
    {
        var error = Assert.Throws<ArgumentException>(
            () => AssetLaunchOptions.Resolve(arguments, Path.GetTempPath()));
        Assert.Contains("--overlay-root requires a directory path", error.Message);
    }

    [Fact]
    public void RejectsOverlayPathThatIsNotADirectory()
    {
        string missing = "missing-" + Guid.NewGuid().ToString("N");
        var error = Assert.Throws<ArgumentException>(() => AssetLaunchOptions.Resolve(
            ["--overlay-root", missing], Path.GetTempPath()));
        Assert.Contains("is not a directory", error.Message);
        Assert.Contains(missing, error.Message);
    }
}
