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
using Xunit.Abstractions;

/// <summary>
/// Milestone 1 of docs/superpowers/specs/2026-11-17-data-patches-design.md, on the installed games: data files shipped
/// by mods with no Lua, linked with the engine's base mods and delivered to their <c>tables.lua:apply_boot</c> at
/// <c>&lt;game&gt;.data.layered</c> while the installed INIT2 runs. Gate 3's last part (a handler on
/// <c>data.ready</c> finds every file applied), gate 5 (spec §1.3's file leaves exactly the cells the
/// <c>kamidori-gate6</c> example's <c>with</c> block leaves, and its twin on Himegari), gate 6 (boot, load, return to
/// title, a load without the mod, and the save made right after boot) and gate 7 (each problem reported with the
/// contributor, the file and the path, nothing applied, and the halt), gates 6 and 7 on both games.
/// </summary>
public sealed partial class DataPatchWorkspaceTests
{
    private readonly ITestOutputHelper? _output;

    public DataPatchWorkspaceTests(ITestOutputHelper output) => _output = output;

    // ------------------------------------------------------------------------------------------------- helpers

    /// <summary>A mod with no Lua that ships <paramref name="text"/> as <c>data/FILE</c>, of the kind
    /// <c>&lt;profile&gt;.tables</c>.</summary>
    private ModPackage DataMod(string profile, string id, string file, string text)
    {
        string folder = Path.Combine(_root, id);
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), $"""
            omp = 4
            id = "{id}"
            name = "{id}"
            version = "1.0.0"

            [game]
            profile = "{profile}"
            target = "extended"

            [[requires]]
            id = "{profile}-base"
            version = ">=1.1, <2"

            [[data]]
            kind = "{profile}.tables"
            file = "{file}"
            """);
        File.WriteAllText(Path.Combine(folder, "data", file), text);
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        return mod;
    }

    /// <summary>A Lua mod that logs <c>global[row]</c> at both boot events, with " restoring" during a load.</summary>
    private ModPackage Reader(string profile, string global, int row)
    {
        string folder = Path.Combine(_root, "test.reader");
        Directory.CreateDirectory(Path.Combine(folder, "lua"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), $"""
            omp = 3
            id = "test.reader"
            name = "test.reader"
            version = "1.0.0"

            [game]
            profile = "{profile}"
            target = "extended"

            [lua]
            api = 1
            capabilities = ["vm"]

            [[requires]]
            id = "{profile}-base"
            version = ">=1.1, <2"

            [[hooks]]
            event = "{profile}.data.layered"
            handler = "reader.lua:on_layered"

            [[hooks]]
            event = "{profile}.data.ready"
            handler = "reader.lua:on_ready"
            """);
        File.WriteAllText(Path.Combine(folder, "lua", "reader.lua"), $$"""
            local game = require("{{profile}}-base")
            local function value()
                return tostring(game.table("{{global}}")[{{row}}]) .. (maid.session.restoring and " restoring" or "")
            end
            function on_layered(e) maid.log.info("layered " .. value()) end
            function on_ready(e) maid.log.info("ready " .. value()) end
            """);
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        return mod;
    }

    /// <summary><paramref name="mods"/> in this load order, with the shipped packages they require, linked.</summary>
    private ModLinkResult LinkedInOrder(ModLinkBase linkBase, params ModPackage[] mods)
    {
        IReadOnlyList<ModPackage> shipped = ShippedPackages.Required(mods, new ModLinkReport());
        _opened.AddRange(shipped);
        ModLinkResult linked = ModLinker.Link(linkBase with { Lua = _runtime }, [.. mods, .. shipped],
                                              mods.Select(mod => mod.Id).ToList());
        Assert.True(linked.Succeeded, linked.Report.Render());
        return linked;
    }

    private Scripting Native(ModLinkResult linked, LuaErrorPolicy errors)
    {
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = errors, Trace = true,
        });
        _owned.Add(native);
        return native;
    }

    /// <summary><paramref name="mod"/>'s log lines, without the time, the level and the mod.</summary>
    private static string[] Logged(Scripting native, string mod)
        => native.RecentLog.Where(line => line.Contains($" {mod}: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf($" {mod}: ", StringComparison.Ordinal) + mod.Length + 3)..]).ToArray();

    /// <summary>The cell of <paramref name="global"/>'s <paramref name="row"/>, by the shipped layout.</summary>
    private static int Cell(string profile, string global, int row, string? column = null)
    {
        using ModPackage baseMod = ShippedPackages.Open($"{profile}-base")!;
        Assert.True(baseMod.BaseLayout!.TryGet(global, out LayoutGlobal found));
        return column is null ? found.Address + row : found.Address + row * found.RowWidth + found.Columns[column];
    }

    private static (ModLinkBase Base, Sys4ScriptProvider Scripts) Kamidori(bool translated)
    {
        string gameRoot = translated ? Paths.KamidoriTranslatedDir : Path.Combine(Paths.Workspace, "Kamidori");
        string[] looseRoots = translated ? [Path.Combine(gameRoot, "patch"), gameRoot] : [gameRoot];
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        Assert.True(File.Exists(sys4Ini), $"Kamidori install not found at {gameRoot}");
        var catalog = Sys4AssetCatalog.Load(sys4Ini);
        var store = new Sys4AssetStore(catalog, gameRoot, looseRoots);
        ModLinkBase linkBase = ModLinkBase.ForInstall("kamidori", sys4Ini, catalog, store, KamidoriTable, Contracts,
            Paths.GlobalsJson("kamidori"), Paths.OmsModules("kamidori"), Paths.PatchPointsJson("kamidori"));
        return (linkBase, new Sys4ScriptProvider(KamidoriTable, catalog, store));
    }

    /// <summary>Kamidori's INIT2, headless, with <paramref name="native"/> or with no mod at all.</summary>
    private static GameSession KamidoriBoot(Sys4ScriptProvider scripts, Scripting? native)
    {
        var session = new GameSession();
        SceneResult result = session.RunScene(scripts.RequireByName("INIT2.BIN"), KamidoriTable, new HeadlessWaits(),
                                              provider: scripts, build: EngineBuilds.Age433G, scripting: native);
        Assert.True(result.Halt is null or "exit" or "end", result.Halt);
        return session;
    }

    /// <summary>Himegari's INIT2, headless, with <paramref name="native"/> or with no mod at all.</summary>
    private static GameSession HimegariBoot(Sys4ScriptProvider scripts, Scripting? native)
    {
        var session = new GameSession();
        SceneResult result = session.RunScene(scripts.RequireByName("INIT2.BIN"), HimegariTable, new HeadlessWaits(),
                                              provider: scripts, scripting: native);
        Assert.True(result.Halt is null or "exit" or "end", result.Halt);
        return session;
    }

    /// <summary>The integer cells two runs leave differently, with the second run's values.</summary>
    private static Dictionary<int, long> Changed(IReadOnlyDictionary<int, long> before, IReadOnlyDictionary<int, long> after)
        => before.Keys.Union(after.Keys)
            .Where(cell => before.GetValueOrDefault(cell) != after.GetValueOrDefault(cell))
            .ToDictionary(cell => cell, cell => after.GetValueOrDefault(cell));

    // ------------------------------------------------------------------------------------------------- gate 3

    [Theory]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    [InlineData(false)]
    [InlineData(true)]
    public void AHandlerAtDataReadyFindsEveryFileAppliedWhateverTheLoadOrder(bool swapped)
    {
        var (linkBase, scripts) = Himegari();
        // The reader comes first in the load order: at data.layered it runs before both files, and at data.ready,
        // which comes later in the boot, after them.
        ModPackage reader = Reader("himegari", "unit_experience_reward", 103);
        ModPackage a = DataMod("himegari", "test.data-a", "a.toml", "[unit.103]\nexperience_reward = 40\n");
        ModPackage b = DataMod("himegari", "test.data-b", "b.toml", "[unit.103]\nexperience_reward = 41\n");
        ModLinkResult linked = LinkedInOrder(linkBase, swapped ? [reader, b, a] : [reader, a, b]);
        string first = swapped ? "test.data-b" : "test.data-a", second = swapped ? "test.data-a" : "test.data-b";
        string report = linked.Report.Render();
        Assert.Contains($"""
              kind himegari.tables (himegari-base), at himegari.data.layered and himegari.stage.loaded
                {first} data/{first[^1..]}.toml: 1 value
                {second} data/{second[^1..]}.toml: 1 value
                unit.103.experience_reward: {first}, then {second} (shared path, in delivery order)
            """.ReplaceLineEndings("\n"), report.ReplaceLineEndings("\n"));

        Scripting native = Native(linked, LuaErrorPolicy.Halt);
        HimegariBoot(scripts, native);
        long own = HimegariBoot(scripts, null).Globals.GetValueOrDefault(Cell("himegari", "unit_experience_reward", 103));
        Assert.Equal(30, own);
        Assert.Equal([$"layered {own}", $"ready {(swapped ? 40 : 41)}"], Logged(native, "test.reader"));
    }

    // ------------------------------------------------------------------------------------------------- gate 5

    /// <summary>Spec §1.3's file, as <c>example.tougher-slimes</c> ships it.</summary>
    private const string Slimes = """
        [unit.761]                         # the unit oms/examples/kamidori-gate6 edits
        stats.max_hp = 150                 # 12
        experience_reward = 999            # 10
        drop_chance.drop_1 = 100           # 30
        """;

    [Theory]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSpecsFileShippedByAModLeavesUnit761AsTheWithBlockDoesAndNothingElse(bool translated)
    {
        var (linkBase, scripts) = Kamidori(translated);
        GameSession plain = KamidoriBoot(scripts, null);
        ModLinkResult linked = LinkedInOrder(linkBase, DataMod("kamidori", "example.tougher-slimes", "slimes.toml", Slimes));
        Scripting native = Native(linked, LuaErrorPolicy.Halt);
        GameSession edited = KamidoriBoot(scripts, native);

        // The cells oms/examples/kamidori-gate6's with block compiles to (TheGate6WithBlockCompilesToTheCellsTheApplierWrites).
        Dictionary<int, long> cells = new() { [0x2c1524] = 150, [0x2c9285] = 999, [0x2cb9c1] = 100 };
        Assert.Equal(cells, Changed(plain.Globals, edited.Globals));
        Assert.Equal([12L, 10L, 30L], cells.Keys.Select(cell => plain.Globals.GetValueOrDefault(cell)));
        Assert.Equal(plain.GlobalStrings, edited.GlobalStrings);
        Assert.Equal(plain.GlobalFloats, edited.GlobalFloats);
        // The trace names the contributor for the writes, through the owner.
        string delivery = Assert.Single(native.RecentTrace, line => line.Contains("\"file\":\"data/slimes.toml\"", StringComparison.Ordinal));
        Assert.Contains("\"mod\":\"example.tougher-slimes\"", delivery, StringComparison.Ordinal);
        Assert.Contains("\"owner\":\"kamidori-base\",\"handler\":\"tables.lua:apply_boot\"", delivery, StringComparison.Ordinal);
        Assert.Contains("\"result\":\"ok\"", delivery, StringComparison.Ordinal);
        _output?.WriteLine(delivery);
    }

    /// <summary>Gate 5's twin on Himegari, whose examples have no <c>with</c> block: unit 103's three cells of the same
    /// kinds as spec §1.3's (a row's named column, a field with one value, a second row's column), as an OMS
    /// <c>with</c> block and as a data file.</summary>
    private const string HimegariWith = """
        oms 1
        script "$2$OMSAUTO.BIN"
        use himegari.globals

            with unit[103] {
                .stats.max_hp = 150
                .experience_reward = 999
                .drop_chance.drop_1_percent = 77
            }
            exit
        """;

    private const string HimegariFile = """
        [unit.103]
        stats.max_hp = 150
        experience_reward = 999              # 30
        drop_chance.drop_1_percent = 77
        """;

    /// <summary>The cells an OMS script's <c>mov</c>s of a constant into a global set, by the registry the workspace
    /// built (its globals.json carries the install's derived rows).</summary>
    private static Dictionary<int, long> WithBlockCells(string profile, OpcodeTable table, string source)
    {
        GameModule globals = GameModule.LoadGlobals(profile, Paths.GlobalsJson(profile));
        ScriptUnit unit = Parser.ParseScript(source, "OMSAUTO.oms");
        IrScript compiled = OmsCompiler.Compile(unit, table.AbiId, table, null, new IntrinsicCatalog(table, Contracts),
            name => name == globals.Name ? globals : null, new List<OmsWarning>());
        Script script = Sys4Loader.Parse(IrEncoder.Encode(compiled), table, "OMSAUTO.BIN");
        int mov = table.ByLabel("mov")!.Value;
        return script.Instructions.Where(instruction => instruction.Opcode == mov && instruction.Args[0].Type == T_GINT
                                                        && instruction.Args[1].Type == T_IMM)
            .ToDictionary(instruction => (int)instruction.Args[0].Value, instruction => instruction.Args[1].Value);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "himegari")]
    public void OnHimegariAFileLeavesUnit103AsTheSameWithBlockDoesAndNothingElse()
    {
        var (linkBase, scripts) = Himegari();
        GameSession plain = HimegariBoot(scripts, null);
        ModLinkResult linked = LinkedInOrder(linkBase, DataMod("himegari", "example.tougher-103", "unit103.toml", HimegariFile));
        GameSession edited = HimegariBoot(scripts, Native(linked, LuaErrorPolicy.Halt));

        Dictionary<int, long> cells = WithBlockCells("himegari", HimegariTable, HimegariWith);
        Assert.Equal(3, cells.Count);
        Assert.Equal(cells, Changed(plain.Globals, edited.Globals));
        Assert.Equal(30, plain.Globals.GetValueOrDefault(Cell("himegari", "unit_experience_reward", 103)));
        Assert.Equal(plain.GlobalStrings, edited.GlobalStrings);
        Assert.Equal(plain.GlobalFloats, edited.GlobalFloats);
    }

    // ------------------------------------------------------------------------------------------------- gate 6

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }

    /// <summary>What gates 6 and 7 need of a game: its install, the opcodes and the build its INIT2 runs, its save
    /// identity, the unit whose experience reward a file sets with that reward as the boot leaves it, and the place of
    /// <c>&lt;game&gt;.data.layered</c>.</summary>
    private sealed record GateGame(string Profile, ModLinkBase Base, Sys4ScriptProvider Scripts, OpcodeTable Table,
                                   EngineBuild Build, NativeSaveIdentity Identity, int Unit, long Reward, string Layered)
    {
        public GameSession Boot(Scripting? native)
            => Profile == "himegari" ? HimegariBoot(Scripts, native) : KamidoriBoot(Scripts, native);
    }

    private static GateGame Game(string profile)
    {
        if (profile == "himegari")
        {
            var (himegari, scripts) = Himegari();
            return new GateGame(profile, himegari, scripts, HimegariTable, EngineBuilds.Age424J, Identity, 103, 30,
                                "INIT2.BIN@0x180");
        }
        var (kamidori, kamidoriScripts) = Kamidori(translated: false);
        return new GateGame(profile, kamidori, kamidoriScripts, KamidoriTable, EngineBuilds.Age433G, KamidoriIdentity.Value,
                            761, 10, "INIT2.BIN@0x20d");
    }

    [Theory]
    [Trait("Category", "Workspace")]
    [InlineData("himegari")]
    [InlineData("kamidori")]
    public void TheEditHoldsAtBootAtALoadAndAfterAReturnToTitleAndASaveKeepsNoneOfIt(string profile)
    {
        GateGame game = Game(profile);
        string global = "unit_experience_reward";
        long own = game.Boot(null).Globals.GetValueOrDefault(Cell(profile, global, game.Unit));
        Assert.Equal(game.Reward, own);
        OpcodeTable table = game.Table;
        int callScript = table.ByLabel("call-script")!.Value;
        uint init2 = game.Scripts.RequireByName("INIT2.BIN").PackedId;
        // ROOT, as the game's own has it: the prologue calls INIT2, op 0xae ends a load's prologue; then a save to
        // slot 1 right after boot (op 0x1ad, then op 0x19e), and op 0x9, the return to title, which zeroes the banks
        // and loads raw script 0. Script 0 here is a title that calls INIT2 again and exits.
        Script root = WithId(ScriptAssembler.Assemble(table, "ROOT.BIN", new List<(int, Operand[])>
        {
            (callScript, [new(T_IMM, init2)]),
            (0xae, []),
            (0x1ad, []),
            (0x19e, [new(T_GINT, 0x20), new(T_IMM, 1)]),
            (0x9, []),
        }, []), RootId);
        Script title = WithId(ScriptAssembler.Assemble(table, "TITLE.BIN", new List<(int, Operand[])>
        {
            (callScript, [new(T_IMM, init2)]),
            (0x2, []),
        }, []), 0);
        Script loader = ScriptAssembler.Assemble(table, "LOADER.BIN", new List<(int, Operand[])>
        {
            (0x1a1, [new(T_LINT, 0), new(T_IMM, 1)]),
            (0x2, []),
        }, []);
        Script callback = ScriptAssembler.Assemble(table, "CALLBACK_LOAD.BIN", new List<(int, Operand[])> { (0x2, []) }, []);
        var provider = new WithRoot(game.Scripts, root, callback, title);
        INativeDatStore Saves(string folder)
            => new SidecarNativeDatStore(new DirectoryNativeDatStore(folder, game.Identity), folder,
                                         Scripting.StateFileSuffix, currentSidecar: null);

        // The data mod sets the unit's experience reward; the reader logs it at both boot events of every run.
        ModLinkResult withMod = LinkedInOrder(game.Base, Reader(profile, global, game.Unit),
                                              DataMod(profile, "test.data-a", "a.toml", $"[unit.{game.Unit}]\nexperience_reward = 40\n"));
        ModLinkResult withoutMod = LinkedInOrder(game.Base, Reader(profile, global, game.Unit));
        string[] Run(Script start, ModLinkResult linked, string saves)
        {
            Directory.CreateDirectory(saves);
            Scripting native = Native(linked, LuaErrorPolicy.Halt);
            var vm = new VirtualMachine(start, table, new HeadlessWaits(), provider: provider,
                                        saves: new SavesAndSettings(store: Saves(saves)), build: game.Build, scripting: native);
            vm.Run();
            Assert.Equal("exit", vm.HaltReason);
            return Logged(native, "test.reader");
        }

        // At boot, and again after the return to title: the tables are built again, and the file is applied again.
        string boot = Path.Combine(_root, "saves-boot"), plainBoot = Path.Combine(_root, "saves-plain");
        Assert.Equal([$"layered {own}", "ready 40", $"layered {own}", "ready 40"], Run(root, withMod, boot));
        Assert.Equal([$"layered {own}", $"ready {own}", $"layered {own}", $"ready {own}"], Run(root, withoutMod, plainBoot));
        string slot = DirectoryNativeDatStore.NumberedFileName(1);
        Assert.True(File.Exists(Path.Combine(boot, slot)));

        // The save made right after boot is the same with and without the mod: the tables lie above the saved prefix,
        // and the file sets no field the game derives a saved value from. Only the time of the save differs.
        NativeSaveComparison comparison = NativeSaveDiff.Compare(
            new DirectoryNativeDatStore(boot, game.Identity).LoadNumbered(1)!,
            new DirectoryNativeDatStore(plainBoot, game.Identity).LoadNumbered(1)!);
        Assert.True(comparison.Numbered, comparison.NumberedDecodeError);
        Assert.Empty(comparison.Differences.Where(difference =>
            difference is not { Section: NativeSaveDiff.Metadata, Field: "timestamp" or "play-seconds" }));

        // A load with the mod: the load zeroes the banks, INIT2 builds the tables again while the load restores, and
        // the file is applied again. ROOT then goes on after its save and returns to title, where it is applied again.
        string loadWith = Path.Combine(_root, "saves-load-with"), loadWithout = Path.Combine(_root, "saves-load-without");
        CopyFolder(boot, loadWith);
        CopyFolder(boot, loadWithout);
        string[] loaded = Run(loader, withMod, loadWith);
        Assert.Equal([$"layered {own} restoring", "ready 40 restoring", $"layered {own}", "ready 40"], loaded);
        // The save made with the mod, loaded without it: the game's own value.
        string[] loadedWithout = Run(loader, withoutMod, loadWithout);
        Assert.Equal([$"layered {own} restoring", $"ready {own} restoring", $"layered {own}", $"ready {own}"], loadedWithout);
    }

    // ------------------------------------------------------------------------------------------------- gate 7

    public static TheoryData<string, string, string, string> FileProblems => new()
    {
        // The file's other part, the unit's max_hp, is valid: a problem anywhere applies nothing.
        { "kamidori", "[unit.761]\nstats.max_hp = 150\nexperiance_reward = 999\n", "unit.761.experiance_reward", "unit has no field experiance_reward" },
        { "kamidori", "[unit.761]\nstats.max_hp = 150\n\n[item.5]\ninventory_count = 9\n", "item.5.inventory_count",
          "item.inventory_count is saved state: a load brings the saved value back, so a file does not set it" },
        { "kamidori", "[unit.761]\nstats.max_hp = 150\n\n[unit.0x2f9]\nexperience_reward = 999\n", "unit.0x2f9", "a record id is written in decimal: 761" },
        { "kamidori", "[unit.761]\nstats.max_hp = 150\ndrop_chance.0 = 100\n", "unit.761.drop_chance.0", "column 0 is named: write unit.761.drop_chance.drop_1" },
        { "kamidori", "[unit.761]\nstats.max_hp = 150\nexperience_reward = \"999\"\n", "unit.761.experience_reward", "takes a whole number, not a string" },
        { "kamidori", "[unit.761]\nstats.max_hp = 150\n\n[item.12]\nname = \"Lily 한\"\n", "item.12.name",
          "a string cell holds text the save can encode (code page 932); U+D55C is not in it" },
        { "himegari", "[unit.103]\nstats.max_hp = 150\nexperiance_reward = 999\n", "unit.103.experiance_reward", "unit has no field experiance_reward" },
        { "himegari", "[unit.103]\nstats.max_hp = 150\n\n[skill.5]\nacquired_flag = 1\n", "skill.5.acquired_flag",
          "skill.acquired_flag is saved state: a load brings the saved value back, so a file does not set it" },
        { "himegari", "[unit.103]\nstats.max_hp = 150\n\n[unit.0x67]\nexperience_reward = 999\n", "unit.0x67", "a record id is written in decimal: 103" },
        { "himegari", "[unit.103]\nstats.max_hp = 150\ndrop_chance.0 = 100\n", "unit.103.drop_chance.0",
          "column 0 is named: write unit.103.drop_chance.drop_1_percent" },
        { "himegari", "[unit.103]\nstats.max_hp = 150\nexperience_reward = \"999\"\n", "unit.103.experience_reward", "takes a whole number, not a string" },
        { "himegari", "[unit.103]\nstats.max_hp = 150\n\n[unit.12]\ndisplay_name = \"Lily 한\"\n", "unit.12.display_name",
          "a string cell holds text the save can encode (code page 932); U+D55C is not in it" },
        // A stage part's problems are the boot's too: the whole file is checked at its first delivery (plan §5.2).
        { "himegari", "[unit.103]\nstats.max_hp = 150\n\n[stage.0.enemy.2]\nunit_id = 810\n", "stage.0", "stage has ids 1 to 999" },
        { "himegari", "[unit.103]\nstats.max_hp = 150\n\n[stage.1.enemyy.2]\nunit_id = 810\n", "stage.1.enemyy",
          "stage has no table enemyy (tables: enemy)" },
        { "himegari", "[unit.103]\nstats.max_hp = 150\n\n[stage_enemy.2]\nunit_id = 810\n", "stage_enemy",
          "stage_enemy is reached through its scope: write stage.<id>.enemy" },
        { "kamidori", "[unit.761]\nstats.max_hp = 150\n\n[stage.51.enemy.30]\nunit_id = 810\n", "stage.51.enemy.30",
          "stage_enemy has records 0 to 29" },
        { "kamidori", "[unit.761]\nstats.max_hp = 150\n\n[stage.51.enemy.2]\nrequired_story_flags.7 = 1\n",
          "stage.51.enemy.2.required_story_flags.7", "stage_enemy.required_story_flags has columns 0 to 6" },
    };

    [Theory]
    [Trait("Category", "Workspace")]
    [MemberData(nameof(FileProblems))]
    public void EachProblemOfAShippedFileIsReportedWithTheContributorTheFileAndThePathAndNothingOfItIsApplied(
        string profile, string text, string path, string reason)
    {
        GateGame game = Game(profile);
        GameSession plain = game.Boot(null);
        ModLinkResult linked = LinkedInOrder(game.Base, DataMod(profile, "test.bad", "bad.toml", text));
        Scripting native = Native(linked, LuaErrorPolicy.Continue);
        GameSession booted = game.Boot(native);

        Assert.Empty(Changed(plain.Globals, booted.Globals));
        Assert.Equal(plain.GlobalStrings, booted.GlobalStrings);
        string logged = Assert.Single(Logged(native, "test.bad"));
        Assert.StartsWith($"data/bad.toml ({profile}-base tables.lua:apply_boot) failed at {profile}.data.layered at {game.Layered}: "
                          + $"test.bad data/bad.toml: 1 problem, so nothing of it is applied: {path}: {reason}\nstack traceback:", logged);
    }

    [Theory]
    [Trait("Category", "Workspace")]
    [InlineData("himegari")]
    [InlineData("kamidori")]
    public void UnderHaltAShippedFilesProblemEndsTheBootNamingTheContributorItsFileAndThePath(string profile)
    {
        GateGame game = Game(profile);
        ModLinkResult linked = LinkedInOrder(game.Base,
            DataMod(profile, "test.bad", "bad.toml", $"[unit.{game.Unit}]\nstats.max_hp = 150\nexperiance_reward = 999\n"));
        Scripting native = Native(linked, LuaErrorPolicy.Halt);
        var session = new GameSession();
        SceneResult result = session.RunScene(game.Scripts.RequireByName("INIT2.BIN"), game.Table, new HeadlessWaits(),
                                              provider: game.Scripts, build: game.Build, scripting: native);
        // The halt reason is the error's first line, which carries the first problem's path; it names the contributor
        // once.
        Assert.Equal($"lua-error: test.bad data/bad.toml ({profile}-base tables.lua:apply_boot) at {profile}.data.layered "
                     + $"at {game.Layered}: 1 problem, so nothing of it is applied: "
                     + $"unit.{game.Unit}.experiance_reward: unit has no field experiance_reward", result.Halt);
        Assert.NotEqual(150, session.Globals.GetValueOrDefault(Cell(profile, "unit_base_stats", game.Unit, "max_hp")));
    }
}
