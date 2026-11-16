using Ome.Launcher.Core;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Launcher spec §4.1 (launcher plan §5.1): where OME home is, for each operating system, and its layout.</summary>
public sealed class OmeHomeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-home-" + Guid.NewGuid().ToString("N"));

    public OmeHomeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static LauncherEnvironment Environment(OmePlatform platform, string? xdg = null)
        => new(platform, name => name == "XDG_DATA_HOME" ? xdg : null,
               Path.Combine("appdata", "Roaming"), Path.Combine("home", "player"));

    [Fact]
    public void TheDefaultFollowsEachSystemsRule()
    {
        Assert.Equal(Path.Combine("appdata", "Roaming", "OpenMaidEngine"), OmeHome.DefaultRoot(Environment(OmePlatform.Windows)));
        Assert.Equal(Path.Combine("home", "player", "Library", "Application Support", "OpenMaidEngine"),
                     OmeHome.DefaultRoot(Environment(OmePlatform.MacOS)));
        string xdg = Path.Combine(_root, "xdg");
        Assert.Equal(Path.Combine(xdg, "OpenMaidEngine"), OmeHome.DefaultRoot(Environment(OmePlatform.Linux, xdg)));
        // The XDG rules ignore an unset, empty or relative XDG_DATA_HOME.
        foreach (string? ignored in new[] { null, "", "relative/data" })
            Assert.Equal(Path.Combine("home", "player", ".local", "share", "OpenMaidEngine"),
                         OmeHome.DefaultRoot(Environment(OmePlatform.Linux, ignored)));
    }

    [Fact]
    public void ADataFolderBesideTheLauncherIsAPortableHome()
    {
        string launcher = Path.Combine(_root, "OME");
        Directory.CreateDirectory(launcher);
        LauncherEnvironment environment = Environment(OmePlatform.Windows);
        Assert.Equal(OmeHomeKind.Default, OmeHome.Resolve(null, launcher, _root, environment).Kind);

        // A file named data is not a home.
        File.WriteAllText(Path.Combine(launcher, "data"), "");
        Assert.Equal(OmeHomeKind.Default, OmeHome.Resolve(null, launcher, _root, environment).Kind);
        File.Delete(Path.Combine(launcher, "data"));

        Directory.CreateDirectory(Path.Combine(launcher, "data"));
        OmeHome portable = OmeHome.Resolve(null, launcher, _root, environment);
        Assert.Equal(OmeHomeKind.Portable, portable.Kind);
        Assert.Equal(Path.Combine(launcher, "data"), portable.Root);
        Assert.False(portable.NeedsHomeArgument(launcher, environment));
    }

    [Fact]
    public void HomeWinsOverPortableAndIsResolvedAgainstTheWorkingFolder()
    {
        string launcher = Path.Combine(_root, "OME");
        Directory.CreateDirectory(Path.Combine(launcher, "data"));
        LauncherEnvironment environment = Environment(OmePlatform.Windows);
        OmeHome home = OmeHome.Resolve("elsewhere", launcher, _root, environment);
        Assert.Equal(OmeHomeKind.Explicit, home.Kind);
        Assert.Equal(Path.Combine(_root, "elsewhere"), home.Root);
        Assert.True(home.NeedsHomeArgument(launcher, environment));
        // --home naming the folder the launcher finds anyway needs no --home in a shortcut.
        Assert.False(OmeHome.Resolve(Path.Combine(launcher, "data") + Path.DirectorySeparatorChar, launcher, _root, environment)
                         .NeedsHomeArgument(launcher, environment));
        Assert.Throws<ArgumentException>(() => OmeHome.Resolve("", launcher, _root, environment));
    }

    [Fact]
    public void TheLayoutIsTheSpecs()
    {
        var home = new OmeHome(_root, OmeHomeKind.Explicit);
        Assert.Equal(Path.Combine(_root, "launcher.toml"), home.SettingsPath);
        Assert.Equal(Path.Combine(_root, "profiles"), home.ProfilesDirectory);
        Assert.Equal(Path.Combine(_root, "mods", "kamidori"), home.ModsDirectory("kamidori"));
        Assert.Equal(Path.Combine(_root, "mods", "common"), home.CommonModsDirectory);
        Assert.Equal(Path.Combine(_root, "art", "himegari"), home.ArtDirectory("himegari"));
        Assert.Equal(Path.Combine(_root, "cache", "art", "himegari"), home.ArtCacheDirectory("himegari"));
    }
}
