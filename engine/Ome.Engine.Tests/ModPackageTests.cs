using System.IO.Compression;
using System.Text;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

/// <summary>Mod loader spec §3 and §11, gate 1: packages are synthesized in a temporary folder per test.</summary>
public sealed class ModPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-omp-" + Guid.NewGuid().ToString("N"));

    private const string Manifest = """
        omp = 1
        id = "example.double-exp"
        name = "Double EXP"
        version = "1.2.0"
        authors = ["Tester"]
        description = "Doubles experience."

        [game]
        profile = "himegari"
        target = "native"
        revisions = ["sha256:0000000000000000000000000000000000000000000000000000000000000000"]

        [engine]
        oms = 1

        [[requires]]
        id = "himegari-base"
        version = ">=1.0, <2"

        [[conflicts]]
        id = "example.half-exp"
        reason = "both rescale the same award"

        [order]
        after = ["himegari-base"]

        [scripts]
        names = ["DBLEXP", "DBLEXP_UI"]

        [assets]
        names = ["DBLEXP_ICON.AGF"]

        [replace]
        "SC0010.BIN" = { over = "other.mod" }
        """;

    private static readonly Dictionary<string, string> Tree = new()
    {
        ["manifest.toml"] = Manifest,
        ["README.md"] = "# Double EXP",
        ["patches/award.oms"] = "oms 1\n",
        ["patches/award.omspatch.json"] = "{}",
        ["patches/plan_only.omspatch.json"] = "{}",
        ["scripts/DBLEXP.oms"] = "oms 1\n",
        ["scripts/DBLEXP_UI.oms"] = "oms 1\n",
        ["replace/SC0010.BIN"] = "BIN",
        ["assets/DBLEXP_ICON.AGF"] = "icon",
        ["assets/SYSTEM.AGF"] = "override",
        ["notes/todo.txt"] = "stray",
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string WriteFolder(IReadOnlyDictionary<string, string> tree, string name = "mod")
    {
        string folder = Path.Combine(_root, name);
        foreach (var (path, text) in tree)
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text, new UTF8Encoding(false));
        }
        return folder;
    }

    private string WriteZip(IEnumerable<(string Path, byte[] Bytes)> entries, string name = "mod.omp",
                            CompressionLevel level = CompressionLevel.Optimal)
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, name);
        using var archive = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var (path, bytes) in entries)
        {
            using Stream stream = archive.CreateEntry(path, level).Open();
            stream.Write(bytes);
        }
        return file;
    }

    private string WriteZip(IReadOnlyDictionary<string, string> tree, string name = "mod.omp")
        => WriteZip(tree.Select(pair => (pair.Key, Encoding.UTF8.GetBytes(pair.Value))), name);

    private static Dictionary<string, string> With(string path, string? text)
    {
        var tree = new Dictionary<string, string>(Tree);
        if (text == null) tree.Remove(path);
        else tree[path] = text;
        return tree;
    }

    [Fact]
    public void TheOmpAndFolderFormsOfOneModOpenIdentically()
    {
        using ModPackage folder = ModPackage.Open(WriteFolder(Tree));
        using ModPackage zip = ModPackage.Open(WriteZip(Tree));

        Assert.Equal(folder.Hash, zip.Hash);
        Assert.StartsWith("sha256:", folder.Hash);
        Assert.Equal(folder.Container.Entries, zip.Container.Entries);
        Assert.Equal(folder.Manifest.Id, zip.Manifest.Id);

        ModLayout layout = zip.Layout;
        Assert.Equal(["award", "plan_only"], layout.Patches.Select(patch => patch.Stem));
        Assert.NotNull(layout.Patches[0].Source);
        Assert.NotNull(layout.Patches[0].Plan);
        Assert.Null(layout.Patches[1].Source);
        Assert.Equal(["DBLEXP", "DBLEXP_UI"], layout.Scripts.Select(script => script.Name));
        Assert.Equal("SC0010.BIN", Assert.Single(layout.Replacements).Script);
        Assert.Equal([("DBLEXP_ICON.AGF", true), ("SYSTEM.AGF", false)],
                     layout.Assets.Select(asset => (asset.Name, asset.ListedAsNew)));
        Assert.Contains(layout.Notes, note => note.Contains("notes/todo.txt"));
    }

    [Fact]
    public void AnyContentChangeChangesThePackageHash()
    {
        using ModPackage original = ModPackage.Open(WriteFolder(Tree, "a"));
        using ModPackage edited = ModPackage.Open(WriteFolder(With("assets/SYSTEM.AGF", "overridE"), "b"));
        Assert.NotEqual(original.Hash, edited.Hash);
    }

    [Fact]
    public void TheManifestReadsEveryVersionOneSection()
    {
        ModManifest manifest = ManifestReader.Parse(Manifest);

        Assert.Equal("example.double-exp", manifest.Id);
        Assert.Equal(new SemanticVersion(1, 2, 0), manifest.Version);
        Assert.Equal("himegari", manifest.Profile);
        Assert.Equal("native", manifest.Target);
        Assert.Single(manifest.Revisions);
        ModRequirement requirement = Assert.Single(manifest.Requires);
        Assert.Equal("himegari-base", requirement.Id);
        Assert.True(requirement.Version.Contains(new SemanticVersion(1, 4, 2)));
        Assert.False(requirement.Version.Contains(new SemanticVersion(2, 0, 0)));
        Assert.False(requirement.Version.Contains(new SemanticVersion(0, 9, 9)));
        Assert.Equal("example.half-exp", Assert.Single(manifest.Conflicts).Id);
        Assert.Equal(["himegari-base"], manifest.After);
        Assert.Equal(["DBLEXP", "DBLEXP_UI"], manifest.ScriptNames);
        Assert.Equal(new ModReplacementOrder("SC0010.BIN", "other.mod"), Assert.Single(manifest.ReplacementOrders));
        Assert.True(manifest.NeedsCatalog);
    }

    [Theory]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\nverison = \"2\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"",
                "verison: unknown key")]
    [InlineData("omp = 1\nid = \"A.B\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"",
                "is not a lowercase, dot-separated id")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"",
                "is not a semantic version")]
    [InlineData("omp = 2\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"",
                "format version 2 is not supported")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"extended\"",
                "version 1 mods target \"native\"")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"", "game: required")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"\nrevisions = [\"S4IC422\"]",
                "is not a sha256:<64 lowercase hex> fingerprint")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"\n[[requires]]\nid = \"a.b\"",
                "a mod cannot name itself")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"\n[[requires]]\nid = \"c\"\nversion = \"~1\"",
                "is not a version comparator")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"\n[scripts]\nnames = [\"A\", \"a\"]",
                "is listed more than once")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"\n[replace]\n\"x.agf\" = { over = \"c\" }",
                "the key must be a script file name")]
    [InlineData("omp = 1\nid = \"a.b\"\nname = 3\nversion = \"1.0.0\"\n[game]\nprofile = \"himegari\"\ntarget = \"native\"",
                "name: expected a string")]
    [InlineData("omp = 1\nid = ", "not valid TOML")]
    public void BadManifestsAreRejectedWithTheirReason(string text, string reason)
    {
        var error = Assert.Throws<ModManifestException>(() => ManifestReader.Parse(text));
        Assert.Contains(error.Problems, problem => problem.Contains(reason));
    }

    [Theory]
    [InlineData("../evil.txt", "'..' path segment")]
    [InlineData("/abs.txt", "absolute path")]
    [InlineData("C:/evil.txt", "drive-letter path")]
    [InlineData("assets/x.agf:stream", "colon in path")]
    [InlineData("assets/CON.agf", "reserved device name")]
    [InlineData("assets/name. ", "ends with a dot or space")]
    public void UnsafeEntryPathsAreRejected(string path, string reason)
    {
        string file = WriteZip(Tree.Select(pair => (pair.Key, Encoding.UTF8.GetBytes(pair.Value)))
                                   .Append((path, "x"u8.ToArray())));
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(file));
        Assert.Contains(error.Problems, problem => problem.Contains(reason));
    }

    [Fact]
    public void NamesThatCollideCaseInsensitivelyAreRejected()
    {
        string file = WriteZip(Tree.Select(pair => (pair.Key, Encoding.UTF8.GetBytes(pair.Value)))
                                   .Append(("assets/system.agf", "x"u8.ToArray())));
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(file));
        Assert.Contains(error.Problems, problem => problem.Contains("collides with"));
    }

    [Fact]
    public void OversizedEntriesAndPackagesAreRejected()
    {
        string folder = WriteFolder(With("assets/BIG.AGF", new string('x', 4096)));
        var entry = Assert.Throws<ModPackageException>(
            () => ModPackage.Open(folder, new PackageLimits(MaxEntryBytes: 1024)));
        Assert.Contains(entry.Problems, problem => problem.Contains("'assets/BIG.AGF' expands to 4096 bytes"));

        var total = Assert.Throws<ModPackageException>(
            () => ModPackage.Open(folder, new PackageLimits(MaxTotalBytes: 2048)));
        Assert.Contains(total.Problems, problem => problem.Contains("over the 2048-byte limit"));

        var count = Assert.Throws<ModPackageException>(() => ModPackage.Open(folder, new PackageLimits(MaxEntries: 3)));
        Assert.Contains(count.Problems, problem => problem.Contains("more than 3 entries"));
    }

    [Fact]
    public void AnEntryThatExpandsPastTheRatioLimitIsRejected()
    {
        // 4 MiB of zeros deflates to a few kilobytes: far past 200:1 and past the 1 MiB floor.
        string file = WriteZip(Tree.Select(pair => (pair.Key, Encoding.UTF8.GetBytes(pair.Value)))
                                   .Append(("assets/BOMB.AGF", new byte[4 << 20])));
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(file));
        Assert.Contains(error.Problems, problem => problem.Contains("'assets/BOMB.AGF' expands") && problem.Contains("200:1"));

        // A small regular file compresses as well but stays under the floor.
        using ModPackage small = ModPackage.Open(WriteZip(
            Tree.Select(pair => (pair.Key, Encoding.UTF8.GetBytes(pair.Value))).Append(("assets/PAD.AGF", new byte[64 << 10])),
            "small.omp"));
        Assert.Contains(small.Layout.Assets, asset => asset.Name == "PAD.AGF");
    }

    [Theory]
    [InlineData("scripts/EXTRA.oms", "oms 1\n", "'scripts/EXTRA.oms' is not named in [scripts].names")]
    [InlineData("scripts/DBLEXP_UI.oms", null, "'DBLEXP_UI' has no scripts/DBLEXP_UI.oms")]
    [InlineData("assets/DBLEXP_ICON.AGF", null, "'DBLEXP_ICON.AGF' has no assets/DBLEXP_ICON.AGF")]
    [InlineData("replace/SC0010.BIN", null, "the package has no replace/SC0010.BIN")]
    [InlineData("patches/award.txt", "x", "patches/ holds only")]
    [InlineData("assets/sub/X.AGF", "x", "without subdirectories")]
    [InlineData("manifest.toml", null, "no manifest.toml at the package root")]
    public void LayoutProblemsAreRejectedWithTheirReason(string path, string? text, string reason)
    {
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(WriteFolder(With(path, text))));
        Assert.Contains(error.Problems, problem => problem.Contains(reason));
    }

    [Fact]
    public void AManifestThatIsNotUtf8IsRejected()
    {
        string file = WriteZip(Tree.Where(pair => pair.Key != "manifest.toml")
                                   .Select(pair => (pair.Key, Encoding.UTF8.GetBytes(pair.Value)))
                                   .Append(("manifest.toml", new byte[] { 0x6f, 0x6d, 0x70, 0x20, 0x3d, 0x20, 0x82, 0xa0 })));
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(file));
        Assert.Contains(error.Problems, problem => problem.Contains("is not UTF-8 text"));
    }

    [Fact]
    public void SemanticVersionsOrderPrereleasesBeforeTheirRelease()
    {
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
                            "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0"];
        var versions = ordered.Select(text =>
        {
            Assert.True(SemanticVersion.TryParse(text, out SemanticVersion version), text);
            return version;
        }).ToList();
        for (int k = 1; k < versions.Count; k++)
            Assert.True(versions[k - 1].CompareTo(versions[k]) < 0, $"{ordered[k - 1]} < {ordered[k]}");
        Assert.Equal(0, versions[7].CompareTo(new SemanticVersion(1, 0, 0, "", "build.5")));
        Assert.False(SemanticVersion.TryParse("01.0.0", out _));
    }

    // docs/modding/packages.md, `version`: missing minor and patch numbers count as zero, except in a bare or `=`
    // version, where `1.2` accepts every 1.2.x release and `1` every 1.x.y release, while `1.2.0` accepts only 1.2.0.
    // `<2` rejects 2.0.0's prereleases as well as 2.0.0.
    [Theory]
    [InlineData("1.2", "1.2.0", true)]
    [InlineData("1.2", "1.2.5", true)]
    [InlineData("1.2", "1.3.0", false)]
    [InlineData("1.2", "1.1.9", false)]
    [InlineData("1.2", "2.2.0", false)]
    [InlineData("=1.2", "1.2.5", true)]
    [InlineData("= 1.2", "1.3.0", false)]
    [InlineData("1", "1.0.0", true)]
    [InlineData("1", "1.9.3", true)]
    [InlineData("=1", "1.4.0", true)]
    [InlineData("1", "2.0.0", false)]
    [InlineData("1", "0.9.0", false)]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.5", false)]
    [InlineData("=1.2.0", "1.2.1", false)]
    [InlineData("1.2.0", "1.2.0+build.7", true)]
    [InlineData("<2", "1.9.9", true)]
    [InlineData("<2", "1.9.9-rc.1", true)]
    [InlineData("<2", "2.0.0", false)]
    [InlineData("<2", "2.0.0-rc.1", false)]
    [InlineData("<2", "2.0.0-rc1", false)]
    [InlineData("<2.0.0", "2.0.0-alpha", false)]
    [InlineData(">=1.0, <2", "1.4.2", true)]
    [InlineData(">=1.0, <2", "2.0.0-rc.1", false)]
    [InlineData(">=1.0", "1.0.0", true)]
    [InlineData(">1", "1.0.1", true)]
    [InlineData("<=1.2", "1.2.0", true)]
    [InlineData("<=1.2", "1.2.1", false)]
    [InlineData("*", "0.0.1-dev", true)]
    [InlineData("", "9.9.9", true)]
    public void VersionRangesFollowTheDocumentedRules(string range, string version, bool contained)
    {
        Assert.True(VersionRange.TryParse(range, out VersionRange parsed, out string? reason), reason);
        Assert.True(SemanticVersion.TryParse(version, out SemanticVersion candidate), version);
        Assert.Equal(contained, parsed.Contains(candidate));
    }
}
