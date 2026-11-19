using System.Text;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Sys4;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Mods.Tools;
using Ome.Script.Abi;
using Ome.Script.Patch;
using Ome.Scripting.Lua;

/// <summary>
/// The link of docs/superpowers/specs/2026-11-17-data-patches-design.md §5.4 and §5.7: the kinds (step 1), the data
/// files (step 2) and the deliveries (step 3), the report's kinds, and the documents in the link cache. Gate 2: each
/// case that stops the load does so with a report naming the mod and the file, and no mod code has run, since every
/// owner's Lua here raises an error at its top level, which only running it would show. The synthetic base has one
/// script, ADDEXP, with one patch point; the owner declares an event there, one raised from Lua, and one at a point the
/// install does not have.
/// </summary>
public sealed class DataLinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-data-link-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = new();
    private readonly LazyModRuntime _runtime = LuaModRuntime.Lazy();

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);
    private const string AddExp = "ADDEXP.BIN";
    private const string Point = "himegari.experience.after_calculation";
    private static readonly byte[] SyntheticAddExp = Encoding.ASCII.GetBytes("base ADDEXP.BIN");

    public void Dispose()
    {
        foreach (ModPackage mod in _opened) mod.Dispose();
        _runtime.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ModLinkBase Base() => new()
    {
        Profile = "himegari",
        CatalogFingerprint = "sha256:1111111111111111111111111111111111111111111111111111111111111111",
        ReadScript = name => name.Equals(AddExp, StringComparison.OrdinalIgnoreCase) ? SyntheticAddExp : null,
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
        PatchPoints = new OmsPatchPoints([new OmsPatchPoint(Point, AddExp, null,
            [new OmsPatchPointSite(OmsPatchPlan.FingerprintOf(SyntheticAddExp), 0x9a)])]),
        Lua = _runtime,
    };

    /// <summary>The owner's three events: at the point, raised from Lua, and at a point the base does not name.</summary>
    private const string OwnerEvents = """
        [[event]]
        name = "test.owner.award"
        point = "himegari.experience.after_calculation"

        [[event]]
        name = "test.owner.computed"
        raised_by = "lua"

        [[event]]
        name = "test.owner.unbound"
        point = "himegari.not.in.this.install"
        """;

    private const string NeverAtLink = "error('never at link')\n";

    /// <summary>A package of format 4 for himegari, its manifest's tables after the head, and its files.</summary>
    private string Folder(string id, string tables, Dictionary<string, string>? files = null, string head = "")
    {
        string folder = Path.Combine(_root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            (head.Length > 0 ? head : $"omp = 4\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n\n"
                                      + "[game]\nprofile = \"himegari\"\ntarget = \"extended\"\n\n") + tables + "\n");
        foreach (var (path, text) in files ?? new Dictionary<string, string>())
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        return folder;
    }

    private ModPackage Mod(string id, string tables, Dictionary<string, string>? files = null)
    {
        ModPackage mod = ModPackage.Open(Folder(id, tables, files));
        _opened.Add(mod);
        return mod;
    }

    private const string LuaTable = "[lua]\napi = 1\ncapabilities = [\"vm\"]\n\n";

    /// <summary>The owner: it accepts <c>test.lists</c> at the point's event and the Lua event, and
    /// <paramref name="extraKinds"/>.</summary>
    private ModPackage Owner(string extraKinds = "", string lists = "function apply(event, contribution) end")
        => Mod("test.owner", LuaTable + """
            [[accepts]]
            kind = "test.lists"
            description = "Lists of names."

            [[accepts.at]]
            event = "test.owner.award"
            handler = "lists.lua:apply"

            [[accepts.at]]
            event = "test.owner.computed"
            handler = "lists.lua:apply"

            """ + extraKinds, new() { ["events/owner.toml"] = OwnerEvents, ["lua/lists.lua"] = NeverAtLink + lists });

    private static string Requires(string id = "test.owner") => $"[[requires]]\nid = \"{id}\"\nversion = \">=1\"\n\n";

    private static string Data(string file, string kind = "test.lists", int? priority = null)
        => $"[[data]]\nkind = \"{kind}\"\nfile = \"{file}\"\n" + (priority is { } p ? $"priority = {p}\n" : "") + "\n";

    private ModPackage Contributor(string id, string text, string file = "a.toml", string kind = "test.lists",
                                   int? priority = null, string extra = "")
        => Mod(id, Requires() + Data(file, kind, priority) + extra, new() { [$"data/{file}"] = text });

    private ModLinkResult Link(params ModPackage[] mods) => ModLinker.Link(Base(), mods, mods.Select(mod => mod.Id).ToList());

    private static ModReportLine Stop(ModLinkResult result, string contains)
    {
        Assert.False(result.Succeeded);
        return Assert.Single(result.Report.Lines.Where(line => line.Stops),
                             line => line.Message.Contains(contains, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------- a clean link

    [Fact]
    public void EachDocumentIsDeliveredAtEachRaisedEventInTheMergedOrder()
    {
        ModPackage owner = Owner();
        ModPackage a = Mod("test.a", LuaTable + Requires() + Data("a.toml")
                                     + "[[hooks]]\nevent = \"test.owner.award\"\nhandler = \"a.lua:on_award\"\n",
            new() { ["data/a.toml"] = "names = [\"x\", \"y\"]\n[t]\nk = 1\n", ["lua/a.lua"] = NeverAtLink + "function on_award(e) end" });
        ModPackage b = Contributor("test.b", "[t]\nk = 2\nj = true\n", "b.toml", priority: -5);
        ModLinkResult result = Link(owner, a, b);

        Assert.True(result.Succeeded, result.Report.Render());
        ModRuntimeStack stack = result.Lua.Stack;
        // The owner's handler has an id because a delivery names it; a's hook got its id first, in the mods' loop.
        Assert.Equal([new ModRuntimeHandler("test.a", "a.lua", "on_award"), new ModRuntimeHandler("test.owner", "lists.lua", "apply")],
                     stack.Handlers);
        // Each value counts, and each table and array as one (plan §12.6): names, its two elements, t and k; t, k and j.
        Assert.Equal([("test.lists", "test.owner", "test.a", "data/a.toml", 5), ("test.lists", "test.owner", "test.b", "data/b.toml", 3)],
                     stack.ContributionList.Select(found => (found.Kind, found.Owner, found.Mod, found.File, found.Values)));
        Assert.Equal("""{names = ["x", "y"], t = {k = 1}}""", stack.ContributionList[0].Document.ToText());
        Assert.All(stack.ContributionList, found => Assert.Empty(found.Unraised));
        // b requires the owner, which ships Lua; b ships none and has no runtime mod of its own.
        Assert.Equal(["test.owner"], stack.ContributionList[1].Requires);
        Assert.DoesNotContain(stack.Mods, mod => mod.Id == "test.b");

        // Spec §5.5: ascending priority, then load order; at one priority a mod's files come before its hooks.
        static (int, int, int, int?) Due(PlannedHandler handler)
            => (handler.Handler.Index, handler.Priority, handler.LoadOrder, handler.Contribution?.Index);
        PlannedSite site = Assert.Single(result.Lua.Plan.Sites["ADDEXP"]);
        Assert.Equal([(1, -5, 2, 1), (1, 0, 1, 0), (0, 0, 1, null)], site.Handlers.Select(Due));
        Assert.All(site.Handlers, handler => Assert.Equal("test.owner.award", handler.GameEvent));
        Assert.Equal([(1, -5, 2, 1), (1, 0, 1, 0)], result.Lua.Plan.SubscribersOf("test.owner.computed").Select(Due));

        // The report: the order at the site, with the contributors named, and the kind with its documents.
        var orders = result.Report.Lines.Where(line => line.Class == ModReportClass.OrderedInteraction).ToList();
        Assert.Equal(2, orders.Count);
        Assert.All(orders, order => Assert.Equal(["test.b", "test.a"], order.Mods));
        Assert.Equal("0x9a (himegari.experience.after_calculation): handlers run in this order: test.b data/b.toml "
                     + "(test.owner lists.lua:apply) on test.owner.award (priority -5), test.a data/a.toml (test.owner "
                     + "lists.lua:apply) on test.owner.award (priority 0), test.a a.lua:on_award on test.owner.award (priority 0)",
                     orders[0].Message);
        Assert.Equal("test.owner.computed: subscribers run in this order: test.b data/b.toml (test.owner lists.lua:apply) "
                     + "(priority -5), test.a data/a.toml (test.owner lists.lua:apply) (priority 0)", orders[1].Message);
        Assert.Equal(["kind test.lists (test.owner), at test.owner.award and test.owner.computed",
                      "  test.b data/b.toml: 3 values",
                      "  test.a data/a.toml: 5 values",
                      "  t.k: test.b, then test.a (shared path, in delivery order)"],
                     result.Report.Lua.SkipWhile(line => !line.StartsWith("kind ", StringComparison.Ordinal)));
        Assert.Contains(result.Report.Lua, line => line.StartsWith("event test.owner.computed (test.owner, raised from Lua)", StringComparison.Ordinal)
                                                   && line.EndsWith("subscribers test.b data/b.toml (test.owner lists.lua:apply) (priority -5), "
                                                                    + "test.a data/a.toml (test.owner lists.lua:apply) (priority 0)", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOwnerWithoutAContributorLinksAsItDidAndGetsNoHandler()
    {
        // Gate 1, at the link: an event with a kind and no data file plans nothing new.
        ModLinkResult result = Link(Owner());
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Empty(result.Lua.Stack.Handlers);
        Assert.Empty(result.Lua.Stack.ContributionList);
        Assert.Null(result.Lua.Stack.Contributions);
        Assert.True(result.Lua.Plan.IsEmpty);
        Assert.Contains("kind test.lists (test.owner), at test.owner.award and test.owner.computed: no data files", result.Report.Lua);
    }

    [Fact]
    public void AnOwnerMayShipFilesOfItsOwnKind()
    {
        ModPackage owner = Mod("test.owner", LuaTable + """
            [[accepts]]
            kind = "test.lists"

            [[accepts.at]]
            event = "test.owner.computed"
            handler = "lists.lua:apply"

            """ + Data("own.toml"),
            new() { ["events/owner.toml"] = OwnerEvents, ["lua/lists.lua"] = "function apply(e, c) end", ["data/own.toml"] = "x = 1" });
        ModLinkResult result = Link(owner);
        Assert.True(result.Succeeded, result.Report.Render());
        ModContribution own = Assert.Single(result.Lua.Stack.ContributionList);
        Assert.Equal(("test.owner", "test.owner", "data/own.toml"), (own.Owner, own.Mod, own.File));
        Assert.Empty(own.Requires);
    }

    [Fact]
    public void AKindWithOneEventNotRaisedStillLinksAndSaysSo()
    {
        ModPackage owner = Owner("""
            [[accepts]]
            kind = "test.partial"

            [[accepts.at]]
            event = "test.owner.award"
            handler = "lists.lua:apply"

            [[accepts.at]]
            event = "test.owner.unbound"
            handler = "lists.lua:apply"
            """);
        ModLinkResult result = Link(owner, Contributor("test.a", "x = 1", kind: "test.partial"));
        Assert.True(result.Succeeded, result.Report.Render());
        ModContribution contribution = Assert.Single(result.Lua.Stack.ContributionList);
        Assert.Equal(["test.owner.unbound"], contribution.Unraised);
        Assert.Single(Assert.Single(result.Lua.Plan.Sites["ADDEXP"]).Handlers);
        ModReportLine note = Assert.Single(result.Report.Lines, line => line.Class == ModReportClass.Note
                                                                      && line.Message.StartsWith("kind test.partial", StringComparison.Ordinal));
        Assert.Equal(["test.owner", "test.a"], note.Mods);
        Assert.Contains("test.owner.unbound is not raised in this install", note.Message);
        Assert.EndsWith("the files: test.a data/a.toml", note.Message);
        Assert.Contains("kind test.partial (test.owner), at test.owner.award; not raised in this install: test.owner.unbound",
                        result.Report.Lua);
    }

    // ------------------------------------------------------------------------------------------- gate 2: what stops

    [Fact]
    public void AKindNoModAcceptsStopsTheLoad()
    {
        ModReportLine line = Stop(Link(Owner(), Contributor("test.a", "x = 1", kind: "test.nothing")),
                                  "data/a.toml: no mod of the stack accepts the kind test.nothing (it accepts test.lists)");
        Assert.Equal(["test.a"], line.Mods);
        // With no Lua in the stack at all, no mod can accept a kind.
        ModPackage alone = Mod("test.alone", Data("a.toml"), new() { ["data/a.toml"] = "x = 1" });
        Assert.Equal(["test.alone"], Stop(Link(alone), "data/a.toml: no mod of the stack accepts the kind test.lists").Mods);
    }

    [Fact]
    public void AnOwnerTheContributorDoesNotRequireStopsTheLoad()
    {
        ModPackage contributor = Mod("test.a", Data("a.toml"), new() { ["data/a.toml"] = "x = 1" });
        ModReportLine line = Stop(Link(Owner(), contributor), "data/a.toml: test.owner accepts test.lists, and the mod does not require it");
        Assert.Equal(["test.a"], line.Mods);
    }

    [Theory]
    [InlineData("a = 1\n[t\n", "data/a.toml:2:")]
    [InlineData("when = 1979-05-27", "data/a.toml: when: a date or a time has no value here")]
    [InlineData("big = 9007199254740993", "data/a.toml:1: the integer 9007199254740993 is past 2^53 in size")]
    [InlineData("k1.k2.k3.k4.k5.k6.k7.k8.k9.k10.k11.k12.k13.k14.k15.k16.k17 = 1", "tables and arrays nest deeper than 16")]
    [InlineData("a = 1\na = 2", "data/a.toml:2: The key `a` is already defined")]
    public void AFileThatDoesNotReadStopsTheLoadWithItsLineOrPath(string text, string problem)
    {
        ModReportLine line = Stop(Link(Owner(), Contributor("test.a", text)), problem);
        Assert.Equal(["test.a"], line.Mods);
    }

    [Fact]
    public void AFileWithManyProblemsListsTwentyAndCountsTheRest()
    {
        string text = string.Concat(Enumerable.Range(0, 25).Select(k => $"d{k} = 1979-05-27\n"));
        ModLinkResult result = Link(Owner(), Contributor("test.a", text));
        Assert.Equal(21, result.Report.Lines.Count(line => line.Stops));
        Stop(result, "data/a.toml: and 5 more problems");
    }

    [Fact]
    public void AKindDeclaredTwiceIsAHardConflictNamingBothOwners()
    {
        ModPackage rival = Mod("test.rival", LuaTable + Requires() + """
            [[accepts]]
            kind = "test.lists"

            [[accepts.at]]
            event = "test.owner.award"
            handler = "r.lua:apply"
            """, new() { ["lua/r.lua"] = NeverAtLink + "function apply(e, c) end" });
        ModLinkResult result = Link(Owner(), rival, Contributor("test.a", "x = 1"));
        ModReportLine line = Stop(result, "test.owner and test.rival both accept the kind test.lists");
        Assert.Equal(ModReportClass.HardConflict, line.Class);
        Assert.Equal(["test.owner", "test.rival"], line.Mods);
        // The contributor's file names a broken kind, which says no more.
        Assert.DoesNotContain(result.Report.Lines, found => found.Mods.Contains("test.a") && found.Stops);
    }

    [Fact]
    public void AHandlerThatIsNotATopLevelFunctionStopsTheLoad()
    {
        ModReportLine line = Stop(Link(Owner(lists: "local function apply(e, c) end"), Contributor("test.a", "x = 1")),
                                  "accepts[0] test.lists: at[0] test.owner.award: lua/lists.lua declares no top-level function apply");
        Assert.Equal(["test.owner"], line.Mods);
    }

    [Fact]
    public void AnEventTheOwnerCannotSubscribeToStopsTheLoad()
    {
        ModPackage rival = Mod("test.rival", LuaTable + """
            [[accepts]]
            kind = "test.other"

            [[accepts.at]]
            event = "test.owner.award"
            handler = "r.lua:apply"

            [[accepts]]
            kind = "test.missing"

            [[accepts.at]]
            event = "test.nobody.declares"
            handler = "r.lua:apply"
            """, new() { ["lua/r.lua"] = NeverAtLink + "function apply(e, c) end" });
        ModLinkResult result = Link(Owner(), rival);
        Assert.Equal(["test.rival"], Stop(result, "accepts[0] test.other: at[0] test.owner.award: test.owner declares the event, "
                                                  + "and the mod does not require it").Mods);
        Assert.Equal(["test.rival"], Stop(result, "accepts[1] test.missing: at[0] test.nobody.declares: no mod of the stack "
                                                  + "declares the event").Mods);
    }

    [Fact]
    public void AKindNoneOfWhoseEventsIsRaisedStopsItsContributors()
    {
        ModPackage owner = Owner("""
            [[accepts]]
            kind = "test.none"

            [[accepts.at]]
            event = "test.owner.unbound"
            handler = "lists.lua:apply"
            """);
        // Without a contributor the owner links.
        Assert.True(Link(owner).Succeeded);
        ModReportLine line = Stop(Link(owner, Contributor("test.a", "x = 1", kind: "test.none")),
                                  "data/a.toml: none of the events of test.none is raised in this install, so the file would never be delivered: "
                                  + "test.owner.unbound:");
        Assert.Equal(["test.a"], line.Mods);
    }

    [Theory]
    [InlineData("data/extra.toml", "'data/extra.toml' is not named by a [[data]] entry")]
    [InlineData("manifest", "data: a package without [game] holds no data files")]
    [InlineData("native", "game.target: a mod with data files targets \"extended\"")]
    public void APackageThatDoesNotOpenStopsTheLoadNamingItsFile(string what, string problem)
    {
        string folder = what switch
        {
            "manifest" => Folder("test.a", Data("a.toml"), new() { ["data/a.toml"] = "x = 1" },
                                 head: "omp = 4\nid = \"test.a\"\nname = \"a\"\nversion = \"1.0.0\"\n\n"),
            "native" => Folder("test.a", Data("a.toml"), new() { ["data/a.toml"] = "x = 1" },
                               head: "omp = 4\nid = \"test.a\"\nname = \"a\"\nversion = \"1.0.0\"\n\n[game]\nprofile = \"himegari\"\ntarget = \"native\"\n\n"),
            _ => Folder("test.a", Data("a.toml"), new() { ["data/a.toml"] = "x = 1", [what] = "y = 2" }),
        };
        var report = new ModLinkReport();
        ModDiscovery discovery = ModLaunch.Discover(Directory.CreateDirectory(Path.Combine(_root, "no-mods")).FullName, [folder], report);
        foreach (ModPackage opened in discovery.Packages) opened.Dispose();
        ModReportLine line = Assert.Single(report.Lines, found => found.Stops);
        Assert.Equal(["test.a"], line.Mods);
        Assert.Contains(problem, line.Message);
    }

    // ------------------------------------------------------------------------------------------- the report

    [Fact]
    public void TheReportListsAtMostTwentySharedPathsAndNamesAModWithTwoFilesByFile()
    {
        string Many(int value) => string.Concat(Enumerable.Range(0, 25).Select(k => $"k{k} = {value}\n"));
        ModPackage a = Mod("test.a", Requires() + Data("one.toml") + Data("two.toml"),
                           new() { ["data/one.toml"] = "\"odd key\" = 1\n" + Many(1), ["data/two.toml"] = "k0 = 9\n" });
        ModPackage b = Contributor("test.b", Many(2) + "\"odd key\" = 2\n");
        ModLinkResult result = Link(Owner(), a, b);
        Assert.True(result.Succeeded, result.Report.Render());
        var kind = result.Report.Lua.SkipWhile(line => !line.StartsWith("kind ", StringComparison.Ordinal)).ToList();
        Assert.Equal("  test.a data/one.toml: 26 values", kind[1]);
        Assert.Equal("  test.a data/two.toml: 1 value", kind[2]);
        Assert.Equal("  test.b data/a.toml: 26 values", kind[3]);
        // 26 shared paths, in the order the first document gives them: a key that is not bare is quoted.
        Assert.Equal("  \"odd key\": test.a data/one.toml, then test.b (shared path, in delivery order)", kind[4]);
        Assert.Equal("  k0: test.a data/one.toml, then test.a data/two.toml, then test.b (shared path, in delivery order)", kind[5]);
        Assert.Equal(ModLuaLinker.SharedPathsShown, kind.Count(line => line.Contains("(shared path", StringComparison.Ordinal)));
        Assert.Equal("  and 6 more shared paths", kind[^1]);
    }

    // ------------------------------------------------------------------------------------------- shapes (spec §7)

    /// <summary>test.lists' shape: a list of names and a table of whole numbers.</summary>
    private const string ListsShape = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "properties": {
            "names": { "type": "array", "items": { "type": "string" } },
            "t": { "type": "object", "properties": { "k": { "type": "integer" }, "j": { "type": "boolean" },
                   "old": { "type": "null", "description": "t.old is kept by the game: write k" } },
                   "additionalProperties": false }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>The owner with test.lists' shape, whose Lua raises an error at its top level as <see cref="Owner"/>'s.</summary>
    private ModPackage ShapedOwner(string shape = ListsShape)
        => Mod("test.owner", LuaTable + """
            [[accepts]]
            kind = "test.lists"
            description = "Lists of names."
            shape = "lists.schema.json"

            [[accepts.at]]
            event = "test.owner.award"
            handler = "lists.lua:apply"

            """, new()
        {
            ["events/owner.toml"] = OwnerEvents, ["lua/lists.lua"] = NeverAtLink + "function apply(event, contribution) end",
            ["shapes/lists.schema.json"] = shape,
        });

    [Fact]
    public void ADocumentThatPassesItsShapeIsDelivered()
    {
        ModLinkResult result = Link(ShapedOwner(), Contributor("test.a", "names = [\"x\"]\n[t]\nk = 1\nj = true\n"));
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal(["data/a.toml"], result.Lua.Stack.ContributionList.Select(found => found.File));
        Assert.Single(result.Lua.Plan.Sites["ADDEXP"].Single().Handlers);
    }

    [Fact]
    public void ADocumentThatFailsItsShapeStopsTheLinkWithTheContributorTheFileTheLineThePathAndTheNearestNames()
    {
        // Gate 12 at the link: no mod code has run, since the owner's Lua raises at its top level.
        ModLinkResult result = Link(ShapedOwner(), Contributor("test.a", "names = [\"x\", 2]\n\n[t]\nkk = 1\nold = 3\n"));
        Assert.False(result.Succeeded);
        var stops = result.Report.Lines.Where(line => line.Stops).ToList();
        Assert.Equal([
            "data/a.toml:1: names[2]: takes a string, not 2",
            "data/a.toml:4: t.kk: no such key; the nearest names here are k and j",
            "data/a.toml:5: t.old: t.old is kept by the game: write k",
        ], stops.Select(line => line.Message));
        Assert.All(stops, line => Assert.Equal(["test.a"], line.Mods));
        Assert.Empty(result.Lua.Stack.ContributionList);
        Assert.DoesNotContain(result.Report.Lines, line => line.Message.Contains("never at link", StringComparison.Ordinal));

        // Past the first 20 problems, the rest are counted.
        string many = "[t]\n" + string.Concat(Enumerable.Range(0, 25).Select(k => $"x{k} = 1\n"));
        Stop(Link(ShapedOwner(), Contributor("test.b", many)), "data/a.toml: and 5 more problems");
    }

    [Theory]
    [InlineData("""{ "type": "object", "required": ["names"] }""", "#/required: the keyword required is outside the shape subset")]
    [InlineData("""{ "$ref": "#/definitions/none" }""", "#/$ref: names no definition (#/definitions/none)")]
    [InlineData("not json", "line 1: not JSON")]
    public void ABrokenShapeIsItsOwnersErrorAndStopsTheKindWithoutALineForEachContributor(string shape, string problem)
    {
        ModLinkResult result = Link(ShapedOwner(shape), Contributor("test.a", "names = [\"x\"]\n"));
        ModReportLine line = Stop(result, problem);
        Assert.Equal(["test.owner"], line.Mods);
        Assert.StartsWith("accepts[0] test.lists: shapes/lists.schema.json: ", line.Message);
        Assert.DoesNotContain(result.Report.Lines, other => other.Mods.Contains("test.a"));

        // Without a contributor too: the shape is read at the link whether or not a file of its kind is there.
        Stop(Link(ShapedOwner(shape)), problem);
    }

    [Fact]
    public void AShapeThatTakesMoreThanItsStepsIsItsOwnersError()
    {
        const string costly = """
            { "$ref": "#/definitions/deep",
              "definitions": { "deep": { "oneOf": [ { "$ref": "#/definitions/deep2" }, { "$ref": "#/definitions/deep2" } ] },
                               "deep2": { "type": "object", "additionalProperties": { "$ref": "#/definitions/deep" } } } }
            """;
        ModLinkResult result = Link(ShapedOwner(costly), Contributor("test.a", "a.b.c.d.e.f.g.h.i.j.k.l.m.n.o = 1\n"));
        ModReportLine line = Stop(result, "the shape takes more than 100,960 steps on test.a data/a.toml (64 for each of its 15 "
                                          + "values and 100,000)");
        Assert.Equal(["test.owner"], line.Mods);
        Assert.Empty(result.Lua.Stack.ContributionList);
    }

    [Fact]
    public void OmpLuaStubsCopiesEachKindsShapeAsItsOwnerShipsIt()
    {
        // Plan §6.2: the shapes of the kinds the stack accepts; the profile's base mod's is added only beside its
        // definitions (ModLuaLinkTests), so none here.
        ModLinkResult result = Link(ShapedOwner(), Contributor("test.a", "names = [\"x\"]\n"));
        Assert.True(result.Succeeded, result.Report.Render());
        IReadOnlyDictionary<string, string> shapes = LuaStubs.Shapes(Base(), result, _opened, []);
        Assert.Equal(["test.lists"], shapes.Keys);
        Assert.Equal(ListsShape, shapes["test.lists"]);
        Assert.Equal("test.lists.schema.json", LuaStubs.ShapeFileName("test.lists"));
    }

    // ------------------------------------------------------------------------------------------- the cache

    [Fact]
    public void TheCacheKeepsTheFilesAndReadsThemAgainAtTheMount()
    {
        ModLinkBase linkBase = Base();
        ModPackage a = Contributor("test.a", "names = [\"x\", \"y\"]\n[sub.\"q k\"]\nn = 1.5\n", "lists/a.toml");
        ModLinkResult result = ModLinker.Link(linkBase, [Owner(), a], ["test.owner", "test.a"]);
        Assert.True(result.Succeeded, result.Report.Render());
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "k", linkBase, result);
        string kept = Path.Combine(cache, "k", "data", "test.a", "lists", "a.toml");
        Assert.Equal("names = [\"x\", \"y\"]\n[sub.\"q k\"]\nn = 1.5\n", File.ReadAllText(kept));

        MountedModLayer layer = ModLinkCache.TryOpen(cache, "k")!;
        ModContribution read = Assert.Single(layer.LuaStack.ContributionList);
        ModContribution linked = Assert.Single(result.Lua.Stack.ContributionList);
        Assert.Equal((linked.Kind, linked.Owner, linked.Mod, linked.File, linked.Values, linked.Document.ToText()),
                     (read.Kind, read.Owner, read.Mod, read.File, read.Values, read.Document.ToText()));
        Assert.Equal(linked.Requires, read.Requires);
        Assert.Equal(result.Lua.Plan.Sites["ADDEXP"].Single().Handlers, layer.HookPlan.Sites["ADDEXP"].Single().Handlers);
        Assert.Equal(result.Lua.Plan.SubscribersOf("test.owner.computed"), layer.HookPlan.SubscribersOf("test.owner.computed"));

        // A missing file makes the entry incomplete, so the link runs again.
        File.Delete(kept);
        Assert.Null(ModLinkCache.TryOpen(cache, "k"));
    }

    /// <summary>Plan §12.6 (the review's defect D): a kept copy that no longer reads, or reads to another size than the
    /// link's, as after an engine whose reader changed, makes the entry a miss with its reason, so the launch links again
    /// with a note instead of throwing when the engine mounts the stack.</summary>
    [Fact]
    public void ACachedCopyThatNoLongerReadsIsAMissAndTheStackIsLinkedAgainWithANote()
    {
        ModLinkBase linkBase = Base();
        ModPackage owner = Owner(), a = Contributor("test.a", "names = [\"x\", \"y\"]\n", "lists/a.toml");
        string mods = Path.Combine(_root, "mods");
        ModStackResult first = ModStack.Prepare(linkBase, [owner, a], ["test.owner", "test.a"], mods);
        Assert.True(first.Succeeded, first.Report.Render());
        string kept = Path.Combine(first.Layer!.Directory, "data", "test.a", "lists", "a.toml");
        string cache = Path.Combine(mods, ModLinkCache.FolderName);

        File.WriteAllText(kept, "names = [\"x\"]\nnames = [\"y\"]\n");
        Assert.Null(ModLinkCache.TryOpen(cache, first.Layer.Key, out string? unread));
        Assert.Equal("its copy of test.a data/lists/a.toml no longer reads: data/lists/a.toml:2: The key `names` is already "
                     + "defined at (1,1) and cannot be redefined", unread);

        ModStackResult again = ModStack.Prepare(linkBase, [owner, a], ["test.owner", "test.a"], mods);
        Assert.True(again.Succeeded, again.Report.Render());
        Assert.False(again.FromCache);
        Assert.Contains(again.Report.Lines, line => line.Class == ModReportClass.Note
                                                    && line.Message == $"{ModLinkCache.UnreadNote} does not read, so the stack "
                                                    + $"was linked again: {unread}");
        Assert.Equal(3, Assert.Single(again.Layer!.LuaStack.ContributionList).Values);
        Assert.True(ModStack.Prepare(linkBase, [owner, a], ["test.owner", "test.a"], mods).FromCache);

        // A copy that reads to another size than the link counted is a miss too.
        File.WriteAllText(kept, "names = [\"x\", \"y\", \"z\"]\n");
        Assert.Null(ModLinkCache.TryOpen(cache, again.Layer.Key, out unread));
        Assert.Equal("its copy of test.a data/lists/a.toml reads as 4 values, where the link read 3", unread);
    }

    [Fact]
    public void AStackWithoutDataWritesNoContributionsToTheCache()
    {
        ModLinkBase linkBase = Base();
        ModLinkResult result = ModLinker.Link(linkBase, [Owner()], ["test.owner"]);
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "k", linkBase, result);
        Assert.DoesNotContain("contribution", File.ReadAllText(Path.Combine(cache, "k", MountedModLayer.ManifestFile)));
        Assert.False(Directory.Exists(Path.Combine(cache, "k", "data")));
    }
}
