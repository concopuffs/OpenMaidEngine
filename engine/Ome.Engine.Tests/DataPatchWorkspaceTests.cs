using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Script.Abi;
using Ome.Script.Compile;
using Ome.Script.Ir;
using Ome.Script.Modules;
using Ome.Script.Syntax;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 0 of docs/superpowers/specs/2026-11-17-data-patches-design.md, on the installed games: the moment a data
/// file is applied, <c>&lt;game&gt;.data.layered</c> (spec §6.6; gate 6, its event half). A test mod's handler there
/// edits the boot's tables after every layer has written them and before the boot's own work uses them: in Himegari,
/// TUNE then lowers a saved tuning level by the edited curve, at boot and again at a load, where the same edit at
/// <c>data.ready</c> comes too late. In Kamidori, on both installs, the event comes before <c>data.ready</c> and an edit
/// made there is still in place at it. And gate 5's cells: the gate 6 example's <c>with</c> block compiles to the three
/// cells <see cref="DataPatchTests"/> has the applier write.
/// </summary>
public sealed class DataPatchWorkspaceTests : IDisposable
{
    private const int T_IMM = 0, T_GINT = 3, T_LINT = 9;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-data-patch-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = new();
    private readonly List<IDisposable> _owned = new();
    private readonly LazyModRuntime _runtime = LuaModRuntime.Lazy();

    private static readonly OpcodeTable HimegariTable = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4422");
    private static readonly OpcodeTable KamidoriTable = OpcodeTableJson.Load(Paths.OpcodesJson, "SYS4433");
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);

    public void Dispose()
    {
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        foreach (ModPackage package in _opened) package.Dispose();
        _runtime.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ------------------------------------------------------------------------------------------------- helpers

    /// <summary>A test mod of <paramref name="profile"/> with <paramref name="hooks"/> as its lua/hooks.lua, subscribed
    /// to both boot events, with the shipped packages it mounts.</summary>
    private IReadOnlyList<ModPackage> TestMod(string profile, string writes, string hooks)
    {
        string folder = Path.Combine(_root, "test.data-moment-" + profile);
        Directory.CreateDirectory(Path.Combine(folder, "lua"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), $"""
            omp = 3
            id = "test.data-moment"
            name = "test.data-moment"
            version = "1.0.0"

            [game]
            profile = "{profile}"
            target = "extended"

            [lua]
            api = 1
            capabilities = ["vm"]
            writes = [{writes}]

            [[requires]]
            id = "{profile}-base"
            version = ">=1.1, <2"

            [[hooks]]
            event = "{profile}.data.layered"
            handler = "hooks.lua:on_layered"

            [[hooks]]
            event = "{profile}.data.ready"
            handler = "hooks.lua:on_ready"
            """);
        File.WriteAllText(Path.Combine(folder, "lua", "hooks.lua"), hooks);
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        IReadOnlyList<ModPackage> shipped = ShippedPackages.Required([mod], new ModLinkReport());
        _opened.AddRange(shipped);
        return [mod, .. shipped];
    }

    private ModLinkResult Linked(ModLinkBase linkBase, IReadOnlyList<ModPackage> packages)
    {
        ModLinkResult linked = ModLinker.Link(linkBase with { Lua = _runtime }, packages, []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        return linked;
    }

    private Scripting Native(ModLinkResult linked)
    {
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt,
        });
        _owned.Add(native);
        return native;
    }

    /// <summary>The test mod's log lines, without the time, the level and the mod.</summary>
    private static string[] Lines(Scripting native)
        => native.RecentLog.Where(line => line.Contains(" test.data-moment: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(" test.data-moment: ", StringComparison.Ordinal) + 19)..]).ToArray();

    // ------------------------------------------------------------------------------------------------- Himegari

    private static (ModLinkBase Base, Sys4ScriptProvider Scripts) Himegari()
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        var store = new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir);
        ModLinkBase linkBase = ModLinkBase.ForInstall("himegari", Paths.Sys4Ini, catalog, store, HimegariTable, Contracts,
            Paths.GlobalsJson("himegari"), Paths.OmsModules("himegari"), Paths.PatchPointsJson("himegari"));
        return (linkBase, new Sys4ScriptProvider(HimegariTable, catalog, store));
    }

    /// <summary>Item 999's first saved tuning level, which TUNE reads as G[0x3ebe + item*10 + k] (TUNE@0x4f).</summary>
    private const int TuningLevel = 0x3ebe + 999 * 10;

    /// <summary>
    /// The TUNE mod: <c>edit</c> makes item 999 an item TUNE walks (its category 10, TUNE@0x24) whose first stat uses
    /// curve 19, and gives that curve a bonus at its first two levels only. TUNE then lowers a saved level of 5 to 2:
    /// from index 4 down to the last index whose bonus is not 0, plus one (TUNE@0x8a to 0xc6). Each handler logs the
    /// level, and <paramref name="editAt"/> names the one that edits first.
    /// </summary>
    private IReadOnlyList<ModPackage> TuneMod(string editAt) => TestMod("himegari",
        "\"item_category\", \"item_tuning_curve_ids\", \"item_tuning_stat_bonus_curves\"", $$"""
        local game = require("himegari-base")
        local function edit()
            game.table("item_category")[999] = 10
            game.table("item_tuning_curve_ids")[999][0] = 19
            local curve = game.table("item_tuning_stat_bonus_curves")[19]
            curve[0] = 3
            curve[1] = 6
            for k = 2, 10 do curve[k] = 0 end
        end
        local function level()
            return tostring(maid.vm.g[{{TuningLevel}}]) .. (maid.session.restoring and " restoring" or "")
        end
        function on_layered(e)
            maid.log.info("layered level=" .. level())
            if "{{editAt}}" == "layered" then edit() end
        end
        function on_ready(e)
            if "{{editAt}}" == "ready" then edit() end
            maid.log.info("ready level=" .. level())
        end
        """);

    [Theory]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [InlineData("layered", 2)]
    [InlineData("ready", 5)]
    public void AnEditAtDataLayeredReachesTuneAndTheSameEditAtDataReadyComesTooLate(string editAt, int level)
    {
        var (linkBase, scripts) = Himegari();
        Scripting native = Native(Linked(linkBase, TuneMod(editAt)));
        var session = new GameSession();
        session.Seed(TuningLevel, 5);   // a level the player reached, as a save brings it back
        SceneResult result = session.RunScene(scripts.RequireByName("INIT2.BIN"), HimegariTable, new HeadlessWaits(),
                                              provider: scripts, scripting: native);
        Assert.True(result.Halt is null or "exit" or "end", result.Halt);
        // The saved level is untouched by the boot until TUNE: INIT2's INIT scripts and the append leave it.
        Assert.Equal(["layered level=5", $"ready level={level}"], Lines(native));
        Assert.Equal(level, session.Globals.GetValueOrDefault(TuningLevel));
        Assert.Equal(10, session.Globals.GetValueOrDefault(0x8d049 + 999));   // item_category[999]
    }

    /// <summary>A save identity with Himegari's saved prefixes (the profile's bank dimensions).</summary>
    private static readonly NativeSaveIdentity Identity =
        new(NativeSaveMagic.S4SD, 0x4a343234, "data-patch-test", 3, 10, new NativeSaveBankDimensions(0x6241b, 1, 0x315, 1, 1, 1));

    /// <summary>An id outside the install's catalog, for the synthetic root a load re-enters.</summary>
    private const uint RootId = 0x7e000000;

    /// <summary>The install's scripts, with the synthetic root by its id and a wait-free CALLBACK_LOAD (the
    /// install's draws and plays sound, which this test does not need).</summary>
    private sealed class WithRoot(Sys4ScriptProvider installed, Script root, Script callback) : IScriptProvider
    {
        public Script? GetById(long id) => id == RootId ? root : installed.GetById(id);
        public AgeBoxMessage? LoadFailure(long id) => ((IScriptProvider)installed).LoadFailure(id);
        public Script? GetByName(string name)
            => name.Equals("CALLBACK_LOAD.BIN", StringComparison.OrdinalIgnoreCase) ? callback : installed.GetByName(name);
        public Script? GetByMountedName(string name) => installed.GetByMountedName(name);
        public IReadOnlyList<int> MountedAppendSelectors => installed.MountedAppendSelectors;
    }

    private static Script WithId(Script source, uint packedId) => new()
    {
        Name = source.Name,
        PackedId = packedId,
        Header = source.Header,
        Instructions = source.Instructions,
        IndexByOffset = source.IndexByOffset,
        Strings = source.Strings,
        BodyDwords = source.BodyDwords,
        ReadMessageOffsets = source.ReadMessageOffsets,
        ScriptCallOffsets = source.ScriptCallOffsets,
        LocalCallOffsets = source.LocalCallOffsets,
    };

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    public void AtALoadDataLayeredComesAgainAndTuneLowersTheRestoredLevel()
    {
        var (linkBase, scripts) = Himegari();
        ModLinkResult linked = Linked(linkBase, TuneMod("layered"));
        int mov = HimegariTable.ByLabel("mov")!.Value;
        int callScript = HimegariTable.ByLabel("call-script")!.Value;
        uint init2 = scripts.RequireByName("INIT2.BIN").PackedId;
        // ROOT, as the game's own root has it: the prologue calls INIT2, op 0xae ends a load's prologue, and then the
        // player tunes item 999 to 5 and saves (op 0x1ad, then slot 1).
        Script root = WithId(ScriptAssembler.Assemble(HimegariTable, "ROOT.BIN", new List<(int, Operand[])>
        {
            (callScript, [new(T_IMM, init2)]),
            (0xae, []),
            (mov, [new(T_GINT, TuningLevel), new(T_IMM, 5)]),
            (0x1ad, []),
            (0x19e, [new(T_GINT, 0x20), new(T_IMM, 1)]),
            (0x2, []),
        }, []), RootId);
        Script loader = ScriptAssembler.Assemble(HimegariTable, "LOADER.BIN", new List<(int, Operand[])>
        {
            (0x1a1, [new(T_LINT, 0), new(T_IMM, 1)]),
            (0x2, []),
        }, []);
        Script callback = ScriptAssembler.Assemble(HimegariTable, "CALLBACK_LOAD.BIN", new List<(int, Operand[])> { (0x2, []) }, []);
        var provider = new WithRoot(scripts, root, callback);
        string saves = Path.Combine(_root, "saves");
        Directory.CreateDirectory(saves);
        INativeDatStore Store() => new SidecarNativeDatStore(new DirectoryNativeDatStore(saves, Identity), saves,
                                                             Scripting.StateFileSuffix, currentSidecar: null);

        Scripting first = Native(linked);
        var played = new VirtualMachine(root, HimegariTable, new HeadlessWaits(), provider: provider,
                                        saves: new SavesAndSettings(store: Store()), scripting: first);
        played.SeedGlobal(0x20, 7);
        played.Run();
        Assert.Equal("exit", played.HaltReason);
        Assert.Equal(0, played.Globals.GetValueOrDefault(0x20));   // the save was written: its status replaced the 7
        // At boot the level is 0, so TUNE has nothing to lower.
        Assert.Equal(["layered level=0", "ready level=0"], Lines(first));

        Scripting second = Native(linked);
        var loaded = new VirtualMachine(loader, HimegariTable, new HeadlessWaits(), provider: provider,
                                        saves: new SavesAndSettings(store: Store()), scripting: second);
        loaded.Run();
        Assert.Equal("exit", loaded.HaltReason);
        // The load zeroes the banks and brings the saved level back; ROOT's prologue runs INIT2 again, which raises
        // both events again, restoring. The edit at data.layered is made again on the rebuilt tables, and TUNE lowers
        // the restored level. (ROOT then resumes after op 0xae and sets it to 5 again.)
        Assert.Equal(["layered level=5 restoring", "ready level=2 restoring"], Lines(second));
    }

    // ------------------------------------------------------------------------------------------------- Kamidori

    [Theory]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [InlineData(false)]
    [InlineData(true)]
    public void KamidorisDataLayeredComesBeforeDataReadyAndAnEditThereHolds(bool translated)
    {
        string gameRoot = translated ? Paths.KamidoriTranslatedDir : Path.Combine(Paths.Workspace, "Kamidori");
        string[] looseRoots = translated ? [Path.Combine(gameRoot, "patch"), gameRoot] : [gameRoot];
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");
        var catalog = Sys4AssetCatalog.Load(sys4Ini);
        var store = new Sys4AssetStore(catalog, gameRoot, looseRoots);
        ModLinkBase linkBase = ModLinkBase.ForInstall("kamidori", sys4Ini, catalog, store, KamidoriTable, Contracts,
            Paths.GlobalsJson("kamidori"), Paths.OmsModules("kamidori"), Paths.PatchPointsJson("kamidori"));
        var scripts = new Sys4ScriptProvider(KamidoriTable, catalog, store);

        // 緑プテテット (EBINIT unit 761): its experience reward, 10, set to 999 at data.layered.
        ModLinkResult linked = Linked(linkBase, TestMod("kamidori", "\"unit_experience_reward\"", """
            local game = require("kamidori-base")
            local function reward() return tostring(game.table("unit_experience_reward")[761]) end
            function on_layered(e)
                maid.log.info("layered " .. reward())
                game.table("unit_experience_reward")[761] = 999
            end
            function on_ready(e) maid.log.info("ready " .. reward()) end
            """));
        Assert.Equal([0x20d, 0x2cf], linked.Lua.Plan.Sites["INIT2"].Select(site => site.Offset));

        Scripting native = Native(linked);
        var session = new GameSession();
        SceneResult result = session.RunScene(scripts.RequireByName("INIT2.BIN"), KamidoriTable, new HeadlessWaits(),
                                              provider: scripts, build: EngineBuilds.Age433G, scripting: native);
        Assert.True(result.Halt is null or "exit" or "end", result.Halt);
        // At data.layered the tables hold what every layer wrote; INIT2's own work after it leaves the edit.
        Assert.Equal(["layered 10", "ready 999"], Lines(native));
        Assert.Equal(999, session.Globals.GetValueOrDefault(0x2c8f8c + 761));
    }

    // ------------------------------------------------------------------------------------------------- gate 5's cells

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void TheGate6WithBlockCompilesToTheCellsTheApplierWrites()
    {
        // The registry's globals.json carries the install's derived rows, so this reads the workspace's build.
        string path = Path.Combine(Paths.Repo, "oms", "examples", "kamidori-gate6", "OMSAUTO.oms");
        GameModule globals = GameModule.LoadGlobals("kamidori", Paths.GlobalsJson("kamidori"));
        ScriptUnit unit = Parser.ParseScript(File.ReadAllText(path), path);
        IrScript compiled = OmsCompiler.Compile(unit, KamidoriTable.AbiId, KamidoriTable, null,
            new IntrinsicCatalog(KamidoriTable, Contracts), name => name == globals.Name ? globals : null, new List<OmsWarning>());
        Script script = Sys4Loader.Parse(IrEncoder.Encode(compiled), KamidoriTable, "OMSAUTO.BIN");
        int mov = KamidoriTable.ByLabel("mov")!.Value;
        Assert.Equal(new Dictionary<int, long> { [0x2c1524] = 150, [0x2c9285] = 999, [0x2cb9c1] = 100 },
                     script.Instructions.Where(instruction => instruction.Opcode == mov && instruction.Args[0].Type == T_GINT
                                                             && instruction.Args[1].Type == T_IMM)
                         .ToDictionary(instruction => (int)instruction.Args[0].Value, instruction => instruction.Args[1].Value));
    }
}
