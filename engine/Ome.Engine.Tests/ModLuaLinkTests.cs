using System.Text;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Linking;
using Ome.Mods.Manifest;
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
    // The shipped base mods call ADDITEM (their [lua] calls), so a stack that mounts one needs a wait-free ADDITEM.BIN.
    private static readonly byte[] SyntheticAddItem = ScriptAssembler.AssembleBytes(new List<(int, Operand[])> { (0x5, []) }, [], "SYS4422");

    public void Dispose()
    {
        foreach (ModPackage mod in _opened) mod.Dispose();
        _runtime.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A base with ADDEXP.BIN and the experience point at 0x9a of its one revision, and a wait-free ADDITEM.BIN.</summary>
    private ModLinkBase SyntheticBase(bool lua = true) => new()
    {
        Profile = "himegari",
        CatalogFingerprint = Installed,
        ReadScript = name => name.Equals(AddExp, StringComparison.OrdinalIgnoreCase) ? SyntheticAddExp
                             : name.Equals("ADDITEM.BIN", StringComparison.OrdinalIgnoreCase) ? SyntheticAddItem : null,
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
                           string lua = "api = 1\ncapabilities = [\"vm\"]", bool game = true, string profile = "himegari")
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        string gameTable = game ? $"[game]\nprofile = \"{profile}\"\ntarget = \"extended\"\n\n" : "";
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
    [InlineData("[[hooks]]\nengine = \"ome.text.present\"\nhandler = \"hooks.lua:on_award\"", "is reserved for the localization filter")]
    [InlineData("[[hooks]]\nengine = \"ome.nothing.here\"\nhandler = \"hooks.lua:on_award\"", "is not an engine event")]
    public void TheLinkRefusesByNameWhatThisEngineDoesNotBuild(string hook, string message)
        => Assert.Equal(["m"], Stop(ModLinker.Link(SyntheticBase(), [Mod("m", hook, Hooks(Doubles))], []), message).Mods);

    [Fact]
    public void AWriteListWithoutABaseModIsRefused()
        => Stop(ModLinker.Link(SyntheticBase(), [Mod("m", files: Hooks(Doubles), lua: "api = 1\nwrites = [\"stage_turn_limit\"]")], []),
                "names cells of a base mod, and the mod requires none");

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

    // ------------------------------------------------------------------------------------------- site hooks (§6.3, §7.1)

    /// <summary>A real ADDEXP.BIN for the site hooks: a mov at 0x0, a call at 0x5, the ADV wait at 0x8, the copy at 0xb
    /// (the experience point of <see cref="SiteBase"/>) and the exit at 0x10; past it, unreached, a jump at 0x11 and a
    /// save at 0x14.</summary>
    private static readonly byte[] SiteAddExp = ScriptAssembler.AssembleBytes(new List<(int, Operand[])>
    {
        (0x55, [new Operand(3, 0x100), new Operand(0, 5)]),
        (0x3, [new Operand(0, 1)]),
        (0x72, [new Operand(0, 0)]),
        (0x55, [new Operand(3, 0x101), new Operand(3, 0x100)]),
        (0x2, []),
        (0x8c, [new Operand(0, 0)]),
        (0x19e, [new Operand(3, 0x20), new Operand(0, 1)]),
    }, [], "SYS4422");

    private static readonly string SiteFingerprint = OmsPatchPlan.FingerprintOf(SiteAddExp);

    /// <summary>A base whose ADDEXP.BIN is <see cref="SiteAddExp"/>, with the experience point at its copy.</summary>
    private ModLinkBase SiteBase() => SyntheticBase() with
    {
        ReadScript = name => name.Equals(AddExp, StringComparison.OrdinalIgnoreCase) ? SiteAddExp : null,
        PatchPoints = new OmsPatchPoints([new OmsPatchPoint(ExperiencePoint, AddExp, null,
            [new OmsPatchPointSite(SiteFingerprint, 0xb)])]),
    };

    private static string SiteHook(int site, string when, string handler = "hooks.lua:on_award", string extra = "",
                                   string script = AddExp, string? fingerprint = null)
        => $"[[hooks]]\nscript = \"{script}\"\nfingerprint = \"{fingerprint ?? SiteFingerprint}\"\nsite = 0x{site:x}\n"
           + $"when = \"{when}\"\nhandler = \"{handler}\"\n{extra}";

    [Fact]
    public void ASiteHookBindsItsOriginalOffsetAndItsAfterHooksTakeTheirOwnList()
    {
        // At the point's instruction: the point hook and a site hook before it, two after it in the merged order.
        ModPackage first = Mod("first", PointHook() + "\n" + SiteHook(0xb, "before", extra: "priority = 1") + "\n"
                                        + SiteHook(0xb, "after", extra: "priority = 1"), Hooks(Doubles));
        ModPackage second = Mod("second", SiteHook(0xb, "after") + "\n" + SiteHook(0x0, "after"), Hooks(Doubles));
        ModLinkResult result = ModLinker.Link(SiteBase(), [first, second], []);

        Assert.True(result.Succeeded, result.Report.Render());
        IReadOnlyList<PlannedSite> sites = result.Lua.Plan.Sites["ADDEXP"];
        Assert.Equal([0x0, 0xb], sites.Select(site => site.Offset));
        Assert.Equal((0x0, null, 0), (sites[0].Site, sites[0].Point, sites[0].Handlers.Count));
        PlannedSite point = sites[1];
        Assert.Equal((0xb, ExperiencePoint), (point.Site, point.Point));
        Assert.Equal([(0, 0, false), (0, 1, true)], point.Handlers.Select(h => (h.LoadOrder, h.Priority, h.Site)));
        Assert.Equal([(1, 0, true), (0, 1, true)], point.AfterList.Select(h => (h.LoadOrder, h.Priority, h.Site)));
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.OrderedInteraction
            && line.Message == $"0xb ({ExperiencePoint}): after it, handlers run in this order: second hooks.lua:on_award "
                               + "(priority 0), first hooks.lua:on_award (priority 1)");
        Assert.Contains("hook ADDEXP 0x0: after: second hooks.lua:on_award (priority 0)", result.Report.Lua);
        Assert.Contains($"hook ADDEXP 0xb ({ExperiencePoint}): first hooks.lua:on_award (priority 0), first hooks.lua:on_award "
                        + "(priority 1); after: second hooks.lua:on_award (priority 0), first hooks.lua:on_award (priority 1)",
                        result.Report.Lua);

        // The cache keeps both lists and which handlers are a site's.
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", SiteBase(), result);
        PlannedSite cached = ModLinkCache.TryOpen(cache, "key")!.HookPlan.Sites["ADDEXP"][1];
        Assert.Equal(point.Handlers, cached.Handlers);
        Assert.Equal(point.AfterList, cached.AfterList);
    }

    /// <summary>A runnable ADDEXP.BIN: L[1] = 3 at 0x0, G[0x100] = L[1] at 0x5 (the experience point of the test
    /// below), the exit at 0xa.</summary>
    private static readonly byte[] RunAddExp = ScriptAssembler.AssembleBytes(new List<(int, Operand[])>
    {
        (0x55, [new Operand(9, 1), new Operand(0, 3)]),
        (0x55, [new Operand(3, 0x100), new Operand(9, 1)]),
        (0x2, []),
    }, [], "SYS4422");

    [Fact]
    public void AnEventAPointHookAndASiteHookOnOneInstructionRunInTheOrderTheReportGives()
    {
        // Spec §7.4, gate 5: one list at the instruction, by priority. Each handler changes L[1] (the event's amount)
        // so that only the reported order leaves 20 in G[0x100].
        string fingerprint = OmsPatchPlan.FingerprintOf(RunAddExp);
        ModLinkBase linkBase = SyntheticBase() with
        {
            ReadScript = name => name.Equals(AddExp, StringComparison.OrdinalIgnoreCase) ? RunAddExp : null,
            PatchPoints = new OmsPatchPoints([new OmsPatchPoint(ExperiencePoint, AddExp, null,
                [new OmsPatchPointSite(fingerprint, 0x5)])]),
        };
        ModPackage pointer = Mod("pointer", PointHook(extra: "priority = 2"),
            Hooks("function on_award(ctx) maid.log.info(\"point\"); maid.vm.l[1] = maid.vm.l[1] * 2 end"));
        ModPackage sited = Mod("sited", SiteHook(0x5, "before", fingerprint: fingerprint),
            Hooks("function on_award(ctx) maid.log.info(\"site\"); maid.vm.l[1] = maid.vm.l[1] * 3 end"));
        ModPackage subscriber = Mod("subscriber", RequiresBase + EventHook("test.award", 1),
            Hooks("function on_award(ctx) maid.log.info(\"event\"); ctx.amount = ctx.amount + 1 end"), lua: "api = 1");
        ModLinkResult result = ModLinker.Link(linkBase, [pointer, EventBase(), subscriber, sited], []);
        Assert.True(result.Succeeded, result.Report.Render());

        ModReportLine order = Assert.Single(result.Report.Lines, line => line.Class == ModReportClass.OrderedInteraction);
        Assert.EndsWith("handlers run in this order: sited hooks.lua:on_award (priority 0), subscriber hooks.lua:on_award on "
                        + "test.award (priority 1), pointer hooks.lua:on_award (priority 2)", order.Message);
        Assert.Contains($"hook ADDEXP 0x5 ({ExperiencePoint}): sited hooks.lua:on_award (priority 0), subscriber "
                        + "hooks.lua:on_award on test.award (priority 1), pointer hooks.lua:on_award (priority 2)", result.Report.Lua);

        using var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = result.Lua.Stack, Plan = result.Lua.Plan, Errors = LuaErrorPolicy.Halt,
        });
        var vm = new VirtualMachine(Sys4Loader.Parse(RunAddExp, HimegariTable, AddExp), HimegariTable, new HeadlessWaits(),
                                    scripting: scripting);
        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(20, vm.Globals[0x100]);   // (3 × 3 + 1) × 2
        Assert.Equal(["site", "event", "point"], scripting.RecentLog.Select(line => line[(line.LastIndexOf(": ", StringComparison.Ordinal) + 2)..])
                                                          .Where(text => text is "site" or "event" or "point"));
    }

    [Theory]
    [InlineData(0xb, "api = 1", null, "a site hook hands its handler the frame and operands, which needs the vm capability")]
    [InlineData(0xb, null, "sha256:2222222222222222222222222222222222222222222222222222222222222222",
                "the hook was written for ADDEXP.BIN sha256:2222")]
    [InlineData(0x6, null, null, "0x6 is not the start of an instruction of ADDEXP.BIN")]
    public void ASiteHookNeedsVmItsRevisionAndAnInstructionsStart(int site, string? lua, string? fingerprint, string message)
    {
        ModPackage mod = lua == null
            ? Mod("m", SiteHook(site, "before", fingerprint: fingerprint), Hooks(Doubles))
            : Mod("m", SiteHook(site, "before", fingerprint: fingerprint), Hooks(Doubles), lua: lua);
        Assert.Equal(["m"], Stop(ModLinker.Link(SiteBase(), [mod], []), message).Mods);
    }

    [Fact]
    public void ASiteHookOnAScriptTheCatalogLacksIsAnError()
        => Stop(ModLinker.Link(SiteBase(), [Mod("m", SiteHook(0x0, "before", script: "NONE.BIN"), Hooks(Doubles))], []),
                "the base catalog has no NONE.BIN");

    // ------------------------------------------------------------------------------------------- script calls (§10)

    /// <summary>Scripts for the calls' check: CALLEE (id 5) calls SUB2 (id 6) and a script from a cell, sleeps and runs
    /// op 0xae; SUB2 saves, calls CALLEE back and returns; SAFE (id 7) only writes a cell.</summary>
    private static readonly Dictionary<string, byte[]> CallScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CALLEE.BIN"] = ScriptAssembler.AssembleBytes(new List<(int, Operand[])>
        {
            (0x55, [new Operand(3, 0x100), new Operand(0, 5)]),
            (0x3, [new Operand(0, 6)]),
            (0x3, [new Operand(3, 0x50)]),
            (0xc8, [new Operand(0, 10)]),
            (0xae, []),
            (0x5, []),
        }, [], "SYS4422"),
        ["SUB2.BIN"] = ScriptAssembler.AssembleBytes(new List<(int, Operand[])>
        {
            (0x19e, [new Operand(3, 0x20), new Operand(0, 1)]),
            (0x3, [new Operand(0, 5)]),
            (0x5, []),
        }, [], "SYS4422"),
        ["SAFE.BIN"] = ScriptAssembler.AssembleBytes(new List<(int, Operand[])>
        {
            (0x55, [new Operand(3, 0x100), new Operand(0, 5)]),
            (0x5, []),
        }, [], "SYS4422"),
    };

    private ModLinkBase CallBase() => SyntheticBase() with
    {
        ReadScript = name => CallScripts.GetValueOrDefault(name),
        Modules = [Ome.Script.Modules.GameModule.Scripts("himegari", [("CALLEE.BIN", 5), ("SUB2.BIN", 6), ("SAFE.BIN", 7)])],
    };

    private const string CallingHook = "[[hooks]]\nengine = \"ome.session.resumed\"\nhandler = \"hooks.lua:on_award\"\n";

    [Fact]
    public void TheScriptsAModCallsAreWalkedThroughTheirCallsAndAWaitASaveOrAMissingScriptIsReportedFirst()
    {
        // Gate 9: omp check reports what would refuse a call before any of it runs.
        ModPackage mod = Mod("m", CallingHook, Hooks(Doubles),
                             lua: "api = 1\ncapabilities = [\"vm\"]\ncalls = [\"CALLEE\", \"safe.bin\", \"MISSING\"]");
        ModLinkResult result = ModLinker.Link(CallBase(), [mod], []);

        Assert.False(result.Succeeded);
        Assert.Equal(
        [
            "lua.calls: CALLEE calls SUB2: SUB2.BIN at 0x0 (op 0x19e save-numbered-slot) saves, and a called script cannot save",
            "lua.calls: neither the catalog nor the mod's [scripts] has MISSING",
        ], Stopping(result).Select(line => line.Message).Order(StringComparer.Ordinal));
        string[] notes = result.Report.Lines.Where(line => line.Class == ModReportClass.Note && line.Message.StartsWith("lua.calls", StringComparison.Ordinal))
            .Select(line => line.Message).ToArray();
        Assert.Equal(
        [
            "lua.calls: CALLEE: CALLEE.BIN at 0x8 (op 0x3 call-script) calls a script whose id comes from a cell, which the check "
            + "cannot follow",
            "lua.calls: CALLEE: CALLEE.BIN at 0xb (op 0xc8 sleep) may wait, and the call fails if it does",
            "lua.calls: CALLEE: CALLEE.BIN at 0xe (op 0xae continue-save-load-stack-restore) continues a load's restore, so the "
            + "call fails while a restore is pending",
        ], notes.Order(StringComparer.Ordinal));
        // SUB2 calls CALLEE back: each script is walked once.
        Assert.Single(result.Report.Lines, line => line.Message.Contains("op 0xc8", StringComparison.Ordinal));

        // SAFE alone links; the runtime enforces the list by catalog name, the report shows it, and the cache keeps it.
        ModPackage safe = Mod("m", CallingHook, Hooks(Doubles), lua: "api = 1\ncapabilities = [\"vm\"]\ncalls = [\"safe.bin\"]");
        ModLinkResult linked = ModLinker.Link(CallBase(), [safe], []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        Assert.Equal(["SAFE"], Assert.Single(linked.Lua.Stack.Mods).CallList);
        Assert.Contains("mod m 1.0.0: capabilities vm (opaque); writes no name, and any cell by address; calls SAFE",
                        linked.Report.Lua);
        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", CallBase(), linked);
        Assert.Equal(["SAFE"], Assert.Single(ModLinkCache.TryOpen(cache, "key")!.LuaStack.Mods).CallList);
    }

    private static string Oms(string name, string body)
        => $"oms 1\nscript \"{name}\"\n\n{string.Join("\n", body.Split('\n').Select(line => "    " + line))}\n";

    [Fact]
    public void AModsOwnScriptOnItsCallsIsItsCatalogRecordAndIsWalkedLikeAGameScript()
    {
        // The mod's WAITER shadows the game's: the list names the mod's record, by its catalog name.
        ModLinkBase linkBase = CallBase() with
        {
            // A compiled script's header carries the table's ABI, so the mod's WAITER needs Himegari's table.
            Table = HimegariTable,
            Intrinsics = new IntrinsicCatalog(HimegariTable, Contracts),
            InstallSelectors = new HashSet<int> { 1 },
            ReadScript = name => name.Equals("WAITER.BIN", StringComparison.OrdinalIgnoreCase) ? CallScripts["SAFE.BIN"]
                                 : CallScripts.GetValueOrDefault(name),
        };
        var files = Hooks(Doubles);
        files["scripts/WAITER.oms"] = Oms("WAITER", "G[0x400] = 1\nsleep(0xa)\nexit");
        ModPackage mod = Mod("m", CallingHook + "\n[scripts]\nnames = [\"WAITER\"]\n", files,
                             lua: "api = 1\ncapabilities = [\"vm\"]\ncalls = [\"waiter\", \"SAFE\"]");
        ModLinkResult result = ModLinker.Link(linkBase, [mod], []);

        Assert.True(result.Succeeded, result.Report.Render());
        ModRuntimeMod linked = Assert.Single(result.Lua.Stack.Mods);
        Assert.Equal(["$2$WAITER", "SAFE"], linked.CallList);
        Assert.Equal([("WAITER", "$2$WAITER")], linked.OwnScripts!.Select(pair => (pair.Key, pair.Value)));
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.Note
            && line.Message.StartsWith("lua.calls: WAITER: $2$WAITER.BIN at 0x", StringComparison.Ordinal)
            && line.Message.EndsWith("(op 0xc8 sleep) may wait, and the call fails if it does", StringComparison.Ordinal));
        Assert.Contains("calls WAITER ($2$WAITER), SAFE", Assert.Single(result.Report.Lua, line => line.StartsWith("mod m ", StringComparison.Ordinal)));

        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", linkBase, result);
        ModRuntimeMod cached = Assert.Single(ModLinkCache.TryOpen(cache, "key")!.LuaStack.Mods);
        Assert.Equal(linked.CallList, cached.CallList);
        Assert.Equal([("WAITER", "$2$WAITER")], cached.OwnScripts!.Select(pair => (pair.Key, pair.Value)));
    }

    [Fact]
    public void AModThatListsCallsNeedsVm()
        => Assert.Equal(["m"], Stop(ModLinker.Link(CallBase(), [Mod("m", CallingHook, Hooks("function on_award(e) end"),
                                                                    lua: "api = 1\ncalls = [\"SAFE\"]")], []),
                                    "lua.calls: maid.vm.call is raw access, so a mod that lists calls needs the vm capability").Mods);

    private static string ScriptHook(string when, string extra = "", string script = AddExp)
        => $"[[hooks]]\nscript = \"{script}\"\nwhen = \"{when}\"\nhandler = \"hooks.lua:on_award\"\n{extra}";

    [Fact]
    public void AScriptHookBindsItsScriptWithEnterAndExitListsAndTheCacheKeepsThem()
    {
        ModPackage first = Mod("first", ScriptHook("enter") + "\n" + ScriptHook("exit"), Hooks(Doubles));
        ModPackage second = Mod("second", ScriptHook("enter", "priority = -1"), Hooks(Doubles));
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [first, second], []);

        Assert.True(result.Succeeded, result.Report.Render());
        PlannedScript planned = result.Lua.Plan.Scripts["ADDEXP"];
        Assert.Equal([(1, -1), (0, 0)], planned.Enter.Select(h => (h.LoadOrder, h.Priority)));
        Assert.Equal([0], planned.Exit.Select(h => h.LoadOrder));
        Assert.Empty(result.Lua.Plan.Sites);
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.OrderedInteraction
            && line.Message == "enter: handlers run in this order: second hooks.lua:on_award (priority -1), first hooks.lua:on_award (priority 0)");
        Assert.Contains("hook ADDEXP enter: second hooks.lua:on_award (priority -1), first hooks.lua:on_award (priority 0)",
                        result.Report.Lua);
        Assert.Contains("hook ADDEXP exit: first hooks.lua:on_award (priority 0)", result.Report.Lua);

        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", SyntheticBase(), result);
        PlannedScript cached = ModLinkCache.TryOpen(cache, "key")!.HookPlan.Scripts["ADDEXP"];
        Assert.Equal(planned.Enter, cached.Enter);
        Assert.Equal(planned.Exit, cached.Exit);
    }

    [Fact]
    public void AScriptHookNeedsVmAScriptOfTheCatalogAndOneNoModReplaces()
    {
        Stop(ModLinker.Link(SyntheticBase(), [Mod("m", ScriptHook("enter"), Hooks(Doubles), lua: "api = 1")], []),
             "a script hook hands its handler the frame, which needs the vm capability");
        Stop(ModLinker.Link(SyntheticBase(), [Mod("m", ScriptHook("exit", script: "NONE.BIN"), Hooks(Doubles))], []),
             "the base catalog has no NONE.BIN");
        ModPackage replacer = Mod("replacer", files: new() { ["replace/ADDEXP.BIN"] = "replacement" }, lua: "api = 1");
        ModReportLine line = Stop(ModLinker.Link(SyntheticBase(), [replacer, Mod("hooker", ScriptHook("enter"), Hooks(Doubles))], []),
                                  "replacer replaces ADDEXP.BIN, and the hook names the original's entry and exit");
        Assert.Equal(ModReportClass.HardConflict, line.Class);
    }

    [Theory]
    [InlineData("site", 0x0, null)]
    [InlineData("point", 0xb, null)]
    [InlineData("site", 0x5, "(op 0x3 call-script), which calls a script and ends its frame, the scene or the run; only")]
    [InlineData("site", 0x8, "(op 0x72 wait-for-input), which may wait, calls a script and ends its frame, the scene or the run")]
    [InlineData("site", 0x10, "(op 0x2 exit), which ends its frame, the scene or the run")]
    [InlineData("site", 0x11, "(op 0x8c jmp), which jumps; only")]
    [InlineData("site", 0x14, "(op 0x19e save-numbered-slot), which saves; only")]
    public void SkipIsRefusedOnAnInstructionThatDoesNotGoOnToTheNextOne(string form, int site, string? refusal)
    {
        // Spec §7.1, from the instruction's facts in the base script.
        string hook = form == "point" ? PointHook(extra: "skip = true") : SiteHook(site, "before", extra: "skip = true");
        ModLinkResult result = ModLinker.Link(SiteBase(), [Mod("m", hook, Hooks(Doubles))], []);
        if (refusal == null)
        {
            Assert.True(result.Succeeded, result.Report.Render());
            Assert.True(Assert.Single(Assert.Single(result.Lua.Plan.Sites["ADDEXP"]).Handlers).Skip);
            Assert.Contains(result.Report.Lua, line => line.EndsWith("m hooks.lua:on_award (priority 0, may skip)", StringComparison.Ordinal));
        }
        else
            Assert.Contains($"skip = true on the instruction at 0x{site:x} {refusal}", Stop(result, "skip = true").Message);
    }

    [Fact]
    public void AnAfterHookAcrossACallOrAWaitAndOneOnAnExitGetNotes()
    {
        // Spec §7.1: across a call or a wait a save can come between, and an exit never finishes. A mov, and a before
        // hook on the wait, get none.
        ModPackage mod = Mod("m", SiteHook(0x5, "after") + "\n" + SiteHook(0x8, "after") + "\n" + SiteHook(0x10, "after")
                                  + "\n" + SiteHook(0x0, "after") + "\n" + SiteHook(0x8, "before"), Hooks(Doubles));
        ModLinkResult result = ModLinker.Link(SiteBase(), [mod], []);

        Assert.True(result.Succeeded, result.Report.Render());
        string[] notes = result.Report.Lines.Where(line => line.Class == ModReportClass.Note && line.Script == AddExp)
            .Select(line => line.Message).ToArray();
        Assert.Equal(3, notes.Length);
        Assert.StartsWith("hooks[0] site ADDEXP.BIN: the instruction at 0x5 (op 0x3 call-script) calls a script, so the after "
                          + "hook fires only once it has finished; a save made before then", notes[0]);
        Assert.StartsWith("hooks[1] site ADDEXP.BIN: the instruction at 0x8 (op 0x72 wait-for-input) may wait and calls a script",
                          notes[1]);
        Assert.Equal("hooks[2] site ADDEXP.BIN: the instruction at 0x10 (op 0x2 exit) ends its frame, the scene or the run, so the "
                     + "after hook never fires; a script exit hook sees a frame's return", notes[2]);
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
        ModPackage siteHooker = Mod("site-hooker", SiteHook(0x9a, "after", fingerprint: AddExpFingerprint), Hooks(Doubles));
        ModLinkResult bySite = ModLinker.Link(linkBase, [inserting, siteHooker], []);
        Assert.True(bySite.Succeeded, bySite.Report.Render());
        Assert.Equal((site.Offset, 0x9a, 1), (bySite.Lua.Plan.Sites["ADDEXP"][0].Offset, bySite.Lua.Plan.Sites["ADDEXP"][0].Site,
                                              bySite.Lua.Plan.Sites["ADDEXP"][0].AfterList.Count));

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
        // The base mod requires ome-records, which the engine ships and mounts first.
        Assert.Equal(["ome-records", "himegari-base", "user"], result.Lua.Stack.Mods.Select(mod => mod.Id));
        Assert.True(result.Lua.Stack.Mods[0].Library);
        Assert.True(result.Lua.Stack.Mods[1].Layout!.TryGet("unit_current_levels", out _));
    }

    // ------------------------------------------------------------------------------------------- watches (§7.3)

    private const string WatchLayout = """
        {"layout": 1, "profile": "himegari", "globals": [
        {"name": "speed", "bank": "GF", "address": "0x10", "dims": []},
        {"name": "stage_turn_limit", "bank": "G", "address": "0x11", "dims": [], "aliases": ["turn_limit"]},
        {"name": "hero_name", "bank": "GS", "address": "0x20", "dims": []},
        {"name": "unit_current_levels", "bank": "G", "address": "0x100", "dims": [4]},
        {"name": "unit_grid", "bank": "G", "address": "0x200", "dims": [2, 3]},
        {"name": "open_table", "bank": "G", "address": "0x300", "dims": [null]}
        ]}
        """;

    private ModPackage WatchBase() => Mod("test-base", files: new() { ["layout/globals.json"] = WatchLayout });

    private static string Watch(string target, string extra = "")
        => $"[[hooks]]\nwatch = \"{target}\"\nhandler = \"hooks.lua:on_write\"\n{extra}\n";

    private const string Requires = "[[requires]]\nid = \"test-base\"\nversion = \">=1, <2\"\n\n";
    private const string OnWrite = "function on_write(ctx) end";

    /// <summary>A mod without vm that requires the base mod and has <paramref name="hooks"/>.</summary>
    private ModPackage Watcher(string hooks, string id = "watcher")
        => Mod(id, Requires + hooks, Hooks(OnWrite), lua: "api = 1");

    [Fact]
    public void AWatchOnANameBindsThroughTheBaseModWithoutVmAndOneCellIsOneWatchInTheMergedOrder()
    {
        ModPackage watcher = Watcher(Watch("turn_limit") + Watch("unit_grid[*]") + Watch("hero_name"));
        ModPackage raw = Mod("raw", Watch("G[0x11]", "priority = -1"), Hooks(OnWrite));
        ModLinkResult result = ModLinker.Link(SyntheticBase(), [WatchBase(), watcher, raw], []);

        Assert.True(result.Succeeded, result.Report.Render());
        IReadOnlyList<PlannedWatch> watches = result.Lua.Plan.Watches;
        Assert.Equal([("stage_turn_limit", CellKind.Integer, 0x11, 1), ("unit_grid[*]", CellKind.Integer, 0x200, 6),
                      ("hero_name", CellKind.String, 0x20, 1)],
                     watches.Select(watch => (watch.Watch, watch.Kind, watch.Address, watch.Count)));
        Assert.Equal([("raw", -1), ("watcher", 0)],
                     watches[0].Handlers.Select(handler => (result.Lua.Stack[handler.Handler].ModId, handler.Priority)));
        Assert.Equal(("unit_grid", 2, 3), (watches[1].Global, watches[1].Dimensions, watches[1].RowWidth));
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.OrderedInteraction
            && line.Message == "watch stage_turn_limit: handlers run in this order: raw hooks.lua:on_write (priority -1), "
                               + "watcher hooks.lua:on_write (priority 0)");
        Assert.Contains("watch unit_grid[*] (G[0x200..0x205]): watcher hooks.lua:on_write (priority 0)", result.Report.Lua);
        Assert.Contains("watch hero_name (GS[0x20]): watcher hooks.lua:on_write (priority 0)", result.Report.Lua);

        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", SyntheticBase(), result);
        IReadOnlyList<PlannedWatch> cached = ModLinkCache.TryOpen(cache, "key")!.HookPlan.Watches;
        static object Cells(PlannedWatch watch)
            => (watch.Watch, watch.Kind, watch.Address, watch.Count, watch.Global, watch.Dimensions, watch.RowWidth);
        Assert.Equal(watches.Select(Cells), cached.Select(Cells));
        Assert.Equal(watches.SelectMany(watch => watch.Handlers), cached.SelectMany(watch => watch.Handlers));
    }

    [Fact]
    public void TwoWatchesThatShareCellsAreAnOrderedInteraction()
    {
        ModLinkResult result = ModLinker.Link(SyntheticBase(),
            [WatchBase(), Watcher(Watch("unit_current_levels[*]")), Mod("raw", Watch("G[0x102]", "priority = -1"), Hooks(OnWrite))], []);

        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal(2, result.Lua.Plan.Watches.Count);
        Assert.Contains(result.Report.Lines, line => line.Class == ModReportClass.OrderedInteraction
            && line.Message == "watches unit_current_levels[*] and G[0x102] share cells; on one of them the handlers run in "
                               + "this order: raw hooks.lua:on_write (priority -1), watcher hooks.lua:on_write (priority 0)");
    }

    [Theory]
    [InlineData("G[0x11]", "a watch on an address names a cell by number, which needs the vm capability")]
    [InlineData("no_such_name", "'no_such_name' is not a name test-base exports")]
    [InlineData("speed", "speed is a float global; version 1 watches integer and string globals")]
    [InlineData("turn_limit[*]", "stage_turn_limit is no table; watch \"stage_turn_limit\"")]
    [InlineData("unit_grid", "unit_grid is a table; watch \"unit_grid[*]\" for its cells")]
    [InlineData("open_table[*]", "open_table's row count is not known, so a watch on the table has no end")]
    public void AWatchTheBaseModCannotGiveIsALinkError(string target, string message)
        => Assert.Equal(["watcher"], Stop(ModLinker.Link(SyntheticBase(), [WatchBase(), Watcher(Watch(target))], []),
                                         $"hooks[0] watch {target}: {message}").Mods);

    [Fact]
    public void AWatchOnANameNeedsABaseMod()
        => Stop(ModLinker.Link(SyntheticBase(), [Mod("m", Watch("stage_turn_limit"), Hooks(OnWrite))], []),
                "a watch on a name resolves through a base mod, and the mod requires none");

    [Theory]
    [InlineData("GF[0x10]", "version 1 watches integer and string cells (G[0x…], GS[0x…]); 'GF[0x10]' is a float cell")]
    [InlineData("G[16]", "'G[16]' is not a global's name, name[*] for a whole table, or G[0x…] or GS[0x…] for a cell")]
    [InlineData("G[0x100000000]", "is not a global's name")]
    [InlineData("levels[0]", "is not a global's name")]
    public void TheReaderRefusesAWatchThatNamesNoCell(string target, string message)
    {
        string text = "omp = 3\nid = \"m\"\nname = \"m\"\nversion = \"1.0.0\"\n\n[game]\nprofile = \"himegari\"\n"
                      + "target = \"extended\"\n\n[lua]\napi = 1\n\n" + Watch(target);
        var error = Assert.Throws<ModManifestException>(() => ManifestReader.Parse(text));
        Assert.Contains(error.Problems, problem => problem.Contains(message, StringComparison.Ordinal));
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
        Assert.Equal(("test-base", ExperiencePoint), (award.Owner, string.Join(",", award.Points)));
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
        Assert.Equal(result.Lua.Stack.EventList.Select(gameEvent => (gameEvent.Name, gameEvent.Owner, string.Join(",", gameEvent.Points))),
                     reopened.LuaStack.EventList.Select(gameEvent => (gameEvent.Name, gameEvent.Owner, string.Join(",", gameEvent.Points))));
        Assert.Equal(result.Lua.Stack.EventList.SelectMany(gameEvent => gameEvent.Fields),
                     reopened.LuaStack.EventList.SelectMany(gameEvent => gameEvent.Fields));
        Assert.Equal(site.Handlers, Assert.Single(reopened.HookPlan.Sites["ADDEXP"]).Handlers);
    }

    /// <summary>A base whose ADDEXP.BIN is <see cref="RunAddExp"/>, with <c>test.first</c> at its 0x0 and
    /// <c>test.second</c> at its 0x5 (and <paramref name="second"/> moves the second point).</summary>
    private ModLinkBase TwoPointBase(int second = 0x5)
    {
        string fingerprint = OmsPatchPlan.FingerprintOf(RunAddExp);
        return SyntheticBase() with
        {
            ReadScript = name => name.Equals(AddExp, StringComparison.OrdinalIgnoreCase) ? RunAddExp : null,
            PatchPoints = new OmsPatchPoints([
                new OmsPatchPoint("test.first", AddExp, null, [new OmsPatchPointSite(fingerprint, 0x0)]),
                new OmsPatchPoint("test.second", AddExp, null, [new OmsPatchPointSite(fingerprint, second)])]),
        };
    }

    private const string TwoPointEvent = """
        [[event]]
        name = "test.award"
        point = ["test.first", "test.second"]

        [[event.field]]
        name = "amount"
        at = "L[0x1]"
        type = "int"
        """;

    [Fact]
    public void AnEventAtTwoPointsIsRaisedAtEachAndTheCacheKeepsBoth()
    {
        ModPackage subscriber = Mod("subscriber", RequiresBase + EventHook("test.award"),
            Hooks("function on_award(ctx) maid.log.info(\"amount \" .. ctx.amount) end"), lua: "api = 1");
        ModLinkResult result = ModLinker.Link(TwoPointBase(), [EventBase(TwoPointEvent), subscriber], []);
        Assert.True(result.Succeeded, result.Report.Render());

        Assert.Equal(["test.first", "test.second"], result.Lua.Stack.Event("test.award")!.Points);
        Assert.Equal([(0x0, "test.first", "test.award"), (0x5, "test.second", "test.award")],
                     result.Lua.Plan.Sites["ADDEXP"].Select(site => (site.Offset, site.Point,
                                                                     Assert.Single(site.Handlers).GameEvent)));
        Assert.Contains("event test.award (test-base, at test.first, ADDEXP 0x0 and test.second, ADDEXP 0x5): fields amount "
                        + "int r; subscribers subscriber hooks.lua:on_award (priority 0)", result.Report.Lua);

        // The handler runs before each instruction: L[1] is 0 before 0x0 sets it, and 3 at 0x5.
        using (var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
               {
                   Runtime = _runtime, Stack = result.Lua.Stack, Plan = result.Lua.Plan, Errors = LuaErrorPolicy.Halt,
               }))
        {
            var vm = new VirtualMachine(Sys4Loader.Parse(RunAddExp, HimegariTable, AddExp), HimegariTable, new HeadlessWaits(),
                                        scripting: scripting);
            vm.Run();
            Assert.Equal("exit", vm.HaltReason);
            Assert.Equal(["amount 0", "amount 3"], scripting.RecentLog
                .Select(line => line[(line.LastIndexOf(": ", StringComparison.Ordinal) + 2)..])
                .Where(text => text.StartsWith("amount ", StringComparison.Ordinal)));
        }

        string cache = Path.Combine(_root, "cache");
        ModLinkCache.Write(cache, "key", TwoPointBase(), result);
        MountedModLayer reopened = ModLinkCache.TryOpen(cache, "key")!;
        Assert.Equal(["test.first", "test.second"], reopened.LuaStack.Event("test.award")!.Points);
        Assert.Equal(result.Lua.Plan.Sites["ADDEXP"].Select(site => site.Handlers),
                     reopened.HookPlan.Sites["ADDEXP"].Select(site => site.Handlers));
    }

    [Fact]
    public void TwoPointsOfOneEventOnOneInstructionAreALinkError()
        => Assert.Equal(["test-base"], Stop(ModLinker.Link(TwoPointBase(second: 0x0),
                [EventBase(TwoPointEvent), Subscriber("m", EventHook("test.award"))], []),
            "test.award: test.first and test.second name one instruction (ADDEXP 0x0), so the event would be raised twice there").Mods);

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
        // Its helpers, from its lua/init.lua, with their annotations.
        Assert.Contains("-- The helpers himegari-base's lua/init.lua returns.\n", names);
        Assert.Contains("---@param item integer an item id, 1 to 999\n", names);
        Assert.EndsWith("function M.give_item(item, count) end\n\nreturn M\n", names);
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
    [InlineData("[[event]]\nname = \"test.award\"\nraised_by = \"lua\"\n[[event.field]]\nname = \"x\"\nat = \"L[1]\"\ntype = \"int\"",
                "events/award.toml: test.award: field x: an event raised from Lua has no locations")]
    [InlineData("[[event]\n", "events/award.toml: not valid TOML")]
    public void ADeclarationThatDoesNotLinkStopsTheLinkNamingItsFile(string events, string message)
        => Assert.Equal(["test-base"], Stop(ModLinker.Link(SyntheticBase(), [EventBase(events), Subscriber("m", EventHook("test.award"))], []),
                                            message).Mods);

    [Fact]
    public void AnEventWhosePointDoesNotBindIsANoteAndOnlyItsSubscribersDoNotLink()
    {
        const string nowhere = "[[event]]\nname = \"test.award\"\npoint = \"himegari.nowhere\"";
        // Declared and not subscribed: the stack links, and the report says why the event is not raised.
        ModLinkResult quiet = ModLinker.Link(SyntheticBase(), [EventBase(nowhere)], []);
        Assert.True(quiet.Succeeded, quiet.Report.Render());
        Assert.Contains(quiet.Report.Lines, line => line.Class == ModReportClass.Note && line.Message
            == "events/award.toml: test.award: not raised in this install, so a mod that subscribes to it does not link: "
               + "'himegari.nowhere' is not a named patch point of himegari");
        Assert.Empty(quiet.Lua.Stack.EventList);

        // A subscriber does not link, naming the declaring mod and the reason.
        ModLinkResult subscribed = ModLinker.Link(SyntheticBase(), [EventBase(nowhere), Subscriber("m", EventHook("test.award"))], []);
        Assert.Equal(["m"], Stop(subscribed, "test-base declares test.award, which is not raised in this install: "
                                             + "'himegari.nowhere' is not a named patch point of himegari").Mods);

        // Two mods that declare it are still a conflict.
        ModPackage again = Mod("again", RequiresBase, new() { ["events/again.toml"] = nowhere, ["lua/init.lua"] = "return {}" });
        Assert.Equal(ModReportClass.HardConflict,
                     Stop(ModLinker.Link(SyntheticBase(), [EventBase(nowhere), again], []), "both declare the event test.award").Class);
    }

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
        Assert.Equal(("himegari.experience.award", "himegari-base", ExperiencePoint), (award.Name, award.Owner, string.Join(",", award.Points)));
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
    private static GameSession Award(Script addExp, long amount, Scripting? scripting = null, IScriptProvider? provider = null,
                                     Action<GameSession>? seed = null)
    {
        var session = new GameSession();
        session.Seed(0x62ccb, amount);      // the award
        session.Seed(0x3302, 0);            // the multiplier bits: none
        session.Seed(0x152616, 0);          // current_entity_index 0: ADDEXP takes current_unit_id as it is
        session.Seed(0x66715, 1);           // current_unit_id
        session.Seed(0x6930 + 1, 1);        // unit_current_levels[1]: level 1
        session.Seed(0x7a766 + 1, 99);      // unit_level_cap[1]: below the cap, so the award applies
        session.Seed(0x6994 + 1, 0);        // unit_experience_progress[1]: 100 is a level, which the award stays under
        seed?.Invoke(session);
        SceneResult result = session.RunScene(addExp, HimegariTable, new HeadlessWaits(), provider: provider, scripting: scripting);
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

    // ------------------------------------------------------------------------------- the base mods' helpers (§9.3, §10)

    /// <summary>A mod without vm that requires <paramref name="profile"/>'s shipped base mod, subscribes to its award
    /// and runs <paramref name="body"/> there with <c>game</c> the base mod; and the base mod.</summary>
    private IReadOnlyList<ModPackage> Giver(string profile, string body)
    {
        ModPackage user = Mod("giver", $"[[requires]]\nid = \"{profile}-base\"\nversion = \">=1.0, <2\"\n\n"
                                       + $"[[hooks]]\nevent = \"{profile}.experience.award\"\nhandler = \"hooks.lua:on_award\"\n",
                              Hooks($"local game = require(\"{profile}-base\")\nfunction on_award(e)\n{body}\nend\n"),
                              lua: "api = 1", profile: profile);
        IReadOnlyList<ModPackage> shipped = ShippedPackages.Required([user], new ModLinkReport());
        _opened.AddRange(shipped);
        return [user, .. shipped];
    }

    /// <summary>A stand-in for Himegari's ADDITEM at its cells: it notes current_item_id in G[0x400], adds
    /// current_item_count_change to item_inventory_counts[5], and clears both arguments, as a script may.</summary>
    private static readonly byte[] HimegariAddItem = ScriptAssembler.AssembleBytes(new List<(int, Operand[])>
    {
        (0x55, [new Operand(OperandTypes.GlobalInteger, 0x400), new Operand(OperandTypes.GlobalInteger, 0x8c877)]),
        (0x50, [new Operand(OperandTypes.GlobalInteger, 0x3306 + 5), new Operand(OperandTypes.GlobalInteger, 0x3306 + 5),
                new Operand(OperandTypes.GlobalInteger, 0x8c878)]),
        (0x55, [new Operand(OperandTypes.GlobalInteger, 0x8c877), new Operand(OperandTypes.Immediate, 0)]),
        (0x55, [new Operand(OperandTypes.GlobalInteger, 0x8c878), new Operand(OperandTypes.Immediate, 0)]),
        (0x5, []),
    }, [], "SYS4422");

    [Fact]
    public void TheBaseModsGiveItemCallsAddItemForTheModThatCalledItAndPutsItsArgumentsBack()
    {
        // Spec §9.3: where the game has a script for a change, the helper calls it. A mod without vm and without a
        // write list changes an item's count through it; the trace names the mod and the base mod that called.
        string fingerprint = OmsPatchPlan.FingerprintOf(RunAddExp);
        ModLinkBase linkBase = SyntheticBase() with
        {
            ReadScript = name => name.Equals(AddExp, StringComparison.OrdinalIgnoreCase) ? RunAddExp
                                 : name.Equals("ADDITEM.BIN", StringComparison.OrdinalIgnoreCase) ? HimegariAddItem : null,
            PatchPoints = new OmsPatchPoints([new OmsPatchPoint(ExperiencePoint, AddExp, null,
                [new OmsPatchPointSite(fingerprint, 0x5)])]),
        };
        ModLinkResult result = ModLinker.Link(linkBase, Giver("himegari", """
            maid.log.info("count " .. game.give_item(5, 3))
            for _, args in ipairs({ { 1000 }, { 5, 1.5 }, { 5, -100 }, { "5" } }) do
                local ok, err = pcall(game.give_item, args[1], args[2])
                maid.log.info(tostring(err))
            end
            """), []);
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Contains(result.Report.Lua, line => line.StartsWith("mod himegari-base 1.1.0: ", StringComparison.Ordinal)
                                                   && line.EndsWith("; calls ADDITEM", StringComparison.Ordinal));

        using var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = result.Lua.Stack, Plan = result.Lua.Plan, Errors = LuaErrorPolicy.Halt, Trace = true,
        });
        var provider = new MapProvider(new(), new(StringComparer.OrdinalIgnoreCase)
        {
            ["ADDITEM.BIN"] = Sys4Loader.Parse(HimegariAddItem, HimegariTable, "ADDITEM.BIN"),
        });
        var vm = new VirtualMachine(Sys4Loader.Parse(RunAddExp, HimegariTable, AddExp), HimegariTable, new HeadlessWaits(),
                                    null, provider, scripting: scripting);
        vm.SeedGlobal(0x8c877, 7);          // current_item_id as the hooked script left it
        vm.SeedGlobal(0x8c878, -2);         // current_item_count_change
        vm.SeedGlobal(0x3306 + 5, 10);      // item_inventory_counts[5]
        vm.Run();

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((5, 13), (vm.Globals[0x400], vm.Globals[0x3306 + 5]));
        Assert.Equal((7, -2), (vm.Globals[0x8c877], vm.Globals[0x8c878]));
        string[] lines = scripting.RecentLog.Where(line => line.Contains(" giver: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(" giver: ", StringComparison.Ordinal) + 8)..]).ToArray();
        Assert.Equal(
        [
            "count 13",
            "bad argument #1 to 'give_item' (an integer from 1 to 999 expected, got 1000)",
            "bad argument #2 to 'give_item' (an integer from -99 to 99 expected, got 1.5)",
            "bad argument #2 to 'give_item' (an integer from -99 to 99 expected, got -100)",
            "bad argument #1 to 'give_item' (an integer from 1 to 999 expected, got 5)",
        ], lines);
        string traced = Assert.Single(scripting.RecentTrace, line => line.Contains("\"mod\":\"giver\"", StringComparison.Ordinal));
        Assert.Contains("\"calls\":[{\"script\":\"ADDITEM.BIN\",\"through\":\"himegari-base\",\"result\":\"ok\"}]", traced);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void HimegarisGiveItemRunsTheGamesAddItemWhichKeepsTheCountUnder100()
    {
        ModLinkBase linkBase = InstalledBase();
        ModLinkResult linked = ModLinker.Link(linkBase, Giver("himegari", """
            maid.log.info("count " .. game.give_item(5, 60) .. " " .. game.give_item(5, 60) .. " " .. game.give_item(5, -99))
            """), []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        using var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt,
        });
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var scripts = new Sys4ScriptProvider(HimegariTable, catalog, new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir));
        GameSession session = Award(Sys4Loader.Parse(linkBase.ReadScript(AddExp)!, HimegariTable, AddExp), 7, scripting, scripts,
                                    seed: s => s.Seed(0x8c877, 4));
        Assert.DoesNotContain(scripting.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));
        Assert.Contains(scripting.RecentLog, line => line.EndsWith(": count 60 99 0", StringComparison.Ordinal));
        Assert.Equal((0, 4), (session.Globals[0x3306 + 5], session.Globals[0x8c877]));
        Assert.Equal(1, session.Globals[0x36ee + 5]);   // a gained item is marked
        Assert.Equal(7, session.Globals[0x6994 + 1]);   // the award goes on as before
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void KamidorisGiveItemPutsBackTheLoopIndexAndScratchValueTheGamesAddItemUses()
    {
        // Kamidori's ADDITEM loops over an item's granted skills with the loop index 365 scripts share, inside the
        // award, whose ADDEXP has the index and the scratch value in use.
        var (linkBase, scripts) = KamidoriBase(Path.Combine(Paths.Workspace, "Kamidori"));
        ModLinkResult linked = ModLinker.Link(linkBase, Giver("kamidori", """
            local names = { "current_item_id", "current_item_count_change", "shared_loop_index", "shared_scratch_value" }
            local before = {}
            for i, name in ipairs(names) do before[i] = game.g[name] end
            maid.log.info("count " .. game.give_item(3, 2))
            for i, name in ipairs(names) do
                if game.g[name] ~= before[i] then error(name .. " changed") end
            end
            """), []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        using var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt,
        });
        GameSession session = KamidoriAward(Sys4Loader.Parse(linkBase.ReadScript(AddExp)!, KamidoriTable, AddExp), scripts, 7,
                                            scripting, seed: s => s.Seed(0x304cf8 + 3, 9));   // item 3 grants skill 9
        Assert.DoesNotContain(scripting.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));
        Assert.Contains(scripting.RecentLog, line => line.EndsWith(": count 2", StringComparison.Ordinal));
        Assert.Equal(2, session.Globals[0x7ea4f + 3]);
        Assert.Equal(1, session.Globals[0xf6483 + 9]);  // ADDITEM learned the skill the item grants
        Assert.Equal(8, session.Globals[0x64391 + 1]);  // the award goes on as before: 7 with the gem's 25%
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
    private static GameSession KamidoriAward(Script addExp, IScriptProvider scripts, long amount, Scripting? scripting = null,
                                             Action<GameSession>? seed = null)
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
        seed?.Invoke(session);
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

        GameEvent award = linked.Lua.Stack.Event("kamidori.experience.award")!;
        Assert.Equal(("kamidori.experience.award", "kamidori-base", KamidoriPoint), (award.Name, award.Owner, string.Join(",", award.Points)));
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

    // ------------------------------------------------------------------------------- the boot and stage events

    private static readonly string[] StageEvents = ["stage.loaded", "stage.ready", "stage.round", "stage.close"];

    /// <summary>A mod of <paramref name="profile"/> that subscribes to its base mod's boot and stage events, with the
    /// base mods it requires: on_layered and on_ready log the class-change chain's second slot, which only the append
    /// writes.</summary>
    private IReadOnlyList<ModPackage> StageSubscriber(string profile)
    {
        string hooks = $"[[requires]]\nid = \"{profile}-base\"\nversion = \">=1.1, <2\"\n"
                       + $"[[hooks]]\nevent = \"{profile}.data.layered\"\nhandler = \"hooks.lua:on_layered\"\n"
                       + $"[[hooks]]\nevent = \"{profile}.data.ready\"\nhandler = \"hooks.lua:on_ready\"\n"
                       + string.Concat(StageEvents.Select(name
                           => $"[[hooks]]\nevent = \"{profile}.{name}\"\nhandler = \"hooks.lua:on_stage\"\n"));
        ModPackage user = Mod("stage-user", hooks, Hooks($$"""
            local function chain(e)
              local game = require("{{profile}}-base")
              return tostring(game.table("class_change_rule_script_ids")[1])
                  .. (maid.session.restoring and " restoring" or "")
            end
            function on_layered(e) maid.log.info("layered " .. chain(e)) end
            function on_ready(e) maid.log.info("ready " .. chain(e)) end
            function on_stage(e) maid.log.info(e.event .. " " .. e.stage) end
            """), lua: "api = 1", profile: profile);
        IReadOnlyList<ModPackage> shipped = ShippedPackages.Required([user], new ModLinkReport());
        _opened.AddRange(shipped);
        return [user, .. shipped];
    }

    private static IEnumerable<int> Offsets(ModLinkResult result, string script)
        => result.Lua.Plan.Sites[script].Select(site => site.Offset);

    [Fact]
    [Trait("Category", "Workspace")]
    public void HimegarisBaseModRaisesItsBootAndStageEventsAtTheInstalledScripts()
    {
        ModLinkResult linked = ModLinker.Link(InstalledBase(), StageSubscriber("himegari"), []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        Assert.DoesNotContain(linked.Report.Lines, line => line.Message.Contains("not raised in this install", StringComparison.Ordinal));
        Assert.Equal([0x180, 0x183], Offsets(linked, "INIT2"));
        Assert.Equal([0x20a, 0xa27, 0x764a, 0xbaa1, 0xbabd], Offsets(linked, "FIELD"));
        Assert.Equal(["himegari.stage.after_close_handlers_won", "himegari.stage.after_close_handlers_lost"],
                     linked.Lua.Stack.Event("himegari.stage.close")!.Points);
        Assert.Equal(["stage", "result", "turn"], linked.Lua.Stack.Event("himegari.stage.close")!.Fields.Select(field => field.Name));

        // At boot each event comes once, after the append's AUTORUN: the class-change chain's slot 1 is $1$CCINIT.
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var scripts = new Sys4ScriptProvider(HimegariTable, catalog, new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir));
        using var scripting = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt,
        });
        SceneResult result = new GameSession().RunScene(scripts.RequireByName("INIT2.BIN"), HimegariTable, new HeadlessWaits(),
                                                        provider: scripts, scripting: scripting);
        Assert.True(result.Halt is null or "exit" or "end", result.Halt);
        Assert.Equal([$"layered {0x1000017}", $"ready {0x1000017}"], scripting.RecentLog
            .Select(line => line[(line.LastIndexOf(": ", StringComparison.Ordinal) + 2)..])
            .Where(text => text.StartsWith("layered ", StringComparison.Ordinal) || text.StartsWith("ready ", StringComparison.Ordinal)));
    }

    [Theory]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [InlineData(false)]
    [InlineData(true)]
    public void KamidorisBaseModRaisesItsBootAndStageEventsOnBothInstalls(bool translated)
    {
        var (linkBase, _) = translated
            ? KamidoriBase(Paths.KamidoriTranslatedDir, Path.Combine(Paths.KamidoriTranslatedDir, "patch"), Paths.KamidoriTranslatedDir)
            : KamidoriBase(Path.Combine(Paths.Workspace, "Kamidori"));
        ModLinkResult linked = ModLinker.Link(linkBase, StageSubscriber("kamidori"), []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        Assert.DoesNotContain(linked.Report.Lines, line => line.Message.Contains("not raised in this install", StringComparison.Ordinal));
        Assert.Equal([0x20d, 0x2cf], Offsets(linked, "INIT2"));
        Assert.Equal([0x527, 0xd58, 0x933d, 0xc785], Offsets(linked, "FIELD"));
        // kamidori.stage.loaded is raised where INFO's third tab runs a floor's loaders again, too (data patches plan
        // §5.2): one revision of INFO3 serves both installs.
        Assert.Equal([0xd20], Offsets(linked, "INFO3"));
        // And where the stage list runs the loaders of the stage under its cursor for its preview (plan §11): the two
        // installs' SELSTAGE revisions have the loop at the same offsets.
        Assert.Equal([0x1408], Offsets(linked, "SELSTAGE"));
        Assert.Equal(["kamidori.stage.after_loader", "kamidori.stage.after_info_reload", "kamidori.stage.after_list_preview"],
                     linked.Lua.Stack.Event("kamidori.stage.loaded")!.Points);
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
