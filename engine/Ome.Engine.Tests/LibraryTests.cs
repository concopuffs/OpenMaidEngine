using Ome.Launcher.Core;
using Ome.Mods.Launch;
using Ome.Mods.Packages;
using Xunit;

namespace Ome.Engine.Tests;

/// <summary>Launcher spec §5.9, §7 and §8.4 Mods (launcher plan §5.6): the index of a folder, the states of a profile's
/// packages, and the library's add, usage, remove, enable, disable and the switch to Compatibility. On synthetic
/// packages; no package is opened beyond its manifest.</summary>
public sealed class LibraryTests : IDisposable
{
    private static readonly string Fingerprint = "sha256:" + new string('c', 64);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-library-" + Guid.NewGuid().ToString("N"));
    private readonly OmeHome _home;
    private readonly string _install;

    public LibraryTests()
    {
        _home = new OmeHome(Path.Combine(_root, "home"), OmeHomeKind.Explicit);
        _install = Path.Combine(_root, "games", "kamidori");
        Directory.CreateDirectory(_install);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Package(string folder, string id, string target = "native", string extra = "",
                                  string version = "1.0.0", string game = "kamidori", bool lua = false, string? name = null)
    {
        string path = Path.Combine(folder, name ?? id);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.toml"),
            $"omp = {(target == "native" && !lua ? 1 : 3)}\nid = \"{id}\"\nname = \"{id}\"\nversion = \"{version}\"\n"
            + (lua ? "[lua]\napi = 1\n" : $"[game]\nprofile = \"{game}\"\ntarget = \"{target}\"\n") + extra);
        if (lua)
        {
            Directory.CreateDirectory(Path.Combine(path, "lua"));
            File.WriteAllText(Path.Combine(path, "lua", "init.lua"), "return {}");
        }
        return path;
    }

    private static string Requires(string id, string version = ">=1.0") => $"[[requires]]\nid = \"{id}\"\nversion = \"{version}\"\n";

    private string Shared => _home.ModsDirectory("kamidori");

    private ProfileDocument Document(params ProfileModEntry[] order)
    {
        ProfileDocument document = ProfileDocument.New(null, "kamidori", _install, Fingerprint, _home);
        return document with { Mods = document.Mods! with { Order = order } };
    }

    private static ProfileModEntry On(string id) => new(id, true);
    private static ProfileModEntry Off(string id) => new(id, false);

    [Fact]
    public void AFolderIndexHasItsEntriesNotesAndDuplicates()
    {
        string folder = Path.Combine(_root, "folder");
        Package(folder, "example.one", name: "one-a");
        Package(folder, "example.one", version: "1.1.0", name: "one-b");
        Directory.CreateDirectory(Path.Combine(folder, "broken"));
        File.WriteAllText(Path.Combine(folder, "broken", "manifest.toml"), "omp = 1\n");
        PackageFolderIndex index = PackageIndex.ReadFolder(folder);
        Assert.Equal(2, index.Entries.Count);
        Assert.Equal("broken", Assert.Single(index.Notes).Name);
        Assert.Equal(2, index.Duplicates["example.one"].Count);
        Assert.Equal("example.one", index.Entries[0].Manifest.Name);
        Assert.Contains("does not exist", Assert.Single(PackageIndex.ReadFolder(Path.Combine(_root, "none")).Notes).Message);
    }

    [Fact]
    public void ThePackagesStatesComeFromTheIndexTheModeAndTheInstall()
    {
        Package(Shared, "example.ready");
        Package(Shared, "example.ext", target: "extended");
        Package(Shared, "example.lib", lua: true, name: "example.lib");
        Package(Shared, "example.orphan", extra: Requires("example.nowhere"));
        Package(Shared, "example.old-dep", extra: Requires("example.ready", ">=2.0"));
        Package(Shared, "example.pinned", extra: "revisions = [\"sha256:" + new string('d', 64) + "\"]\n");
        Package(Shared, "example.himegari", game: "himegari");
        string other = Path.Combine(_root, "other-folder");
        Package(other, "example.ready");
        Directory.CreateDirectory(Path.Combine(Shared, "example.unread"));
        File.WriteAllText(Path.Combine(Shared, "example.unread", "manifest.toml"), "omp = 1\n");
        ProfileDocument document = Document(Off("example.ext"), On("example.ready"), On("example.gone"),
                                            On("example.unread"));
        document = document with { Mods = document.Mods! with { Folders = [.. document.Mods.Folders, other] } };

        LibraryView view = Library.View(document, Library.Index(document), Fingerprint);
        Assert.Equal(["example.ext", "example.ready", "example.gone", "example.unread", "example.lib", "example.old-dep",
                      "example.orphan", "example.pinned"], view.Packages.Select(row => row.Id));
        LibraryPackage Row(string id) => view.Packages.Single(row => row.Id == id);
        Assert.Equal(PackageState.NeedsExtended, Row("example.ext").State);
        Assert.False(Row("example.ext").Enabled);
        Assert.True(Row("example.ext").Listed);
        Assert.Equal(PackageState.NeedsExtended, Row("example.lib").State);
        Assert.Equal(PackageState.Enabled, Row("example.ready").State);
        Assert.Contains("another copy of example.ready", Assert.Single(Row("example.ready").Problems));
        Assert.Equal(PackageState.Missing, Row("example.gone").State);
        Assert.Equal(["the profile lists it, and none of its folders holds it"], Row("example.gone").Reasons);
        Assert.Equal(PackageState.Missing, Row("example.unread").State);
        Assert.Contains("example.unread is left out", Assert.Single(Row("example.unread").Reasons));
        Assert.Equal(PackageState.NeedsDependency, Row("example.orphan").State);
        Assert.Equal(PackageState.NeedsDependency, Row("example.old-dep").State);
        Assert.Equal(PackageState.UnsupportedRevision, Row("example.pinned").State);
        Assert.False(Row("example.pinned").Listed);

        // In Extended mode the mode refuses nothing; without a fingerprint the revision is not checked.
        LibraryView extended = Library.View(document with { Mode = ProfileMode.Extended }, Library.Index(document), null);
        Assert.Equal(PackageState.Off, extended.Packages.Single(row => row.Id == "example.ext").State);
        Assert.Equal(PackageState.Off, extended.Packages.Single(row => row.Id == "example.pinned").State);
    }

    [Fact]
    public void AddFilesAPackageByItsGameAndAsksBeforeReplacing()
    {
        string downloads = Path.Combine(_root, "downloads");
        string first = Package(downloads, "example.voice", name: "Voice Pack v1");
        LibraryAddResult added = Library.Add(_home, first, replace: false);
        Assert.True(added.Copied);
        Assert.Equal(Path.Combine(Shared, "example.voice"), added.Destination);
        Assert.True(File.Exists(Path.Combine(added.Destination, "manifest.toml")));
        Assert.True(Directory.Exists(first));

        string second = Package(Path.Combine(_root, "downloads2"), "example.voice", version: "2.0.0");
        LibraryAddResult asked = Library.Add(_home, second, replace: false);
        Assert.False(asked.Copied);
        Assert.Equal("1.0.0", Assert.Single(asked.Replaced).Version.ToString());
        LibraryAddResult replaced = Library.Add(_home, second, replace: true);
        Assert.True(replaced.Copied);
        Assert.Equal("2.0.0", Assert.Single(PackageIndex.ReadFolder(Shared).Entries).Version.ToString());
        Assert.Empty(Directory.GetDirectories(Shared, ".adding-*"));

        string library = Package(downloads, "example.lua-lib", lua: true);
        Assert.Equal(Path.Combine(_home.CommonModsDirectory, "example.lua-lib"), Library.Add(_home, library, false).Destination);
        Assert.Throws<ArgumentException>(() => Library.Add(_home, added.Destination, replace: true));
    }

    [Fact]
    public void UsageNamesTheProfilesThatEnableAPackageOrHoldItsSelector()
    {
        string package = Package(Shared, "example.voice");
        LauncherSettings settings = LauncherSettings.Defaults;
        ProfileEntry enabling = ProfileStore.Create(_home, settings, "enabling", Document(On("example.voice"))).Profile;
        ProfileEntry holding = ProfileStore.Create(_home, settings, "holding", Document(Off("example.voice"))).Profile;
        Directory.CreateDirectory(Path.Combine(holding.Root, "mods"));
        File.WriteAllText(Path.Combine(holding.Root, "mods", "selectors.toml"), "[selectors]\n\"example.voice\" = 1\n");
        ProfileStore.Create(_home, settings, "unrelated", Document());
        IReadOnlyList<ProfileEntry> profiles = ProfileStore.List(_home, settings, () => throw new InvalidOperationException());

        PackageIndexEntry entry = PackageIndex.Read(package);
        PackageUsage usage = Library.Usage(entry, profiles);
        Assert.Equal(["enabling"], usage.Enabling);
        Assert.Equal(["holding"], usage.HoldingSelector);
        Library.Remove(entry);
        Assert.False(Directory.Exists(package));
    }

    [Fact]
    public void EnablingAlsoEnablesTheDependenciesTheLibraryHoldsButNeverABaseMod()
    {
        Package(Shared, "example.core");
        Package(Shared, "example.middle", extra: Requires("example.core"));
        Package(Shared, "example.top", target: "extended",
                extra: Requires("example.middle") + Requires("kamidori-base", ">=0") + Requires("example.absent"));
        ProfileDocument document = Document(On("example.other"), Off("example.middle"));
        LibraryChange change = Library.Enable(document, "example.top", Library.Index(document));
        // A disabled dependency is enabled in its place; the others go at the end, each before what needs it.
        Assert.Equal([On("example.other"), On("example.middle"), On("example.core"), On("example.top")],
                     change.Document.Mods!.Order);
        Assert.Equal(["example.core", "example.middle"], change.Changed.Order(StringComparer.Ordinal));
        Assert.Equal(["example.absent"], change.Unmet);
        Assert.Throws<ArgumentException>(() => Library.Enable(document, "example.nowhere", Library.Index(document)));

        LibraryChange disabled = Library.Disable(change.Document, "example.core", Library.Index(document));
        Assert.Equal(Off("example.core"), disabled.Document.Mods!.Order[2]);
        Assert.Equal(["example.middle"], disabled.Unmet);
    }

    [Fact]
    public void MovingSetsThePreferredOrderAndAPackageNeverListedJoinsItDisabled()
    {
        ProfileDocument document = Document(On("a"), Off("b"), On("c"));
        string Order(ProfileDocument changed)
            => string.Join(" ", changed.Mods!.Order.Select(entry => (entry.Enabled ? "+" : "-") + entry.Id));

        Assert.Equal("+c -b +a", Order(Library.Move(Library.Move(document, "c", "a"), "a", null)));
        Assert.Equal("+a +c -b", Order(Library.Move(document, "b", null)));
        Assert.Equal("+a -b +c", Order(Library.Move(document, "b", "b")));
        Assert.Equal("+a -new -b +c", Order(Library.Move(document, "new", "b")));
        Assert.Equal("+a -b +c -new", Order(Library.Move(document, "new", "unlisted")));
    }

    [Fact]
    public void AReadmeIsReadAsPlainTextUpToItsLimit()
    {
        string plain = Package(Shared, "example.plain");
        Assert.Null(Library.Readme(PackageIndex.Read(plain)));

        string documented = Package(Shared, "example.documented");
        File.WriteAllText(Path.Combine(documented, "README.md"), "\uFEFF# 説明\n" + new string('x', 100));
        PackageIndexEntry entry = PackageIndex.Read(documented);
        Assert.Equal(("# 説明\n" + new string('x', 100), false), Library.Readme(entry));
        // "# 説" is 5 bytes after the mark: a cut at 6 falls inside 説's three, which is dropped whole.
        Assert.Equal(("# ", true), Library.Readme(entry, limit: 6));
    }

    [Fact]
    public void SwitchingToCompatibilityDisablesWhatTheModeRefuses()
    {
        Package(Shared, "example.plain");
        Package(Shared, "example.ext", target: "extended");
        Package(Shared, "example.user", extra: Requires("example.ext"));
        ProfileDocument document = Document(On("example.ext"), On("example.plain"), On("example.user")) with
        {
            Mode = ProfileMode.Extended,
        };
        LibraryChange change = Library.ToCompatibility(document, Library.Index(document));
        Assert.Equal(ProfileMode.Native, change.Document.Mode);
        Assert.Equal([Off("example.ext"), On("example.plain"), Off("example.user")], change.Document.Mods!.Order);
        Assert.Equal(["example.ext", "example.user"], change.Changed);

        // The change is written under the profile's lock, read back first.
        ProfileEntry profile = ProfileStore.Create(_home, LauncherSettings.Defaults, "kami", document).Profile;
        LoadedProfile written = ProfileStore.Update(profile, current => Library.ToCompatibility(current,
                                                    Library.Index(current)).Document, _home);
        Assert.Equal(ProfileMode.Native, written.File.Mode);
        Assert.Equal(["example.plain"], written.File.Mods!.Enabled);
    }
}
