using Ome.Engine.Frontend.Shell;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Launcher spec §8.4 Settings (launcher plan §6.4, §6.6): a profile root's <c>OME.INI</c> and
/// <c>SYS4REG.INI</c> read as the engine reads them, a key the registry lacks taking the game's <c>SYS4INI.BIN</c>
/// value. On a synthetic profile root.</summary>
public sealed class ProfileSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-profile-settings-" + Guid.NewGuid().ToString("N"));

    public ProfileSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ARootWithNeitherFileStartsFullscreenWithTheDefaultOptions()
    {
        ProfileSettings settings = ProfileSettings.Load(_root, Sys4StartupSettings.Empty);
        Assert.False(settings.Windowed);
        Assert.False(settings.ForceScreen);
        Assert.Equal(Ome.Engine.Frontend.Shell.PortOptions.Defaults, settings.Port);
        Assert.False(File.Exists(Path.Combine(_root, "SYS4REG.INI")), "reading writes nothing");
    }

    [Fact]
    public void TheFilesValuesAreReadAndAMissingKeyTakesTheGamesValue()
    {
        File.WriteAllText(Path.Combine(_root, "OME.INI"),
                          "[ui]\nLanguage=ja\n[display]\nWindowWidth=1280\nWindowHeight=720\n[debug]\nFpsCounter=1\n");
        File.WriteAllText(Path.Combine(_root, "SYS4REG.INI"), "[display]\r\nScreenMode=1\r\n");
        var startup = Sys4StartupSettings.FromPairs([new("FORCESCREEN", "1")]);

        ProfileSettings settings = ProfileSettings.Load(_root, startup);
        Assert.True(settings.Windowed);
        Assert.True(settings.ForceScreen);
        Assert.Equal("ja", settings.Port.Language);
        Assert.Equal((1280, 720), (settings.Port.WindowWidth, settings.Port.WindowHeight));
        Assert.True(settings.Port.FpsCounter);

        File.WriteAllText(Path.Combine(_root, "SYS4REG.INI"), "[display]\r\nScreenMode=0\r\nForceScreen=0\r\n");
        settings = ProfileSettings.Load(_root, Sys4StartupSettings.FromPairs([new("SCREENMODE", "1")]));
        Assert.False(settings.Windowed);
        Assert.False(settings.ForceScreen);
    }

    [Fact]
    public void BothFilesRoundTripWithTheKeysTheLauncherDoesNotKnow()
    {
        string ome = Path.Combine(_root, "OME.INI"), registry = Path.Combine(_root, "SYS4REG.INI");
        File.WriteAllText(ome, "[ui]\nLanguage=ja\n[future]\nOption=7\n");
        File.WriteAllText(registry, "[display]\r\nScreenMode=0\r\nNewKey=3\r\n[mine]\r\nKept=1\r\n");
        var startup = Sys4StartupSettings.FromPairs([new("FULLSCREENBIT", "32")]);

        ProfileSettings.Save(_root, port => port with { Language = "en" });
        ProfileSettings.Save(_root, port => port with { FpsCounter = true });
        ProfileSettings.Save(_root, windowed: true, startup: startup);
        ProfileSettings settings = ProfileSettings.Load(_root, startup);
        Assert.Equal(("en", true, true), (settings.Port.Language, settings.Port.FpsCounter, settings.Windowed));
        Assert.Contains("Option=7", File.ReadAllText(ome));
        string written = File.ReadAllText(registry);
        Assert.Contains("NewKey=3", written);
        Assert.Contains("[mine]\r\nKept=1", written);
        // The bound keys the file lacked take the game's values, as the engine's own write fills them.
        Assert.Contains("FullScreenBit=32", written);
        Assert.Equal(written.IndexOf("ScreenMode=1", StringComparison.Ordinal),
                     written.IndexOf("ScreenMode=", StringComparison.Ordinal));

        // Only what is given is written.
        DateTime before = File.GetLastWriteTimeUtc(registry);
        File.SetLastWriteTimeUtc(registry, before.AddHours(-1));
        ProfileSettings.Save(_root, port => port);
        Assert.Equal(before.AddHours(-1), File.GetLastWriteTimeUtc(registry));
    }

    [Fact]
    public void AStartModeIsNotWrittenWithoutTheInstallNorWhileTheGameRuns()
    {
        Assert.Throws<ArgumentException>(() => ProfileSettings.Save(_root, windowed: true));
        Assert.False(File.Exists(Path.Combine(_root, "SYS4REG.INI")));

        Assert.True(RunLock.TryAcquire(_root, markStart: true, out RunLock? engine, out _));
        using (engine)
            Assert.Throws<ProfileBusyException>(() => ProfileSettings.Save(_root, port => port));
        Assert.False(File.Exists(Path.Combine(_root, "OME.INI")));
    }

    [Fact]
    public void TheWindowSizesAreTheOptionsPanelsWithTheMenuBar()
    {
        var canvas = new Sys4LogicalCanvas(800, 600);
        Assert.Equal([(800, 600), (1000, 750), (1200, 900), (1400, 1050), (1600, 1200)],
                     ProfileSettings.WindowSizes(canvas, PortOptions.Defaults, null));
        // 1000 x 750 and its 25 rows of bar fit 775; 1200 x 900 does not.
        Assert.Equal([(800, 600), (1000, 750), (1280, 720)],
                     ProfileSettings.WindowSizes(canvas, PortOptions.Defaults with { WindowWidth = 1280, WindowHeight = 720 },
                                                 (1900, 775)));
    }
}
