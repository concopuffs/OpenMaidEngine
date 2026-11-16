using Ome.Engine.Sys4;
using Ome.Launcher.Core;
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
}
