using System.Diagnostics;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Sys4;
using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Launcher spec §4.1 to §4.4 and §8.4 (launcher plan §5.3; gate 7's create, duplicate, rename and delete):
/// the profile list, the <c>profile.toml</c> writer, and the operations, each writing under the profile's lock. On
/// synthetic folders only: no install is opened.</summary>
public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-profile-store-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;
    private readonly string _install;

    public ProfileStoreTests()
    {
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
        _install = Path.Combine(_root, "games", "kamidori");
        Directory.CreateDirectory(_install);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string entry in Directory.EnumerateFileSystemEntries(_root, "*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = 0 }).OrderByDescending(path => path.Length))
            if (new FileInfo(entry).LinkTarget != null) Directory.Delete(entry);
        Directory.Delete(_root, recursive: true);
    }

    private static Func<MachineState> NoMachine => () => throw new InvalidOperationException("the machine's state was read");

    private ProfileDocument Document(string? name = "Kamidori")
        => ProfileDocument.New(name, "kamidori", _install, "sha256:" + new string('a', 64), _home);

    [Fact]
    public void TheWriterWritesWhatTheEngineReadsBack()
    {
        string root = Path.Combine(_home.ProfilesDirectory, "kami");
        string installInHome = Path.Combine(_home.Root, "games", "kamidori");
        var document = new ProfileDocument(
            "Kamidori \"main\"", "kamidori", ProfileMode.Extended,
            new ProfileInstall(installInHome, "sha256:" + new string('b', 64),
                               [Path.Combine(installInHome, "patch"), Path.Combine(_root, "elsewhere-overlay")], true),
            new ProfileMods([Path.Combine(_home.Root, "mods", "kamidori"), Path.Combine(_root, "my-mods")],
                            [new ProfileModEntry("example.one", true), new ProfileModEntry("example.two", false)],
                            SkipFailed: true),
            new ProfileLaunch("software", "portable", "halt"),
            new ProfileLauncherTable(Path.Combine(root, "art", "cover.png"), ProfileLauncherTable.Direct3D12, "a note\non two lines"));
        ProfileDocuments.Write(document, root, _home);

        string text = File.ReadAllText(Path.Combine(root, ProfileFile.FileName));
        // Inside OME home a path is relative to the root; an overlay is relative to the install when inside it.
        Assert.Contains("root = \"../../games/kamidori\"", text);
        Assert.Contains("overlays = [\"patch\", ", text);
        Assert.Contains("folders = [\"../../mods/kamidori\", ", text);
        Assert.Contains("order = [\"example.one\", \"-example.two\"]", text);
        Assert.Contains("cover = \"art/cover.png\"", text);
        Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));

        LoadedProfile read = ProfileDocuments.Read(root)!;
        Assert.Empty(read.Warnings);
        Assert.True(ProfileDocuments.SameAsEngineReads(document, read.File));
        Assert.Equal(document.Launcher, read.Document.Launcher);
        Assert.Equal(document.Name, read.Document.Name);
    }

    [Fact]
    public void TheLauncherTableWarnsAndKeepsItsDefaults()
    {
        string root = Path.Combine(_root, "hand-written");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ProfileFile.FileName), $"""
            ome_profile = 1
            game = "kamidori"
            mode = "native"
            [install]
            root = {Ome.Mods.Localization.TextCatalogFileWriter.Quote(_install)}
            [launcher]
            cover = "../outside.png"
            graphics_api = "vulkan"
            later = 1
            """);
        LoadedProfile read = ProfileDocuments.Read(root)!;
        Assert.Equal(ProfileLauncherTable.Defaults, read.Document.Launcher);
        string warnings = string.Join("\n", read.Warnings);
        Assert.Contains("launcher.cover: '../outside.png' is not a file inside the profile root", warnings);
        Assert.Contains("launcher.graphics_api: 'vulkan'", warnings);
        Assert.Contains("launcher.later: unknown key", warnings);
        Assert.Throws<ArgumentException>(() => ProfileDocuments.Serialize(
            read.Document with { Launcher = new ProfileLauncherTable(Path.Combine(_root, "outside.png")) }, root, _home));
    }

    [Fact]
    public void CreateWritesTheFileAndTheDefaultsAndRefusesATakenId()
    {
        var settings = LauncherSettings.Defaults with { NewProfile = new PortOptions("ja", 1600, 1200, false, true) };
        ProfileChange change = ProfileStore.Create(_home, settings, "kamidori", Document());
        Assert.True(change.Profile.Usable);
        Assert.Same(settings, change.Settings);
        string root = Path.Combine(_home.ProfilesDirectory, "kamidori");
        Assert.Equal(root, change.Profile.Root);
        Assert.Equal(settings.NewProfile, new OmeIniStore(Path.Combine(root, PortOptions.FileName)).Load());
        Assert.Equal("Kamidori", ProfileFile.TryRead(root)!.Name);

        Assert.Equal(["kamidori"], ProfileStore.List(_home, settings, NoMachine).Select(entry => entry.Id));
        Assert.Contains("taken", Assert.Throws<ArgumentException>(
            () => ProfileStore.Create(_home, settings, "kamidori", Document())).Message);
        Assert.Throws<ArgumentException>(() => ProfileStore.Create(_home, settings, "Kami Dori", Document()));
        Assert.Equal("kamidori-2", ProfileStore.SuggestId("Kamidori", "kamidori", _home, settings));
        Assert.Equal("kami-dori", ProfileStore.SuggestId("  Kämi Dori! ", "kamidori", _home, settings));
        Assert.Equal("kamidori-2", ProfileStore.SuggestId("神採り", "kamidori", _home, settings));
    }

    [Fact]
    public void ACreatedRootElsewhereIsListedAndCountsAsReviewed()
    {
        string elsewhere = Path.Combine(_root, "saves-on-d", "kami");
        ProfileChange change = ProfileStore.Create(_home, LauncherSettings.Defaults, "kami", Document(), elsewhere);
        Assert.Equal([new ExternalRoot(elsewhere, "kami")], change.Settings.Roots);
        Assert.Equal([elsewhere], change.Settings.Reviewed);
        Assert.True(Assert.Single(ProfileStore.List(_home, change.Settings, NoMachine)).Elsewhere);

        string busy = Path.Combine(_root, "not-empty");
        Directory.CreateDirectory(busy);
        File.WriteAllText(Path.Combine(busy, "readme.txt"), "x");
        Assert.Throws<IOException>(() => ProfileStore.Create(_home, change.Settings, "other", Document(), busy));
        Assert.Throws<ArgumentException>(() => ProfileStore.Create(_home, change.Settings, "inner", Document(),
                                                                   Path.Combine(elsewhere, "inner")));
        Assert.Throws<ArgumentException>(() => ProfileStore.Create(_home, change.Settings, "home", Document(),
                                                                   Path.Combine(_home.ProfilesDirectory, "x")));
    }

    /// <summary>A profile with saves, selectors, a link cache, logs, a cover and a mod folder of its own.</summary>
    private ProfileEntry Played(string id)
    {
        ProfileDocument document = Document() with
        {
            Mods = new ProfileMods([Path.Combine(_home.ProfilesDirectory, id, "packages")], [], SkipFailed: false),
            Launcher = new ProfileLauncherTable(Path.Combine(_home.ProfilesDirectory, id, "cover.png")),
        };
        ProfileEntry entry = ProfileStore.Create(_home, LauncherSettings.Defaults, id, document).Profile;
        string root = entry.Root;
        foreach (string file in new[] { "SAVE/SAVE001.SAV", "SAVE/SAVE001.mods.toml", "mods/selectors.toml",
                                        "mods/.cache/link.bin", "diagnostics/godot.log", "SYS4REG.INI", "cover.png",
                                        "packages/example.one/manifest.toml" })
        {
            string path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }
        return ProfileStore.Find(ProfileStore.List(_home, LauncherSettings.Defaults, NoMachine), id)!;
    }

    [Fact]
    public void DuplicateCopiesTheSavesAndSelectorsTogether()
    {
        ProfileEntry source = Played("kami");
        ProfileChange copy = ProfileStore.Duplicate(_home, LauncherSettings.Defaults, source, "kami-copy", "Copy",
                                                    leaveOutSaves: null);
        string root = copy.Profile.Root;
        foreach (string file in new[] { "SAVE/SAVE001.SAV", "SAVE/SAVE001.mods.toml", "mods/selectors.toml",
                                        "SYS4REG.INI", "OME.INI", "cover.png", "packages/example.one/manifest.toml" })
            Assert.True(File.Exists(Path.Combine(root, file)), file);
        Assert.False(Directory.Exists(Path.Combine(root, "mods", ".cache")));
        Assert.False(Directory.Exists(Path.Combine(root, "diagnostics")));

        // The copy uses its own files: the paths inside the source now point inside the copy.
        ProfileDocument document = copy.Profile.Loaded!.Document;
        Assert.Equal("Copy", document.Name);
        Assert.Equal(Path.Combine(root, "packages"), Assert.Single(document.Mods!.Folders));
        Assert.Equal(Path.Combine(root, "cover.png"), document.Launcher.Cover);
        Assert.Equal(_install, document.Install.Root);
        Assert.Equal("Kamidori", ProfileFile.TryRead(source.Root)!.Name);
    }

    [Fact]
    public void DuplicateWithoutSavesLeavesOutTheSelectorsToo()
    {
        ProfileEntry source = Played("kami");
        Sys4StartupSettings startup = Sys4StartupSettings.FromPairs(
        [
            new("USEAPPDATAFOLDER", "1"),
            new("REGFILEPATH", @"Eushully\Kamidori"),
            new("SAVEPATH", @"Eushully\Kamidori\SAVE"),
        ]);
        string saves = ProfileStore.SaveDirectory(startup, source.Root);
        Assert.Equal(Path.Combine(source.Root, "SAVE"), saves);
        string root = ProfileStore.Duplicate(_home, LauncherSettings.Defaults, source, "fresh", null, saves).Profile.Root;
        Assert.False(Directory.Exists(Path.Combine(root, "SAVE")));
        Assert.False(File.Exists(Path.Combine(root, "mods", "selectors.toml")));
        Assert.True(File.Exists(Path.Combine(root, "SYS4REG.INI")));
        Assert.True(File.Exists(Path.Combine(root, "OME.INI")));
        Assert.Throws<ArgumentException>(() => ProfileStore.Duplicate(_home, LauncherSettings.Defaults, source, "bad",
                                                                      null, source.Root));
    }

    [Fact]
    public void NothingIsWrittenWhileTheGameRuns()
    {
        ProfileEntry source = Played("kami");
        Assert.True(RunLock.TryAcquire(source.Root, markStart: true, out RunLock? running, out _));
        using (running)
        {
            Assert.Throws<ProfileBusyException>(() => ProfileStore.Duplicate(_home, LauncherSettings.Defaults, source,
                                                                             "copy", null, null));
            Assert.False(Directory.Exists(Path.Combine(_home.ProfilesDirectory, "copy")));
            Assert.Throws<ProfileBusyException>(() => ProfileStore.Rename(source, "New", _home));
            Assert.Throws<ProfileBusyException>(() => ProfileStore.Delete(_home, LauncherSettings.Defaults, source));
            Assert.True(File.Exists(Path.Combine(source.Root, ProfileFile.FileName)));
        }
        Assert.Equal("Kamidori", ProfileFile.TryRead(source.Root)!.Name);
    }

    [Fact]
    public void RenameChangesTheNameAndNeverTheId()
    {
        ProfileEntry source = Played("kami");
        ProfileStore.Rename(source, "  Second run  ", _home);
        ProfileEntry renamed = ProfileStore.Find(ProfileStore.List(_home, LauncherSettings.Defaults, NoMachine), "kami")!;
        Assert.Equal("Second run", renamed.DisplayName);
        Assert.Equal(source.Root, renamed.Root);
        ProfileStore.Rename(renamed, "", _home);
        Assert.Equal("kami", ProfileStore.Find(ProfileStore.List(_home, LauncherSettings.Defaults, NoMachine), "kami")!.DisplayName);
        Assert.Throws<ArgumentException>(() => ProfileStore.Rename(renamed, "two\nlines", _home));
    }

    [Fact]
    public void DeleteRemovesTheFolderAndForgetsTheRootButNotWhatALinkNames()
    {
        string elsewhere = Path.Combine(_root, "elsewhere", "kami");
        ProfileChange change = ProfileStore.Create(_home, LauncherSettings.Defaults with { LastProfile = "kami" }, "kami",
                                                   Document(), elsewhere);
        string target = Path.Combine(_root, "linked");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        Junction(Path.Combine(elsewhere, "link"), target);

        LauncherSettings after = ProfileStore.Delete(_home, change.Settings, change.Profile);
        Assert.False(Directory.Exists(elsewhere));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        Assert.Empty(after.Roots);
        Assert.Empty(after.Reviewed);
        Assert.Null(after.LastProfile);
    }

    [Fact]
    public void TheListSaysWhatIsWrongWithARoot()
    {
        Directory.CreateDirectory(Path.Combine(_home.ProfilesDirectory, "Not An Id"));
        Directory.CreateDirectory(Path.Combine(_home.ProfilesDirectory, "empty"));
        ProfileStore.Create(_home, LauncherSettings.Defaults, "kami", Document());
        var settings = LauncherSettings.Defaults with
        {
            Roots =
            [
                new ExternalRoot(Path.Combine(_root, "moved-away"), "gone"),
                new ExternalRoot(Path.Combine(_root, "other"), "kami"),
                new ExternalRoot(@"\\ome-test-host.invalid\share\kami", "shared"),
            ],
        };
        int reads = 0;
        string machineFolder = Path.Combine(_root, "machine");
        IReadOnlyList<ProfileEntry> list = ProfileStore.List(_home, settings, () =>
        {
            reads++;
            return MachineState.Load(machineFolder);
        });
        string Problem(string id) => list.Single(entry => entry.Id == id && !entry.Usable).Problem!;
        Assert.Contains("is not a profile id", Problem("Not An Id"));
        Assert.Contains("has no profile.toml", Problem("empty"));
        Assert.Contains("is gone", Problem("gone"));
        Assert.Contains("is also the name of a folder in profiles/", Problem("kami"));
        Assert.True(ProfileStore.Find(list, "kami")!.Usable);
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("has not approved", Problem("shared"));
            Assert.Equal(1, reads);
        }
    }

    private static void Junction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
}
