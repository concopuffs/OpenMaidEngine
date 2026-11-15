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

    private ModPackage OmsMod(string id, string body)
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(Path.Combine(folder, "patches"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), $"""
            omp = 1
            id = "{id}"
            name = "{id}"
            version = "1.0.0"

            [game]
            profile = "himegari"
            target = "native"
            """);
        File.WriteAllText(Path.Combine(folder, "patches", "addexp.oms"), Patch(body));
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
                                      Path.Combine(_root, "built.omp"), [baseFolder], console);
        Assert.True(built == 0, console.ToString());
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
}
