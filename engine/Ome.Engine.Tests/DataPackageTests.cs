using System.Text;
using Ome.Mods.Data;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

/// <summary>
/// Format version 4 of docs/superpowers/specs/2026-11-17-data-patches-design.md §5.1 and §5.2: <c>[[data]]</c>,
/// <c>[[accepts]]</c> and <c>data/</c>, as the manifest reader and the package layout take them. Packages are
/// synthesized in a temporary folder per test. Whether a kind is accepted, and whether a file parses, is the link's.
/// </summary>
public sealed class DataPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-data-omp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string Head = """
        omp = 4
        id = "example.slimes"
        name = "Slimes"
        version = "1.0.0"

        [game]
        profile = "kamidori"
        target = "extended"

        """;

    private const string Owner = """
        omp = 4
        id = "example.owner"
        name = "Owner"
        version = "1.0.0"

        [game]
        profile = "kamidori"
        target = "extended"

        [lua]
        api = 1

        [[accepts]]
        kind = "example.lists"
        description = "Lists of names."

        [[accepts.at]]
        event = "example.owner.ready"
        handler = "lists.lua:apply"

        [[accepts.at]]
        event = "example.owner.later"
        handler = "lists.lua:apply_later"

        [[data]]
        kind = "example.lists"
        file = "own.toml"
        """;

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

    private static Exception Problem(string text) => Assert.Throws<ModManifestException>(() => ManifestReader.Parse(text));

    [Fact]
    public void FormatFourReadsDataEntriesAndKinds()
    {
        ModManifest contributor = ManifestReader.Parse(Head + """
            [[data]]
            kind = "kamidori.tables"
            file = "slimes.toml"

            [[data]]
            kind = "kamidori.tables"
            file = "bosses/hard.toml"
            priority = -5
            """);
        Assert.Equal(4, contributor.OmpVersion);
        Assert.Null(contributor.Lua);
        Assert.Equal([new ModDataEntry(0, "kamidori.tables", "slimes.toml", 0),
                      new ModDataEntry(1, "kamidori.tables", "bosses/hard.toml", -5)], contributor.Data);
        Assert.Equal("data/bosses/hard.toml", contributor.Data[1].PackagePath);
        Assert.Equal("data[1] (data/bosses/hard.toml)", contributor.Data[1].Describe());

        ModManifest owner = ManifestReader.Parse(Owner);
        ModKind kind = Assert.Single(owner.Accepts);
        Assert.Equal("example.lists", kind.Kind);
        Assert.Equal("Lists of names.", kind.Description);
        Assert.Equal([new ModKindEvent(0, "example.owner.ready", new ModHandlerRef("lists.lua", "apply")),
                      new ModKindEvent(1, "example.owner.later", new ModHandlerRef("lists.lua", "apply_later"))], kind.At);
        Assert.Equal("example.lists", Assert.Single(owner.Data).Kind);
    }

    [Theory]
    [InlineData("omp = 3\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[data]]\nkind = \"kamidori.tables\"\nfile = \"a.toml\"", "data: [[data]] and [[accepts]] need omp = 4")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[[data]]\nkind = \"kamidori.tables\"\nfile = \"a.toml\"",
                "data: a package without [game] holds no data files")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[lua]\napi = 1\n"
                + "[[accepts]]\nkind = \"a.kind\"\n[[accepts.at]]\nevent = \"a.e\"\nhandler = \"a.lua:f\"",
                "accepts: a package without [game] is a library, and declares no kind")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"native\"\n"
                + "[[data]]\nkind = \"kamidori.tables\"\nfile = \"a.toml\"", "a mod with data files targets \"extended\"")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[accepts]]\nkind = \"a.kind\"\n[[accepts.at]]\nevent = \"a.e\"\nhandler = \"a.lua:f\"",
                "accepts: [[accepts]] needs a [lua] table")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[lua]\napi = 1\n[[accepts]]\nkind = \"a.kind\"\nshape = \"a.schema.json\"\n[[accepts.at]]\nevent = \"a.e\"\nhandler = \"a.lua:f\"",
                "accepts[0].shape: shape checks come with data patches milestone 3")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[lua]\napi = 1\n[[accepts]]\nkind = \"a.kind\"", "accepts[0].at: a kind is delivered at one event at least")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[lua]\napi = 1\n[[accepts]]\nkind = \"a.kind\"\n[[accepts.at]]\nevent = \"a.e\"\nhandler = \"a.lua\"",
                "accepts[0].at[0].handler: 'a.lua' is not FILE.lua:FUNCTION")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[lua]\napi = 1\n[[accepts]]\nkind = \"a.kind\"\n[[accepts.at]]\nevent = \"a.e\"\nhandler = \"a.lua:f\"\n"
                + "[[accepts.at]]\nevent = \"a.e\"\nhandler = \"a.lua:g\"", "accepts[0].at: the event a.e is named 2 times")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[lua]\napi = 1\n[[accepts]]\nkind = \"a.kind\"\n[[accepts.at]]\nevent = \"a.e\"\nhandler = \"a.lua:f\"\n"
                + "[[accepts]]\nkind = \"a.kind\"\n[[accepts.at]]\nevent = \"a.f\"\nhandler = \"a.lua:g\"",
                "accepts: the kind 'a.kind' is declared 2 times")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[data]]\nkind = \"tables\"\nfile = \"a.toml\"", "data[0].kind: 'tables' is not a dotted name")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[data]]\nkind = \"a.kind\"\nfile = \"../a.toml\"", "data[0].file: '../a.toml' is not a path under data/")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[data]]\nkind = \"a.kind\"\nfile = \"/a.toml\"", "data[0].file: '/a.toml' is not a path under data/")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[data]]\nkind = \"a.kind\"\nfile = 'sub\\a.toml'", "has a backslash")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[data]]\nkind = \"a.kind\"\nfile = \"a.json\"", "data[0].file: 'a.json' is not a .toml file")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[data]]\nkind = \"a.kind\"\nfile = \"a.toml\"\n[[data]]\nkind = \"b.kind\"\nfile = \"A.toml\"",
                "data: data/a.toml is named by 2 entries")]
    [InlineData("omp = 4\nid = \"a.b\"\nname = \"x\"\nversion = \"1.0.0\"\n[game]\nprofile = \"kamidori\"\ntarget = \"extended\"\n"
                + "[[data]]\nkind = \"a.kind\"\nfile = \"a.toml\"\nweight = 3", "data[0].weight: unknown key")]
    public void BadDataManifestsAreRejectedWithTheirReason(string text, string reason)
    {
        var error = (ModManifestException)Problem(text);
        Assert.Contains(error.Problems, problem => problem.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void APackageNamesABoundedNumberOfDataFiles()
    {
        string entries = string.Concat(Enumerable.Range(0, DataBounds.MaxFilesPerPackage + 1)
            .Select(k => $"[[data]]\nkind = \"a.kind\"\nfile = \"f{k}.toml\"\n"));
        var error = (ModManifestException)Problem(Head + entries);
        Assert.Contains(error.Problems, problem => problem == $"data: a package names at most {DataBounds.MaxFilesPerPackage} "
                                                               + $"data files; this one names {DataBounds.MaxFilesPerPackage + 1}");
    }

    [Fact]
    public void TheLayoutKeepsEachNamedFileAndADataModChangesContent()
    {
        using ModPackage package = ModPackage.Open(WriteFolder(new Dictionary<string, string>
        {
            ["manifest.toml"] = Head + "[[data]]\nkind = \"kamidori.tables\"\nfile = \"b.toml\"\n"
                                     + "[[data]]\nkind = \"kamidori.tables\"\nfile = \"sub/a.toml\"\n",
            ["data/b.toml"] = "[unit.761]\nexperience_reward = 999\n",
            ["data/sub/a.toml"] = "",
        }));
        Assert.Equal(["data/b.toml", "data/sub/a.toml"], package.Layout.Data.Select(file => file.File.Path));
        Assert.Equal([0, 1], package.Layout.Data.Select(file => file.Entry.Index));
        Assert.False(package.Layout.ChangesNoContent);
        Assert.False(package.PresentationOnly);
        Assert.Empty(package.Layout.Notes);
    }

    [Fact]
    public void BeforeFormatFourADataFolderIsANote()
    {
        using ModPackage package = ModPackage.Open(WriteFolder(new Dictionary<string, string>
        {
            ["manifest.toml"] = Head.Replace("omp = 4", "omp = 3"),
            ["data/slimes.toml"] = "[unit.761]\n",
        }));
        Assert.Empty(package.Layout.Data);
        Assert.Contains("'data/slimes.toml' is outside the package layout and is ignored", package.Layout.Notes);
    }

    [Theory]
    [InlineData("data/extra.toml", "x = 1", "'data/extra.toml' is not named by a [[data]] entry, so it would do nothing")]
    [InlineData("data/slimes.toml", null, "data[0].file: the package has no data/slimes.toml")]
    [InlineData("lua/lists.lua", null, "accepts[0].at[1].handler: the package has no lua/lists.lua")]
    public void LayoutProblemsOfDataAreRejectedWithTheirReason(string path, string? text, string reason)
    {
        var tree = new Dictionary<string, string>
        {
            ["manifest.toml"] = Owner.Replace("file = \"own.toml\"", "file = \"slimes.toml\""),
            ["data/slimes.toml"] = "names = []",
            ["lua/lists.lua"] = "function apply() end function apply_later() end",
        };
        if (text == null) tree.Remove(path);
        else tree[path] = text;
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(WriteFolder(tree)));
        Assert.Contains(error.Problems, problem => problem.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void ALibraryHoldsNoDataFolder()
    {
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(WriteFolder(new Dictionary<string, string>
        {
            ["manifest.toml"] = "omp = 4\nid = \"a.lib\"\nname = \"x\"\nversion = \"1.0.0\"\n",
            ["data/a.toml"] = "",
        })));
        Assert.Contains(error.Problems, problem => problem.Contains("'data/a.toml': a package without [game] holds no data/"));
    }

    [Fact]
    public void ADataFileOverTheSizeBoundIsRejected()
    {
        string folder = WriteFolder(new Dictionary<string, string>
        {
            ["manifest.toml"] = Head + "[[data]]\nkind = \"kamidori.tables\"\nfile = \"big.toml\"\n",
        });
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        using (var stream = new FileStream(Path.Combine(folder, "data", "big.toml"), FileMode.Create))
            stream.SetLength(DataBounds.MaxFileBytes + 1);
        var error = Assert.Throws<ModPackageException>(() => ModPackage.Open(folder));
        Assert.Contains(error.Problems, problem => problem.Contains("'data/big.toml': a data file may be at most 16 MiB"));
    }
}
