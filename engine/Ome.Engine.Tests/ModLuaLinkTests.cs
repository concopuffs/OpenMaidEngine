using System.Text;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Mods.Tools;
using Ome.Script.Abi;
using Ome.Script.Patch;
using Ome.Scripting.Lua;

/// <summary>
/// The Lua scripting spec's gate 3 (the link refuses what does not link, naming the mods, before any mod code runs)
/// and gate 4 (a Lua handler on <c>himegari.experience.after_calculation</c> does what the guide's OMS patch does).
/// The synthetic cases link against a base with one script and one patch point; the cases that patch or run the real
/// <c>ADDEXP.BIN</c> need the install.
/// </summary>
public sealed class ModLuaLinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-lua-link-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = new();
    private readonly LazyModRuntime _runtime = LuaModRuntime.Lazy();

    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeTable HimegariTable = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);
    private const string Installed = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string AddExp = "ADDEXP.BIN";
    private const string AddExpFingerprint = "sha256:b4f38c43f2c500012be48517ce773d3c9e81c12deeb4d712cab4b99749661e1c";
    private const string ExperiencePoint = "himegari.experience.after_calculation";
    private static readonly byte[] SyntheticAddExp = Encoding.ASCII.GetBytes("base ADDEXP.BIN");

    public void Dispose()
    {
        foreach (ModPackage mod in _opened) mod.Dispose();
        _runtime.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A base with ADDEXP.BIN and the experience point at 0x9a of its one revision.</summary>
    private ModLinkBase SyntheticBase(bool lua = true) => new()
    {
        Profile = "himegari",
        CatalogFingerprint = Installed,
        ReadScript = name => name.Equals(AddExp, StringComparison.OrdinalIgnoreCase) ? SyntheticAddExp : null,
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
        PatchPoints = new OmsPatchPoints([new OmsPatchPoint(ExperiencePoint, AddExp, null,
            [new OmsPatchPointSite(OmsPatchPlan.FingerprintOf(SyntheticAddExp), 0x9a)])]),
        Lua = lua ? _runtime : null,
    };

    private ModLinkBase InstalledBase()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        return ModLinkBase.ForInstall("himegari", Paths.Sys4Ini, catalog, store, HimegariTable, Contracts,
            Paths.GlobalsJson("himegari"), Paths.OmsModules("himegari"), Paths.PatchPointsJson("himegari")) with
        {
            Lua = _runtime,
        };
    }

    /// <summary>A version 3 package: <paramref name="tables"/> after <c>[lua]</c> (hooks, requires), and its files.
    /// Without <paramref name="game"/> it is a library.</summary>
    private ModPackage Mod(string id, string tables = "", Dictionary<string, string>? files = null,
                           string lua = "api = 1\ncapabilities = [\"vm\"]", bool game = true)
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        string gameTable = game ? "[game]\nprofile = \"himegari\"\ntarget = \"extended\"\n\n" : "";
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 3\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n\n{gameTable}[lua]\n{lua}\n\n{tables}\n");
        foreach (var (path, text) in files ?? new Dictionary<string, string>())
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        return mod;
    }

    private static string PointHook(string handler = "hooks.lua:on_award", string point = ExperiencePoint, string extra = "")
        => $"[[hooks]]\npoint = \"{point}\"\nhandler = \"{handler}\"\n{extra}";

    private static Dictionary<string, string> Hooks(string text) => new() { ["lua/hooks.lua"] = text };

    private const string Doubles = "function on_award(ctx) maid.vm.l[0x1] = maid.vm.l[0x1] * 2 end";

    private static IEnumerable<ModReportLine> Stopping(ModLinkResult result) => result.Report.Lines.Where(line => line.Stops);

    private static ModReportLine Stop(ModLinkResult result, string contains)
    {
        Assert.False(result.Succeeded);
        return Assert.Single(Stopping(result), line => line.Message.Contains(contains, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------- gate 3: a clean link

    [Fact]
    public void APointHookLinksToItsSiteWithOneHandlerIdPerFunctionAndRunsNoModCode()
    {
        // The file raises an error at its top level: the link only parses it.
        ModPackage mod = Mod("double-exp", PointHook() + "\n" + PointHook(extra: "priority = 5") + "\n"
                                           + "[[hooks]]\nengine = \"ome.session.start\"\nhandler = \"hooks.lua:on_start\"\n"
                                           + "[[hooks]]\nengine = \"ome.session.resumed\"\nhandler = \"hooks.lua:on_start\"\n"
                                           + "[[hooks]]\nengine = \"ome.save.writing\"\nhandler = \"hooks.lua:on_start\"\n",
            Hooks("error('never at link')\n" + Doubles + "\nfunction on_start(e) end"));
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [mod], []);

        Assert.True(result.Succeeded, result.Report.Render());
        ModRuntimeMod linked = Assert.Single(result.Lua.Stack.Mods);
        Assert.Equal(("double-exp", "1.0.0", false, true), (linked.Id, linked.Version, linked.Library, linked.Vm));
        Assert.Equal(["hooks.lua"], linked.Files.Select(file => file.Path));
        Assert.Equal([new ModRuntimeHandler("double-exp", "hooks.lua", "on_award"),
                      new ModRuntimeHandler("double-exp", "hooks.lua", "on_start")], result.Lua.Stack.Handlers);
        PlannedSite site = Assert.Single(result.Lua.Plan.Sites["ADDEXP"]);
        Assert.Equal((0x9a, 0x9a, ExperiencePoint), (site.Offset, site.Site, site.Point));
        // One handler at two priorities: ascending priority, one id.
        Assert.Equal([(0, 0), (0, 5)], site.Handlers.Select(handler => (handler.Handler.Index, handler.Priority)));
        // The three engine events this engine raises (spec §7.7), each bound to its one handler id.
        foreach (string engineEvent in new[] { "ome.session.start", "ome.session.resumed", "ome.save.writing" })
            Assert.Equal(1, Assert.Single(result.Lua.Plan.HandlersOf(engineEvent)).Handler.Index);
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.OrderedInteraction
                                                     && line.Message.Contains("handlers run in this order", StringComparison.Ordinal));
        // Spec §15: the report says which mods may change any game state.
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.Note && line.Mods.SequenceEqual(["double-exp"])
                                                     && line.Message.Contains("has the vm capability", StringComparison.Ordinal));
    }

    [Fact]
    public void ALibraryLinksWithoutAGameAndAModWithoutLuaLinksWithoutARuntime()
    {
        ModPackage library = Mod("names-lib", files: new() { ["lua/init.lua"] = "return { name = 'x' }" },
                                 lua: "api = 1", game: false);
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [library], []);
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.True(Assert.Single(result.Lua.Stack.Mods).Library);

        // A build without a runtime fails only a stack that ships Lua.
        Assert.Contains("this build has no Lua runtime", Stop(ModLinker.Link(SyntheticBase(lua: false), [library], []),
                                                              "no Lua runtime").Message);
        Assert.False(_runtime.Created && ModLinker.Link(SyntheticBase(lua: false), [], []).Lua.Stack.HasLua);
    }

    // ------------------------------------------------------------------------------------------- gate 3: refusals

    [Fact]
    public void ASyntaxErrorStopsTheLinkWithItsPositionAndTheMod()
    {
        ModPackage mod = Mod("broken", PointHook(), Hooks("function on_award(ctx)\n  local x = = 1\nend"));
        ModReportLine line = Stop(ModLinker.Link(SyntheticBase(), [mod], []), "lua/hooks.lua: line 2");
        Assert.Equal(["broken"], line.Mods);
        // The report gives the position once: not the library's own CHUNK:LINE: and name besides.
        Assert.DoesNotContain("hooks.lua:2", line.Message);
        Assert.DoesNotContain("Lua-CSharp", line.Message);

        // A wrong point does not depend on the file, so a broken file does not hide it.
        ModPackage both = Mod("both", PointHook(point: "himegari.nowhere"), Hooks("function on_award(ctx)\n  local x = = 1\nend"));
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [both], []);
        Assert.Contains(Stopping(result), stop => stop.Message.Contains("lua/hooks.lua: line 2", StringComparison.Ordinal));
        Assert.Contains(Stopping(result), stop => stop.Message.Contains("'himegari.nowhere' is not a named patch point", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("function other(ctx) end", "declares no top-level function on_award")]
    [InlineData("local function on_award(ctx) end", "declares no top-level function on_award")]
    [InlineData("M = {} function M.on_award(ctx) end", "declares no top-level function on_award")]
    [InlineData("on_award = function(ctx) end", "declares no top-level function on_award")]
    public void AHandlerMustBeATopLevelFunctionOfItsFile(string source, string message)
        => Assert.Equal(["m"], Stop(ModLinker.Link(SyntheticBase(), [Mod("m", PointHook(), Hooks(source))], []), message).Mods);

    [Fact]
    public void APointHookNeedsTheVmCapabilityAndAKnownPoint()
    {
        ModPackage noVm = Mod("no-vm", PointHook(), Hooks(Doubles), lua: "api = 1");
        Stop(ModLinker.Link(SyntheticBase(), [noVm], []), "needs the vm capability");
        ModPackage unknown = Mod("unknown", PointHook(point: "himegari.nowhere"), Hooks(Doubles));
        Stop(ModLinker.Link(SyntheticBase(), [unknown], []), "'himegari.nowhere' is not a named patch point");
    }

    [Fact]
    public void APointWhoseRevisionTheRegistryDoesNotListIsAWrongFingerprint()
    {
        ModLinkBase other = SyntheticBase() with { ReadScript = name => Encoding.ASCII.GetBytes("another revision") };
        ModPackage mod = Mod("m", PointHook(), Hooks(Doubles));
        Assert.Contains(OmsPatchPlan.FingerprintOf(SyntheticAddExp),
                        Stop(ModLinker.Link(other, [mod], []), "has no site for ADDEXP.BIN").Message);
    }

    [Theory]
    [InlineData("[[hooks]]\nscript = \"ADDEXP.BIN\"\nfingerprint = \"" + AddExpFingerprint + "\"\nsite = 0x9a\nwhen = \"after\"\nhandler = \"hooks.lua:on_award\"",
                "site hooks come with the wider hooks")]
    [InlineData("[[hooks]]\nscript = \"ADDEXP.BIN\"\nwhen = \"enter\"\nhandler = \"hooks.lua:on_award\"", "script entry and exit hooks")]
    [InlineData("[[hooks]]\nwatch = \"current_unit_id\"\nhandler = \"hooks.lua:on_award\"", "watches come with the wider hooks")]
    [InlineData("[[hooks]]\npoint = \"" + ExperiencePoint + "\"\nskip = true\nhandler = \"hooks.lua:on_award\"", "skip = true comes with")]
    [InlineData("[[hooks]]\nengine = \"ome.text.present\"\nhandler = \"hooks.lua:on_award\"", "is reserved for the localization filter")]
    [InlineData("[[hooks]]\nengine = \"ome.nothing.here\"\nhandler = \"hooks.lua:on_award\"", "is not an engine event")]
    public void TheLinkRefusesByNameWhatThisEngineDoesNotBuild(string hook, string message)
        => Assert.Equal(["m"], Stop(ModLinker.Link(SyntheticBase(), [Mod("m", hook, Hooks(Doubles))], []), message).Mods);

    [Theory]
    [InlineData("api = 1\nwrites = [\"stage_turn_limit\"]", "names cells of a base mod, and the mod requires none")]
    [InlineData("api = 1\ncalls = [\"ADDEXP.BIN\"]", "script calls come with")]
    public void AWriteListWithoutABaseModAndScriptCallsAreRefused(string lua, string message)
        => Stop(ModLinker.Link(SyntheticBase(), [Mod("m", files: Hooks(Doubles), lua: lua)], []), message);

    [Fact]
    public void AModuleNamedLikeARequiredModIsAnError()
    {
        ModPackage library = Mod("names", files: new() { ["lua/init.lua"] = "return {}" }, lua: "api = 1", game: false);
        ModPackage user = Mod("user", "[[requires]]\nid = \"names\"\nversion = \">=1\"",
                              new() { ["lua/names.lua"] = "return 1" });
        ModReportLine line = Stop(ModLinker.Link(SyntheticBase(), [library, user], []), "also the id of a mod it requires");
        Assert.Equal(["user"], line.Mods);
    }

    [Fact]
    public void AHookOnAScriptAnotherModReplacesNamesTheReplacingMod()
    {
        ModPackage replacer = Mod("replacer", files: new() { ["replace/ADDEXP.BIN"] = "replacement" }, lua: "api = 1");
        ModPackage hooker = Mod("hooker", PointHook(), Hooks(Doubles));
        ModReportLine line = Stop(ModLinker.Link(SyntheticBase(), [replacer, hooker], []), "replacer replaces ADDEXP.BIN");
        Assert.Equal(ModReportClass.HardConflict, line.Class);
        Assert.Equal(["hooker", "replacer"], line.Mods.Order(StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------------------------------- gate 3 on the install

    private static string Patch(string body)
        => $"oms 1\npatch \"{AddExp}\" fingerprint \"{AddExpFingerprint}\"\nuse himegari.globals\n\nlocal int exp @ 0x1\n\n{body}\n";

    private ModPackage OmsMod(string id, string body) => OmsMod(id, "himegari", Patch(body));

    /// <summary>A version 1 package of <paramref name="profile"/> with one fragment, <paramref name="patch"/>.</summary>
    private ModPackage OmsMod(string id, string profile, string patch)
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(Path.Combine(folder, "patches"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), $"""
            omp = 1
            id = "{id}"
            name = "{id}"
            version = "1.0.0"

            [game]
            profile = "{profile}"
            target = "native"
            """);
        File.WriteAllText(Path.Combine(folder, "patches", "addexp.oms"), patch);
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        return mod;
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void AHookOnAnInstructionAPatchReplacedNamesBothModsAndAPatchBeforeItMovesTheHook()
    {
        ModLinkBase linkBase = InstalledBase();
        ModPackage hooker = Mod("hooker", PointHook(), Hooks(Doubles));

        ModPackage replacing = OmsMod("replacing", "replace site 0x9a through 0x9a {\n    exp = exp\n}");
        ModReportLine line = Stop(ModLinker.Link(linkBase, [replacing, hooker], []), "replaced the instruction at 0x9a");
        Assert.Equal(ModReportClass.HardConflict, line.Class);
        Assert.Equal(["hooker", "replacing"], line.Mods.Order(StringComparer.Ordinal));

        // The guide's patch inserts before the point: the hook binds to the original instruction, which moved.
        ModPackage inserting = OmsMod("inserting", $"before site {ExperiencePoint} {{\n    exp = exp * 2\n}}");
        ModLinkResult result = ModLinker.Link(linkBase, [inserting, hooker], []);
        Assert.True(result.Succeeded, result.Report.Render());
        PlannedSite site = Assert.Single(result.Lua.Plan.Sites["ADDEXP"]);
        Assert.Equal(0x9a, site.Site);
        Assert.True(site.Offset > 0x9a);
        Assert.Equal(site.Offset, result.Scripts[AddExp].Offsets!.Map(0x9a));

        // The cache keeps the sources and the plan, and the layer maps the linked offset back.
        string cache = Path.Combine(_root, "cache");
        MountedModLayer layer = ModLinkCache.Write(cache, "key", linkBase, result);
        MountedModLayer reopened = ModLinkCache.TryOpen(cache, "key")!;
        Assert.NotNull(reopened);
        Assert.Equal(result.Lua.Stack.Handlers, reopened.LuaStack.Handlers);
        Assert.Equal(Doubles, Assert.Single(Assert.Single(reopened.LuaStack.Mods).Files).Text);
        PlannedSite cached = Assert.Single(reopened.HookPlan.Sites["ADDEXP"]);
        Assert.Equal((site.Offset, site.Site, site.Point), (cached.Offset, cached.Site, cached.Point));
        Assert.Equal(0x9a, reopened.ScriptSites.Site(AddExp, site.Offset));
        Assert.Null(reopened.ScriptSites.Site(AddExp, 0x9a));   // the inserted instruction: no original
        Assert.Equal(["inserting"], reopened.ScriptSites.ChangedBy(AddExp));
        Assert.Equal(0x10, reopened.ScriptSites.Site("SC0000", 0x10));
        Assert.Equal(layer.Manifest.Lua!.Runtime, LuaModRuntime.RuntimeIdentity);

        // A missing source makes the entry incomplete.
        File.Delete(Path.Combine(reopened.Directory, LinkedLuaEntry.PathOf("hooker", "hooks.lua")));
        Assert.Null(ModLinkCache.TryOpen(cache, "key"));
    }

    // ------------------------------------------------------------------------------- base mods and write lists (§9.3)

    private const string BaseLayout = """
        {"layout": 1, "profile": "himegari", "globals": [
        {"name": "stage_turn_limit", "bank": "G", "address": "0x11", "dims": [], "aliases": ["turn_limit"]},
        {"name": "unit_current_levels", "bank": "G", "address": "0x100", "dims": [4]}
        ]}
        """;

    private ModPackage BaseMod() => Mod("test-base", files: new() { ["layout/globals.json"] = BaseLayout });

    private ModPackage Writer(string writes)
        => Mod("writer", "[[requires]]\nid = \"test-base\"\nversion = \">=1, <2\"",
               new() { ["lua/init.lua"] = "return {}" }, lua: $"api = 1\nwrites = [{writes}]");

    [Fact]
    public void ABaseModJoinsTheStackWithItsLayoutAndAWriteListBecomesGlobalNames()
    {
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [Writer("\"turn_limit\", \"unit_current_levels\", \"stage_turn_limit\""),
                                                                BaseMod()], []);
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal(["test-base", "writer"], result.Lua.Stack.Mods.Select(mod => mod.Id));
        ModRuntimeMod baseMod = result.Lua.Stack.Mods[0], writer = result.Lua.Stack.Mods[1];
        Assert.Empty(baseMod.Files);
        Assert.Equal(["stage_turn_limit", "unit_current_levels"], baseMod.Layout!.Globals.Select(global => global.Name));
        Assert.Null(writer.Layout);
        Assert.Equal(["stage_turn_limit", "unit_current_levels"], writer.WriteList);

        // The cache keeps the layout and the list.
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", SyntheticBase(), result);
        MountedModLayer reopened = ModLinkCache.TryOpen(cache, "key")!;
        Assert.Equal(BaseLayout, reopened.LuaStack.Mods[0].Layout!.Json);
        Assert.Equal(writer.WriteList, reopened.LuaStack.Mods[1].WriteList);
        File.Delete(Path.Combine(reopened.Directory, LinkedLuaEntry.LayoutPathOf("test-base")));
        Assert.Null(ModLinkCache.TryOpen(cache, "key"));
    }

    [Fact]
    public void AListedNameTheBaseModDoesNotExportIsALinkError()
    {
        ModReportLine line = Stop(ModLinker.Link(SyntheticBase(), [BaseMod(), Writer("\"stage_turn_limit\", \"no_such_name\"")], []),
                                  "lua.writes: 'no_such_name' is not a name test-base exports");
        Assert.Equal(["writer"], line.Mods);
    }

    [Fact]
    public void TheShippedBaseModIsMountedForAModThatRequiresItAndItsListIsCheckedAgainstIt()
    {
        ModPackage user = Mod("user", "[[requires]]\nid = \"himegari-base\"\nversion = \">=1, <2\"",
                              new() { ["lua/init.lua"] = "return {}" }, lua: "api = 1\nwrites = [\"stage_turn_limit\"]");
        var report = new ModLinkReport();
        IReadOnlyList<ModPackage> shipped = ShippedPackages.Required([user], report);
        _opened.AddRange(shipped);
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [user, .. shipped], []);
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal(["himegari-base", "user"], result.Lua.Stack.Mods.Select(mod => mod.Id));
        Assert.True(result.Lua.Stack.Mods[0].Layout!.TryGet("unit_current_levels", out _));
    }

    [Fact]
    public void TheCacheKeyNamesTheRuntimeOnlyForAStackWithLua()
    {
        ModPackage lua = Mod("m", PointHook(), Hooks(Doubles));
        ModPackage plain = Mod("plain", lua: "api = 1");   // a [lua] table takes part in the Lua link
        var none = new Dictionary<string, int>();
        ModLinkBase withRuntime = SyntheticBase(), other = SyntheticBase() with { Lua = new LazyModRuntime("other", () => null!) };
        Assert.NotEqual(ModLinkCache.ComputeKey(withRuntime, [lua], none), ModLinkCache.ComputeKey(other, [lua], none));
        Assert.Equal(ModLinkCache.ComputeKey(withRuntime, [], none), ModLinkCache.ComputeKey(other, [], none));
        Assert.True(ModLuaLinker.ShipsLua(plain));
    }

    // ------------------------------------------------------------------------------- the write scan and --lua-dev (§9.3)

    private const string Writes = """
        local game = require("test-base")
        game.g.turn_limit = 1
        game.g.stage_turn_limit = 2
        game.table("unit_current_levels")[1] = 3
        game.g.no_such = 4
        """;

    private ModPackage ScannedWriter(string writes = "\"turn_limit\"", string id = "writer")
        => Mod(id, RequiresBase, new() { ["lua/init.lua"] = Writes }, lua: $"api = 1\nwrites = [{writes}]");

    [Fact]
    public void TheWriteScanReportsANameTheListLacksWithTheListAsItShouldRead()
    {
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [BaseMod(), ScannedWriter()], []);
        Assert.True(result.Succeeded, result.Report.Render());
        // An alias in the source is its global; the list already names it.
        ModUndeclaredWrite write = Assert.Single(result.Lua.Undeclared);
        Assert.Equal(new ModUndeclaredWrite("writer", "init.lua", "unit_current_levels"), write);
        ModReportLine note = Assert.Single(result.Report.Lines, line => line.Message.StartsWith("the source writes names", StringComparison.Ordinal));
        Assert.Equal((ModReportClass.Note, "writer"), (note.Class, note.Mods.Single()));
        Assert.Equal("the source writes names its [lua] writes list lacks: unit_current_levels (lua/init.lua); each write fails "
                     + "when it runs, and omp build refuses the mod. The list should read writes = [\"stage_turn_limit\", "
                     + "\"unit_current_levels\"]", note.Message);
        Assert.Contains(result.Report.Lines, line => line.Message == "lua/init.lua writes no_such by name, which test-base does not "
                                                                     + "export; that write fails when it runs");
        Assert.DoesNotContain(result.Report.Lines, line => line.Message.Contains("milestone 3", StringComparison.Ordinal));

        // A base mod's own names have no list, and a mod that requires no base mod names no game cells.
        ModPackage baseWithLua = Mod("test-base", files: new() { ["layout/globals.json"] = BaseLayout,
                                                                ["lua/init.lua"] = "maid.names.g.stage_turn_limit = 1 return {}" });
        ModPackage plain = Mod("plain", files: new() { ["lua/init.lua"] = "local t = { g = {} } t.g.anything = 1" }, lua: "api = 1");
        ModLinkResult quiet = ModLinker.Link(SyntheticBase(), [baseWithLua, plain], []);
        Assert.True(quiet.Succeeded, quiet.Report.Render());
        Assert.Empty(quiet.Lua.Undeclared);
        Assert.DoesNotContain(quiet.Report.Lines, line => line.Message.Contains("by name", StringComparison.Ordinal)
                                                          || line.Message.Contains("the source writes", StringComparison.Ordinal));
    }

    [Fact]
    public void OmpBuildRefusesAModWhoseSourceWritesANameItsListLacks()
    {
        string baseFolder = BaseMod().Location;
        var console = new StringWriter();
        Assert.Equal(1, OmpCommands.Build(SyntheticBase(), ScannedWriter().Location, Path.Combine(_root, "refused.omp"),
                                          [baseFolder], console));
        Assert.Contains("omp build: writer writes names its [lua] writes list lacks: unit_current_levels (lua/init.lua)",
                        console.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "refused.omp")));

        console = new StringWriter();
        int built = OmpCommands.Build(SyntheticBase(), ScannedWriter("\"turn_limit\", \"unit_current_levels\"").Location,
                                      Path.Combine(_root, "out", "built.omp"), [baseFolder], console);
        Assert.True(built == 0, console.ToString());
        Assert.True(File.Exists(Path.Combine(_root, "out", "built.omp")));   // the output's folder is made
    }

    [Fact]
    public void LuaDevLetsAnUnpackedModsUndeclaredWriteThroughAndKeepsAPackedModsList()
    {
        Assert.True(Ome.Mods.Launch.ModLaunchOptions.Resolve(["--lua-dev"], _root).LuaDev);
        Assert.False(Ome.Mods.Launch.ModLaunchOptions.Resolve([], _root).LuaDev);
        ModLinkBase dev = SyntheticBase() with { LuaDev = true };

        ModLinkResult result = ModLinker.Link(dev, [BaseMod(), ScannedWriter()], []);
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.True(result.Lua.Stack.Mods.Single(mod => mod.Id == "writer").LuaDev);
        Assert.False(result.Lua.Stack.Mods.Single(mod => mod.Id == "test-base").LuaDev);
        Assert.Contains(result.Report.Lines, line => line.Message.StartsWith("runs under --lua-dev", StringComparison.Ordinal));
        Assert.Contains(result.Report.Lines, line => line.Message.Contains("--lua-dev lets them through", StringComparison.Ordinal));
        // The cache keeps the mark, and the flag is part of the key.
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", dev, result);
        Assert.True(ModLinkCache.TryOpen(cache, "key")!.LuaStack.Mods.Single(mod => mod.Id == "writer").LuaDev);
        IReadOnlyList<ModPackage> stack = [BaseMod(), ScannedWriter()];
        Assert.NotEqual(ModLinkCache.ComputeKey(dev, stack, new Dictionary<string, int>()),
                        ModLinkCache.ComputeKey(SyntheticBase(), stack, new Dictionary<string, int>()));

        // A packed mod's list is enforced.
        string packed = Path.Combine(_root, "writer-packed.omp");
        Assert.Equal(0, OmpCommands.Build(SyntheticBase(), ScannedWriter("\"turn_limit\", \"unit_current_levels\"", "writer").Location,
                                          packed, [BaseMod().Location], new StringWriter()));
        ModPackage omp = ModPackage.Open(packed);
        _opened.Add(omp);
        ModLinkResult packedResult = ModLinker.Link(dev, [BaseMod(), omp], []);
        Assert.True(packedResult.Succeeded, packedResult.Report.Render());
        Assert.False(packedResult.Lua.Stack.Mods.Single(mod => mod.Id == "writer").LuaDev);
        Assert.Contains(packedResult.Report.Lines, line => line.Message == "is packed, so --lua-dev does not apply: its [lua] writes list is enforced");
    }

    // ------------------------------------------------------------------------------------------- events (§9.2, gate 5)

    private const string Events = """
        [[event]]
        name = "test.award"
        point = "himegari.experience.after_calculation"

        [[event.field]]
        name = "amount"
        at = "L[0x1]"
        type = "int"
        access = "rw"

        [[event.field]]
        name = "limit"
        at = "turn_limit"
        type = "int"

        [[event]]
        name = "test.computed"
        raised_by = "lua"

        [[event.field]]
        name = "amount"
        type = "int"
        access = "rw"
        """;

    private ModPackage EventBase(string events = Events, string? points = null)
    {
        var files = new Dictionary<string, string> { ["layout/globals.json"] = BaseLayout, ["events/award.toml"] = events };
        if (points != null) files["layout/points.json"] = points;
        return Mod("test-base", files: files);
    }

    private const string RequiresBase = "[[requires]]\nid = \"test-base\"\nversion = \">=1, <2\"\n";

    private static string EventHook(string name, int priority = 0, string handler = "hooks.lua:on_award")
        => $"[[hooks]]\nevent = \"{name}\"\nhandler = \"{handler}\"\npriority = {priority}\n";

    private const string Subscribes = "function on_award(ctx) ctx.amount = ctx.amount * 2 end";

    /// <summary>A mod without vm that subscribes to the events it is given.</summary>
    private ModPackage Subscriber(string id, string hooks, bool requires = true)
        => Mod(id, (requires ? RequiresBase : "") + hooks, Hooks(Subscribes), lua: "api = 1");

    [Fact]
    public void AnEventAtAPointJoinsItsInstructionsListInTheMergedOrderAndTheCacheKeepsIt()
    {
        ModLinkResult result = ModLinker.Link(SyntheticBase(),
            [EventBase(), Subscriber("first", EventHook("test.award")), Subscriber("last", EventHook("test.award", 10)),
             Mod("hooker", PointHook(extra: "priority = 5"), Hooks(Doubles))], []);
        Assert.True(result.Succeeded, result.Report.Render());

        Assert.Equal(["test.award", "test.computed"], result.Lua.Stack.EventList.Select(gameEvent => gameEvent.Name));
        GameEvent award = result.Lua.Stack.Event("test.award")!;
        Assert.Equal(("test-base", ExperiencePoint), (award.Owner, award.Point));
        Assert.Equal([new GameEventField("amount", EventFieldType.Int, true, "L[0x1]", new FieldLocation.Local(CellKind.Integer, 1)),
                      new GameEventField("limit", EventFieldType.Int, false, "turn_limit",
                                         new FieldLocation.Global(CellKind.Integer, 0x11, "stage_turn_limit"))], award.Fields);

        // One instruction, one list: the subscribers and the point hook by priority, each subscriber naming its event.
        PlannedSite site = Assert.Single(result.Lua.Plan.Sites["ADDEXP"]);
        Assert.Equal((0x9a, ExperiencePoint), (site.Offset, site.Point));
        Assert.Equal([("first", "test.award"), ("hooker", null), ("last", "test.award")],
                     site.Handlers.Select(handler => (result.Lua.Stack[handler.Handler].ModId, handler.GameEvent)));
        ModReportLine order = Assert.Single(result.Report.Lines, line => line.Class == ModReportClass.OrderedInteraction);
        Assert.EndsWith("handlers run in this order: first hooks.lua:on_award on test.award (priority 0), hooker hooks.lua:on_award "
                        + "(priority 5), last hooks.lua:on_award on test.award (priority 10)", order.Message);

        // The cache keeps the events and which handlers subscribe.
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", SyntheticBase(), result);
        MountedModLayer reopened = ModLinkCache.TryOpen(cache, "key")!;
        Assert.Equal(result.Lua.Stack.EventList.Select(gameEvent => (gameEvent.Name, gameEvent.Owner, gameEvent.Point)),
                     reopened.LuaStack.EventList.Select(gameEvent => (gameEvent.Name, gameEvent.Owner, gameEvent.Point)));
        Assert.Equal(result.Lua.Stack.EventList.SelectMany(gameEvent => gameEvent.Fields),
                     reopened.LuaStack.EventList.SelectMany(gameEvent => gameEvent.Fields));
        Assert.Equal(site.Handlers, Assert.Single(reopened.HookPlan.Sites["ADDEXP"]).Handlers);
    }

    [Fact]
    public void AnEventRaisedFromLuaHasItsSubscribersInTheirOwnOrder()
    {
        ModLinkResult result = ModLinker.Link(SyntheticBase(),
            [EventBase(), Subscriber("late", EventHook("test.computed", 3)), Subscriber("early", EventHook("test.computed", 1))], []);
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Empty(result.Lua.Plan.Sites);
        Assert.Equal(["early", "late"], result.Lua.Plan.SubscribersOf("test.computed")
                                           .Select(handler => result.Lua.Stack[handler.Handler].ModId));
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.OrderedInteraction
                                                     && line.Message.StartsWith("test.computed: subscribers run in this order: ",
                                                                                StringComparison.Ordinal));
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", SyntheticBase(), result);
        Assert.Equal(result.Lua.Plan.SubscribersOf("test.computed"),
                     ModLinkCache.TryOpen(cache, "key")!.HookPlan.SubscribersOf("test.computed"));
    }

    [Fact]
    public void TheReportListsEachModsWritesEachHookInOrderAndEachEventWithItsSubscribers()
    {
        // Spec §12, gate 15: each mod's capabilities and writes, the hooks and the events; two mods on one name.
        ModPackage first = Mod("first", RequiresBase + EventHook("test.award"), Hooks(Subscribes),
                               lua: "api = 1\nwrites = [\"turn_limit\", \"unit_current_levels\"]");
        ModPackage second = Mod("second", RequiresBase + EventHook("test.computed", 2), Hooks(Subscribes),
                                lua: "api = 1\nwrites = [\"stage_turn_limit\"]");
        ModPackage hooker = Mod("hooker", PointHook(extra: "priority = 5")
                                          + "\n[[hooks]]\nengine = \"ome.session.start\"\nhandler = \"hooks.lua:on_start\"\n",
                                Hooks(Doubles + "\nfunction on_start(e) end"));
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [EventBase(), first, second, hooker], []);
        Assert.True(result.Succeeded, result.Report.Render());

        ModReportLine shared = Assert.Single(result.Report.Lines, line => line.Message.StartsWith("stage_turn_limit ", StringComparison.Ordinal));
        Assert.Equal(ModReportClass.OrderedInteraction, shared.Class);
        Assert.Equal(["first", "second"], shared.Mods);
        Assert.Equal("stage_turn_limit is written by first, then second (load order); the trace shows each write (--lua-trace)",
                     shared.Message);
        Assert.Contains(result.Report.Lines, line => line.Mods.SequenceEqual(["hooker"])
                                                     && line.Message.StartsWith("has the vm capability, so it is opaque",
                                                                                StringComparison.Ordinal));
        // The mods in load order: hooker requires nothing, so it may load first. Compared as text, so a difference shows
        // where it is.
        Assert.Equal(string.Join("\n", (string[])[
            "mod hooker 1.0.0: capabilities vm (opaque); writes no name, and any cell by address",
            "mod test-base 1.0.0: base mod of 2 globals; capabilities vm (opaque); writes its own names, with no list",
            "mod first 1.0.0: capabilities none; writes stage_turn_limit, unit_current_levels through test-base",
            "mod second 1.0.0: capabilities none; writes stage_turn_limit through test-base",
            $"hook ADDEXP 0x9a ({ExperiencePoint}): first hooks.lua:on_award on test.award (priority 0), "
            + "hooker hooks.lua:on_award (priority 5)",
            "hook ome.session.start: hooker hooks.lua:on_start (priority 0)",
            $"event test.award (test-base, at {ExperiencePoint}, ADDEXP 0x9a): fields amount int rw, limit int r; "
            + "subscribers first hooks.lua:on_award (priority 0)",
            "event test.computed (test-base, raised from Lua): fields amount int rw; subscribers second hooks.lua:on_award (priority 2)",
        ]), string.Join("\n", result.Report.Lua));
        // The section follows the findings, one line each, indented.
        string[] rendered = result.Report.Render().Split(Environment.NewLine);
        int section = Array.IndexOf(rendered, "lua:");
        Assert.True(section > 0 && rendered[1..section].All(line => !line.StartsWith("  ", StringComparison.Ordinal)));
        Assert.Equal(result.Report.Lua.Select(line => "  " + line), rendered[(section + 1)..^1]);
        Assert.Equal("", rendered[^1]);
        // Under --lua-dev a folder mod is opaque too.
        ModLinkResult dev = ModLinker.Link(SyntheticBase() with { LuaDev = true }, [EventBase(), first], []);
        Assert.Contains("mod first 1.0.0: capabilities none; writes stage_turn_limit, unit_current_levels through test-base, and "
                        + "under --lua-dev (opaque) any exported name its list lacks", dev.Report.Lua);
        Assert.Contains(dev.Report.Lines, line => line.Message.StartsWith("runs under --lua-dev, so it is opaque", StringComparison.Ordinal));
        // A stack without Lua has no section.
        Assert.Empty(ModLinker.Link(SyntheticBase(), [], []).Report.Lua);
    }

    [Fact]
    public void TheStartupReportKeepsTheLuaSectionWhenItLinksAndWhenItReusesTheCache()
    {
        string profile = Path.Combine(_root, "profile");
        string diagnostics = Path.Combine(profile, "diagnostics");
        ModPackage hooker = Mod("hooker", PointHook(), Hooks(Doubles));
        var options = Ome.Mods.Launch.ModLaunchOptions.Resolve(["--mod", hooker.Location], _root);
        foreach (bool cached in new[] { false, true })
        {
            Ome.Mods.Launch.ModLaunchResult launched = Ome.Mods.Launch.ModLaunch.Run(SyntheticBase(), profile, diagnostics, options);
            Assert.False(launched.Failed, launched.Report.Render());
            Assert.Equal(cached, launched.FromCache);
            string text = File.ReadAllText(Path.Combine(diagnostics, Ome.Mods.Launch.ModLaunch.ReportFile));
            Assert.Contains("  mod hooker 1.0.0: capabilities vm (opaque); writes no name, and any cell by address", text);
        }
    }

    [Fact]
    public void OmpLuaStubsDescribesTheStackAndTheProfilesBaseModWhenNoModMountsIt()
    {
        // Spec §13: with no mods, the shipped himegari-base and its experience award, for a mod not yet written.
        string output = Path.Combine(_root, "stubs");
        var console = new StringWriter();
        Assert.True(OmpCommands.LuaStubs(SyntheticBase(), null, [], output, console) == 0, console.ToString());
        Assert.Equal(["events.lua", "himegari-base.lua", "maid.lua"],
                     Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        string names = File.ReadAllText(Path.Combine(output, "himegari-base.lua"));
        Assert.StartsWith("---@meta himegari-base\n", names);
        Assert.Contains("---@class himegari_base.g\n", names);
        Assert.DoesNotContain("\r", names);
        Assert.Contains("---@class himegari.experience.award\n", File.ReadAllText(Path.Combine(output, "events.lua")));
        Assert.Contains("omp lua-stubs: 3 files (himegari-base; 1 events)", console.ToString());
        Assert.Contains("\"workspace.library\"", console.ToString());

        // A stack's own base mod and events come first; the profile's is added beside them.
        string withStack = Path.Combine(_root, "stubs-stack");
        console = new StringWriter();
        Assert.True(OmpCommands.LuaStubs(SyntheticBase(), null, [EventBase().Location], withStack, console) == 0, console.ToString());
        Assert.Equal(["events.lua", "himegari-base.lua", "maid.lua", "test-base.lua"],
                     Directory.GetFiles(withStack).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        string events = File.ReadAllText(Path.Combine(withStack, "events.lua"));
        Assert.Contains("---@class test.award\n", events);
        Assert.Contains("---@class himegari.experience.award\n", events);

        // A stack that does not link writes nothing.
        string broken = Path.Combine(_root, "stubs-broken");
        console = new StringWriter();
        Assert.Equal(1, OmpCommands.LuaStubs(SyntheticBase(), null, [Mod("broken", PointHook("hooks.lua:missing"), Hooks(Doubles)).Location],
                                             broken, console));
        Assert.Contains("omp lua-stubs: the stack does not link", console.ToString());
        Assert.False(Directory.Exists(broken));
    }

    [Fact]
    public void AnUndeclaredEventAndASubscriberThatDoesNotRequireItsOwnerAreLinkErrors()
    {
        ModReportLine line = Stop(ModLinker.Link(SyntheticBase(), [EventBase(), Subscriber("m", EventHook("test.none"))], []),
                                  "no mod of the stack declares the event 'test.none' (it declares test.award, test.computed)");
        Assert.Equal(["m"], line.Mods);
        Stop(ModLinker.Link(SyntheticBase(), [Subscriber("m", EventHook("test.none"), requires: false)], []),
             "no mod of the stack declares the event 'test.none'");
        line = Stop(ModLinker.Link(SyntheticBase(), [EventBase(), Subscriber("m", EventHook("test.award"), requires: false)], []),
                    "test-base declares test.award, and the mod does not require it");
        Assert.Equal(["m"], line.Mods);
    }

    [Fact]
    public void TwoModsThatDeclareOneEventAreAHardConflictAndItsSubscribersReportNothingMore()
    {
        ModPackage other = Mod("other", RequiresBase, new() { ["events/mine.toml"] =
            "[[event]]\nname = \"test.computed\"\nraised_by = \"lua\"\n", ["lua/init.lua"] = "return {}" }, lua: "api = 1");
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [EventBase(), other, Subscriber("m", EventHook("test.computed"))], []);
        ModReportLine line = Stop(result, "test-base and other both declare the event test.computed");
        Assert.Equal(ModReportClass.HardConflict, line.Class);
        Assert.Equal(["other", "test-base"], line.Mods.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("[[event]]\nname = \"test.award\"\npoint = \"" + ExperiencePoint + "\"\n[[event.field]]\nname = \"limit\"\nat = \"no_such\"\ntype = \"int\"",
                "events/award.toml: test.award: field limit: 'no_such': the layout names no global 'no_such'")]
    [InlineData("[[event]]\nname = \"test.award\"\npoint = \"himegari.nowhere\"",
                "events/award.toml: test.award: 'himegari.nowhere' is not a named patch point of himegari")]
    [InlineData("[[event]]\nname = \"test.award\"\nraised_by = \"lua\"\n[[event.field]]\nname = \"x\"\nat = \"L[1]\"\ntype = \"int\"",
                "events/award.toml: test.award: field x: an event raised from Lua has no locations")]
    [InlineData("[[event]\n", "events/award.toml: not valid TOML")]
    public void ADeclarationThatDoesNotLinkStopsTheLinkNamingItsFile(string events, string message)
        => Assert.Equal(["test-base"], Stop(ModLinker.Link(SyntheticBase(), [EventBase(events), Subscriber("m", EventHook("test.award"))], []),
                                            message).Mods);

    [Fact]
    public void AnEventAtAPointNeedsTheVmCapabilityInItsDeclaringMod()
    {
        ModPackage declarer = Mod("declarer", files: new() { ["events/x.toml"] =
            "[[event]]\nname = \"test.award\"\npoint = \"" + ExperiencePoint + "\"\n[[event.field]]\nname = \"amount\"\nat = \"L[1]\"\ntype = \"int\"\n" },
            lua: "api = 1");
        Assert.Equal(["declarer"], Stop(ModLinker.Link(SyntheticBase(), [declarer], []),
                                        "an event at a point reads the frame's cells, which needs the vm capability").Mods);
    }

    [Fact]
    public void APointLinksThroughTheBaseModsCopyWhenTheProfileHasNoPatchPoints()
    {
        string points = $$"""
            {"profile": "himegari", "points": [{"name": "{{ExperiencePoint}}", "script": "ADDEXP.BIN",
             "at": [{"fingerprint": "{{OmsPatchPlan.FingerprintOf(SyntheticAddExp)}}", "site": 154}]}]}
            """;
        ModLinkBase noPoints = SyntheticBase() with { PatchPoints = null };
        ModLinkResult result = ModLinker.Link(noPoints,
            [EventBase(points: points), Subscriber("m", EventHook("test.award")), Mod("hooker", PointHook(), Hooks(Doubles))], []);
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal(2, Assert.Single(result.Lua.Plan.Sites["ADDEXP"]).Handlers.Count);

        Stop(ModLinker.Link(noPoints, [EventBase(), Subscriber("m", EventHook("test.award"))], []),
             "'" + ExperiencePoint + "' is not a named patch point of himegari");
    }

    /// <summary>The shipped example that doubles each award without vm (spec §9.4), and the base mod it requires.</summary>
    private IReadOnlyList<ModPackage> ShippedAward()
    {
        ModPackage user = ModPackage.Open(Path.Combine(Paths.Repo, "oms", "examples", "mods", "example.double-exp-event"));
        _opened.Add(user);
        IReadOnlyList<ModPackage> shipped = ShippedPackages.Required([user], new ModLinkReport());
        _opened.AddRange(shipped);
        return [user, .. shipped];
    }

    [Fact]
    public void TheShippedBaseModDeclaresTheExperienceAwardAtItsPoint()
    {
        ModLinkResult result = ModLinker.Link(SyntheticBase(), ShippedAward(), []);
        Assert.True(result.Succeeded, result.Report.Render());
        GameEvent award = Assert.Single(result.Lua.Stack.EventList);
        Assert.Equal(("himegari.experience.award", "himegari-base", ExperiencePoint), (award.Name, award.Owner, award.Point));
        Assert.Equal(["amount", "unit", "level"], award.Fields.Select(field => field.Name));
        Assert.Equal(new FieldLocation.Global(CellKind.Integer, 0x66715, "current_unit_id"), award.Field("unit")!.Location);
        var level = Assert.IsType<FieldLocation.Element>(award.Field("level")!.Location);
        Assert.Equal(("unit_current_levels", 0x6930), (level.Table.Name, level.Table.Address));
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void ASubscriberOfTheExperienceAwardDoesWhatTheGuidesPatchDoesWithoutVm()
    {
        ModLinkBase linkBase = InstalledBase();
        ModLinkResult patched = ModLinker.Link(linkBase,
            [OmsMod("double-exp-oms", $"before site {ExperiencePoint} {{\n    exp = exp * 2\n}}")], []);
        Assert.True(patched.Succeeded, patched.Report.Render());
        string withPatch = Award(Sys4Loader.Parse(patched.Scripts[AddExp].Bytes, HimegariTable, AddExp), 7).ToJson();

        ModLinkResult linked = ModLinker.Link(linkBase, ShippedAward(), []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        using var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt,
        });
        string withEvent = Award(Sys4Loader.Parse(linkBase.ReadScript(AddExp)!, HimegariTable, AddExp), 7, scripting).ToJson();
        Assert.DoesNotContain(scripting.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));
        Assert.Equal(14, GameSession.FromJson(withEvent).Globals[0x6994 + 1]);
        Assert.Equal(withPatch, withEvent);
    }

    // ------------------------------------------------------------------------------------------- gate 4

    /// <summary>ADDEXP awarding <paramref name="amount"/> to the first unit, with no multiplier, from a session whose
    /// banks the award reads.</summary>
    private static GameSession Award(Script addExp, long amount, Scripting? scripting = null)
    {
        var session = new GameSession();
        session.Seed(0x62ccb, amount);      // the award
        session.Seed(0x3302, 0);            // the multiplier bits: none
        session.Seed(0x152616, 0);          // current_entity_index 0: ADDEXP takes current_unit_id as it is
        session.Seed(0x66715, 1);           // current_unit_id
        session.Seed(0x6930 + 1, 1);        // unit_current_levels[1]: level 1
        session.Seed(0x7a766 + 1, 99);      // unit_level_cap[1]: below the cap, so the award applies
        session.Seed(0x6994 + 1, 0);        // unit_experience_progress[1]: 100 is a level, which the award stays under
        SceneResult result = session.RunScene(addExp, HimegariTable, new HeadlessWaits(), scripting: scripting);
        Assert.True(result.Halt is null or "exit" or "end", result.Halt);
        return session;
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void ALuaHandlerOnTheExperiencePointDoesWhatTheGuidesPatchDoes()
    {
        ModLinkBase linkBase = InstalledBase();
        byte[] baseBytes = linkBase.ReadScript(AddExp)!;
        Assert.Equal(AddExpFingerprint, OmsPatchPlan.FingerprintOf(baseBytes));

        // The guide's OMS patch (docs/modding/games/himegari.md, "Patch points").
        ModLinkResult patched = ModLinker.Link(linkBase,
            [OmsMod("double-exp-oms", $"before site {ExperiencePoint} {{\n    exp = exp * 2\n}}")], []);
        Assert.True(patched.Succeeded, patched.Report.Render());
        string withPatch = Award(Sys4Loader.Parse(patched.Scripts[AddExp].Bytes, HimegariTable, AddExp), 7).ToJson();

        // The same award with a Lua handler at the point, on the base script: the shipped example mod.
        ModPackage example = ModPackage.Open(Path.Combine(Paths.Repo, "oms", "examples", "mods", "example.double-exp-lua"));
        _opened.Add(example);
        ModLinkResult linked = ModLinker.Link(linkBase, [example], []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        using var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt,
        });
        Script baseScript = Sys4Loader.Parse(baseBytes, HimegariTable, AddExp);
        string withLua = Award(baseScript, 7, scripting).ToJson();
        Assert.DoesNotContain(scripting.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));

        GameSession unmodded = Award(baseScript, 7);
        Assert.Equal(7, unmodded.Globals[0x6994 + 1]);
        Assert.Equal(14, GameSession.FromJson(withPatch).Globals[0x6994 + 1]);
        Assert.Equal(withPatch, withLua);
    }

    // ------------------------------------------------------------------------------------------ gate 13: a second game

    private const string KamidoriPoint = "kamidori.experience.after_calculation";
    private const string KamidoriAddExp = "sha256:606b83e952a3ce45ee406a2383710734f1ea707288f5751b5614be50e3edd7b6";
    private const string KamidoriTranslatedAddExp = "sha256:23bb6ac1e1aa83ef248bbacba4cadd71801fa07188aed3938c4768034f52b40b";
    private static readonly OpcodeTable KamidoriTable = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");

    /// <summary>Kamidori's install at <paramref name="gameRoot"/> (its loose files first in
    /// <paramref name="looseRoots"/>), with the script provider ADDEXP's calls resolve through.</summary>
    private (ModLinkBase Base, Sys4ScriptProvider Scripts) KamidoriBase(string gameRoot, params string[] looseRoots)
    {
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");
        var catalog = Sys4AssetCatalog.Load(sys4Ini);
        var store = new Sys4AssetStore(catalog, gameRoot, looseRoots.Length > 0 ? looseRoots : [gameRoot]);
        ModLinkBase linkBase = ModLinkBase.ForInstall("kamidori", sys4Ini, catalog, store, KamidoriTable, Contracts,
            Paths.GlobalsJson("kamidori"), Paths.OmsModules("kamidori"), Paths.PatchPointsJson("kamidori")) with
        {
            Lua = _runtime,
        };
        return (linkBase, new Sys4ScriptProvider(KamidoriTable, catalog, store));
    }

    /// <summary>Kamidori's ADDEXP awarding <paramref name="amount"/> to character 1, who wears a costume with one
    /// experience gem (0x118, +25%), from a session that seeds the cells the award reads and stays under a level.</summary>
    private static GameSession KamidoriAward(Script addExp, IScriptProvider scripts, long amount, Scripting? scripting = null)
    {
        var session = new GameSession();
        session.Seed(0xfec0c, amount);          // the amount the caller passes
        session.Seed(0xfec0d, 0);               // no weapon experience
        session.Seed(0x2a9b89, 0);              // current_battle_slot 0
        session.Seed(0x53bb2 + 0, 1);           // battle_unit_unit_ids[0]: unit 1
        session.Seed(0x2b6a94 + 1, 1);          // unit_canonical_character_id[1]: character 1
        session.Seed(0x642c9 + 1, 3);           // character_costume_item_id[1]: costume item 3
        session.Seed(0xefed0 + 3 * 4, 0x118);   // item_costume_gem_sockets[3][0]: the experience gem
        session.Seed(0x6432d + 1, 1);           // character_levels[1]: level 1
        session.Seed(0x2bdfc4 + 1, 99);         // unit_level_cap[1]: below the cap, so the award applies
        session.Seed(0x64391 + 1, 0);           // character_experience[1]: 100 is a level, which the award stays under
        SceneResult result = session.RunScene(addExp, KamidoriTable, new HeadlessWaits(), provider: scripts,
                                              build: EngineBuilds.Age433G, scripting: scripting);
        Assert.True(result.Halt is null or "exit" or "end", result.Halt);
        return session;
    }

    /// <summary>The shipped Kamidori example that doubles each award without vm, and the base mod it requires.</summary>
    private IReadOnlyList<ModPackage> ShippedKamidoriAward()
    {
        ModPackage user = ModPackage.Open(Path.Combine(Paths.Repo, "oms", "examples", "mods", "example.kamidori-double-exp-event"));
        _opened.Add(user);
        IReadOnlyList<ModPackage> shipped = ShippedPackages.Required([user], new ModLinkReport());
        _opened.AddRange(shipped);
        return [user, .. shipped];
    }

    /// <summary>The OMS patch that doubles L[0x1] at the Kamidori point of the script <paramref name="fingerprint"/>
    /// names (docs/modding/games/kamidori.md, "Patch points").</summary>
    private ModPackage KamidoriPatch(string fingerprint) => OmsMod("kamidori-double-exp-oms", "kamidori",
        $"oms 1\npatch \"{AddExp}\" fingerprint \"{fingerprint}\"\nuse kamidori.globals\n\nlocal int exp @ 0x1\n\n"
        + $"before site {KamidoriPoint} {{\n    exp = exp * 2\n}}\n");

    /// <summary>Links the shipped example on <paramref name="linkBase"/> and runs the award with it, then with the OMS
    /// patch for <paramref name="fingerprint"/>, and returns both sessions' state and the unmodded award's.</summary>
    private (string WithEvent, string WithPatch, GameSession Unmodded, ModLinkResult Linked) KamidoriAwards(
        ModLinkBase linkBase, Sys4ScriptProvider scripts, string fingerprint, long amount)
    {
        byte[] baseBytes = linkBase.ReadScript(AddExp)!;
        Assert.Equal(fingerprint, OmsPatchPlan.FingerprintOf(baseBytes));
        Script baseScript = Sys4Loader.Parse(baseBytes, KamidoriTable, AddExp);

        ModLinkResult patched = ModLinker.Link(linkBase, [KamidoriPatch(fingerprint)], []);
        Assert.True(patched.Succeeded, patched.Report.Render());
        string withPatch = KamidoriAward(Sys4Loader.Parse(patched.Scripts[AddExp].Bytes, KamidoriTable, AddExp), scripts, amount).ToJson();

        ModLinkResult linked = ModLinker.Link(linkBase, ShippedKamidoriAward(), []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        using var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt,
        });
        string withEvent = KamidoriAward(baseScript, scripts, amount, scripting).ToJson();
        Assert.DoesNotContain(scripting.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));
        return (withEvent, withPatch, KamidoriAward(baseScript, scripts, amount), linked);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void KamidorisBaseModRaisesItsExperienceAwardAndASubscriberDoesWhatThePatchDoes()
    {
        // Spec gate 13: the Kamidori base mod's event at the Kamidori point, with no engine change.
        var (linkBase, scripts) = KamidoriBase(Path.Combine(Paths.Workspace, "Kamidori"));
        var (withEvent, withPatch, unmodded, linked) = KamidoriAwards(linkBase, scripts, KamidoriAddExp, 7);

        GameEvent award = Assert.Single(linked.Lua.Stack.EventList);
        Assert.Equal(("kamidori.experience.award", "kamidori-base", KamidoriPoint), (award.Name, award.Owner, award.Point));
        Assert.Equal(["amount", "unit", "character", "level"], award.Fields.Select(field => field.Name));
        Assert.Equal(new FieldLocation.Global(CellKind.Integer, 0x2d2845, "current_character_id"), award.Field("character")!.Location);
        var level = Assert.IsType<FieldLocation.Element>(award.Field("level")!.Location);
        Assert.Equal(("character_levels", 0x6432d), (level.Table.Name, level.Table.Address));
        Assert.Equal((0x88, KamidoriPoint), (Assert.Single(linked.Lua.Plan.Sites["ADDEXP"]).Offset, Assert.Single(linked.Lua.Plan.Sites["ADDEXP"]).Point));

        // 7 with the gem's 25% is 8 (875 / 100), which the subscriber doubles: 16, not the 17 a doubling before
        // the bonus would give.
        Assert.Equal(8, unmodded.Globals[0x64391 + 1]);
        Assert.Equal(16, GameSession.FromJson(withEvent).Globals[0x64391 + 1]);
        Assert.Equal(withPatch, withEvent);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void TheKamidoriPointHasTheTranslatedInstallsAddExpToo()
    {
        // The translation's patch/ADDEXP.BIN differs from the base in two strings: the point lists its revision, so
        // the same mods link on the translated install.
        string gameRoot = Paths.KamidoriTranslatedDir;
        var (linkBase, scripts) = KamidoriBase(gameRoot, Path.Combine(gameRoot, "patch"), gameRoot);
        var (withEvent, withPatch, _, linked) = KamidoriAwards(linkBase, scripts, KamidoriTranslatedAddExp, 7);
        Assert.Equal(0x88, Assert.Single(linked.Lua.Plan.Sites["ADDEXP"]).Offset);
        Assert.Equal(16, GameSession.FromJson(withEvent).Globals[0x64391 + 1]);
        Assert.Equal(withPatch, withEvent);
    }
}
