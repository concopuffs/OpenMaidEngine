using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Launcher spec §5.9 and §8.4 (launcher plan §5.8): releasing a selector as a call, the selectors a profile
/// holds, and the launcher's release under the profile's lock.</summary>
public sealed class SelectorReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-release-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Selectors(string root, string body)
    {
        Directory.CreateDirectory(Path.Combine(root, ModLaunch.ModsFolder));
        File.WriteAllText(SelectorRelease.PathFor(root), "[selectors]\n" + body);
    }

    [Fact]
    public void ReleaseSaysWhatEachIdHeldAndWritesOnlyWhenOneWasReleased()
    {
        string root = Path.Combine(_root, "data");
        Selectors(root, "\"example.a\" = 2\n\"example.b\" = 3\n");
        Assert.Equal([new ReleasedSelector("example.a", 2), new ReleasedSelector("example.c", null)],
                     SelectorRelease.Release(root, ["example.a", "example.c"]));
        Assert.Equal(new Dictionary<string, int> { ["example.b"] = 3 }, SelectorRelease.Held(root).Selectors);

        DateTime written = File.GetLastWriteTimeUtc(SelectorRelease.PathFor(root));
        File.SetLastWriteTimeUtc(SelectorRelease.PathFor(root), written.AddHours(-1));
        Assert.False(Assert.Single(SelectorRelease.Release(root, ["example.c"])).Held);
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(SelectorRelease.PathFor(root)));

        // A root with no selectors gains no file.
        string empty = Path.Combine(_root, "empty");
        Assert.False(Assert.Single(SelectorRelease.Release(empty, ["example.a"])).Held);
        Assert.False(File.Exists(SelectorRelease.PathFor(empty)));
    }

    [Fact]
    public void TheLauncherListsHeldSelectorsAndReleasesThemUnderTheLock()
    {
        var home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
        string install = Path.Combine(_root, "games", "kamidori");
        Directory.CreateDirectory(install);
        string package = Path.Combine(home.ModsDirectory("kamidori"), "example.here");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "manifest.toml"),
            "omp = 1\nid = \"example.here\"\nname = \"Here\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"native\"\n");
        ProfileDocument document = ProfileDocument.New(null, "kamidori", install, "sha256:" + new string('c', 64), home);
        ProfileEntry profile = ProfileStore.Create(home, LauncherSettings.Defaults, "kami", document).Profile;
        Selectors(profile.Root, "\"example.here\" = 2\n\"example.gone\" = 3\n\"kamidori-base\" = 4\n");

        LibraryView view = Library.View(document, Library.Index(document), null);
        Assert.Equal([new HeldSelector("example.gone", 3, true), new HeldSelector("example.here", 2, false),
                      new HeldSelector("kamidori-base", 4, false)], ProfileSelectors.List(profile, view));

        Assert.True(RunLock.TryAcquire(profile.Root, markStart: true, out RunLock? running, out _));
        using (running)
        {
            Assert.Throws<ProfileBusyException>(() => ProfileSelectors.Release(profile, ["example.gone"]));
            Assert.True(SelectorRelease.Held(profile.Root).TryGet("example.gone", out _));
        }
        Assert.True(Assert.Single(ProfileSelectors.Release(profile, ["example.gone"])).Held);
        Assert.False(SelectorRelease.Held(profile.Root).TryGet("example.gone", out _));
    }
}
