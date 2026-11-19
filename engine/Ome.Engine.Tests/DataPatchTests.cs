using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Script.Abi;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 0 of docs/superpowers/specs/2026-11-17-data-patches-design.md: the applier, before the engine delivers a
/// file. The shipped base mods' <c>tables.lua</c> and the <c>ome-records</c> library they require, linked from the
/// engine's own packages, take a document as a Lua table from a console chunk in the base mod's environment (spec §13):
/// the applier's half of gate 5 (the same cells as <c>oms/examples/kamidori-gate6</c>) and of gate 7 (each problem is
/// reported with the mod, the file and the path, and nothing of the file is written). The library alone takes a
/// read-only view, as milestone 1 will hand it one.
/// </summary>
public sealed class DataPatchTests : IDisposable
{
    private readonly List<ModPackage> _opened = new();
    private readonly List<IDisposable> _owned = new();
    private readonly LazyModRuntime _runtime = LuaModRuntime.Lazy();

    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);

    public void Dispose()
    {
        // The natives' sessions end before the runtime that built them.
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        foreach (ModPackage package in _opened) package.Dispose();
        _runtime.Dispose();
    }

    // ------------------------------------------------------------------------------------------------- helpers

    /// <summary>A link base for <paramref name="profile"/> with no script but a wait-free ADDITEM.BIN, which the base
    /// mods' <c>[lua] calls</c> name. The base mod's own copy of the patch points is used, and binds nothing here.</summary>
    private ModLinkBase LinkBase(string profile)
    {
        string abi = profile == "himegari" ? "SYS4422" : "SYS4433";
        OpcodeTable table = OpcodeTableJson.Load(Paths.OpcodesJson, abi);
        byte[] addItem = ScriptAssembler.AssembleBytes(new List<(int, Operand[])> { (0x5, []) }, [], abi);
        return new ModLinkBase
        {
            Profile = profile,
            CatalogFingerprint = "sha256:" + new string('1', 64),
            ReadScript = name => name.Equals("ADDITEM.BIN", StringComparison.OrdinalIgnoreCase) ? addItem : null,
            Table = table,
            Contracts = Contracts,
            Intrinsics = new IntrinsicCatalog(table, Contracts),
            Lua = _runtime,
        };
    }

    /// <summary>The engine's own base mod of <paramref name="profile"/> with what it requires, linked, and a session.</summary>
    private Scripting Started(string profile)
    {
        ModPackage baseMod = ShippedPackages.Open($"{profile}-base")!;
        _opened.Add(baseMod);
        IReadOnlyList<ModPackage> required = ShippedPackages.Required([baseMod], new ModLinkReport());
        _opened.AddRange(required);
        ModLinkResult result = ModLinker.Link(LinkBase(profile), [baseMod, .. required], []);
        Assert.True(result.Succeeded, result.Report.Render());
        Assert.Equal(["ome-records", $"{profile}-base"], result.Lua.Stack.Mods.Select(mod => mod.Id));
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = result.Lua.Stack, Plan = result.Lua.Plan,
        });
        _owned.Add(native);
        Assert.Null(native.StartSession(SessionReason.Boot));
        return native;
    }

    private static string Chunk(Scripting native, string mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        Assert.True(request.Done);
        Assert.True(request.Result!.Succeeded, request.Result.Text);
        return request.Result.Text;
    }

    /// <summary>A console chunk in the base mod's environment that hands <paramref name="document"/> (a Lua table
    /// constructor) to its <c>apply_boot</c> twice, as two deliveries of one file would: what the first gave or
    /// raised, and what the second gave.</summary>
    private static (bool Applied, string First, string Second) ApplyTwice(Scripting native, string profile, string file,
                                                                          string document)
    {
        string text = Chunk(native, $"{profile}-base", """
            require("tables")
            local contribution = { mod = "example.data", file = "FILE", data = DOCUMENT }
            local ok, first = pcall(apply_boot, nil, contribution)
            return tostring(ok), first, apply_boot(nil, contribution)
            """.Replace("FILE", file).Replace("DOCUMENT", document));
        string[] parts = text.Split('\t');
        Assert.Equal(3, parts.Length);
        return (parts[0] == "true", parts[1], parts[2]);
    }

    private static int Address(string profile, string global, int row = 0, int column = -1)
    {
        using ModPackage baseMod = ShippedPackages.Open($"{profile}-base")!;
        Assert.True(baseMod.BaseLayout!.TryGet(global, out LayoutGlobal found));
        return column < 0 ? found.Address + row : found.Address + row * found.RowWidth + column;
    }

    /// <summary>The cell of a named column in a row, by the shipped layout.</summary>
    private static int Address(string profile, string global, int row, string column)
    {
        using ModPackage baseMod = ShippedPackages.Open($"{profile}-base")!;
        Assert.True(baseMod.BaseLayout!.TryGet(global, out LayoutGlobal found));
        return found.Address + row * found.RowWidth + found.Columns[column];
    }

    // ------------------------------------------------------------------------------------------------- gate 5

    [Fact]
    public void TheSpecsFileAsALuaTableWritesTheCellsTheGate6WithBlockWrites()
    {
        // Spec §1.3's file, applied by kamidori-base: the three cells oms/examples/kamidori-gate6's with block
        // compiles to (DataPatchWorkspaceTests.TheGate6WithBlockCompilesToTheCellsTheApplierWrites), and no other cell.
        Assert.Equal(0x2c1524, Address("kamidori", "unit_base_stats", 761, "max_hp"));
        Assert.Equal(0x2c9285, Address("kamidori", "unit_experience_reward", 761));
        Assert.Equal(0x2cb9c1, Address("kamidori", "unit_drop_chance_percent", 761, "drop_1"));
        Scripting native = Started("kamidori");
        var (applied, written, again) = ApplyTwice(native, "kamidori", "data/slimes.toml", """
            { unit = { ["761"] = { stats = { max_hp = 150 }, experience_reward = 999, drop_chance = { drop_1 = 100 } } } }
            """);
        Assert.True(applied, written);
        Assert.Equal(("3", "3"), (written, again));
        Assert.Equal(new Dictionary<int, long> { [0x2c1524] = 150, [0x2c9285] = 999, [0x2cb9c1] = 100 },
                     native.Memory.Integers.ToDictionary(pair => pair.Key, pair => pair.Value));
        Assert.Empty(native.Memory.Strings);
        Assert.Empty(native.Memory.Floats);
    }

    [Fact]
    public void HimegarisBaseModAppliesItsOwnTablesWithTheSameLibrary()
    {
        // Spec §6.4: the library holds no game fact; himegari-base passes its own record map in.
        Scripting native = Started("himegari");
        var (applied, written, _) = ApplyTwice(native, "himegari", "data/items.toml", """
            { item = { ["14"] = { max_range = 3, stat_modifiers = { max_fs = 2 } } }, unit = { ["5"] = { experience_reward = 40 } } }
            """);
        Assert.True(applied, written);
        Assert.Equal("3", written);
        Assert.Equal(3, native.Memory.Integer(Address("himegari", "item_max_range", 14)));
        Assert.Equal(2, native.Memory.Integer(Address("himegari", "item_stat_modifiers", 14, 13)));
        Assert.Equal(40, native.Memory.Integer(Address("himegari", "unit_experience_reward", 5)));
        Assert.Equal(3, native.Memory.Integers.Count);
    }

    [Fact]
    public void ValuesAtTheEdgesOfA32BitCellAreTakenAsTheWriteTakesThem()
    {
        // The check's rule is CellRules.Integer's: -2^31 to 2^32-1, stored as the signed low 32 bits.
        Scripting native = Started("kamidori");
        var (applied, written, _) = ApplyTwice(native, "kamidori", "data/edges.toml", """
            { unit = { ["761"] = { experience_reward = 4294967295 }, ["762"] = { experience_reward = -2147483648 } } }
            """);
        Assert.True(applied, written);
        Assert.Equal(-1, native.Memory.Integer(0x2c8f8c + 761));
        Assert.Equal(int.MinValue, native.Memory.Integer(0x2c8f8c + 762));
    }

    // ------------------------------------------------------------------------------------------------- gate 7

    public static TheoryData<string, string, string> Problems => new()
    {
        // The document's other part, unit 761's max_hp, is valid: a problem anywhere writes nothing.
        { "experiance_reward = 999", "unit.761.experiance_reward", "unit has no field experiance_reward" },
        { "drop_chance = { drop_one = 100 }", "unit.761.drop_chance.drop_one", "unit.drop_chance has no column drop_one" },
        { "drop_chance = { [\"0\"] = 100 }", "unit.761.drop_chance.0", "column 0 is named: write unit.761.drop_chance.drop_1" },
        { "drop_chance = { 100, 50 }", "unit.761.drop_chance", "unit.drop_chance takes its columns by name, or by number where a column has no name, not a whole row as an array" },
        { "drop_chance = { [\"5\"] = 100 }", "unit.761.drop_chance.5", "unit.drop_chance has columns 0 to 4" },
        { "experience_reward = \"999\"", "unit.761.experience_reward", "takes a whole number, not a string" },
        { "experience_reward = 1.5", "unit.761.experience_reward", "takes a whole number in 32 bits, not 1.5" },
        { "experience_reward = 4294967296", "unit.761.experience_reward", "takes a whole number in 32 bits, not 4294967296" },
        { "experience_reward = true", "unit.761.experience_reward", "takes a whole number, not a boolean" },
        { "experience_reward = { 999 }", "unit.761.experience_reward", "unit.experience_reward takes one value, not a table" },
        { "stats = 150", "unit.761.stats", "unit.stats is a row of 14 columns: give its columns (stats.<column> = …)" },
        { "}, [\"0x2f9\"] = { experience_reward = 999", "unit.0x2f9", "a record id is written in decimal: 761" },
        { "}, [\"0761\"] = { experience_reward = 999", "unit.0761", "a record id is written without a leading zero: 761" },
        { "}, [\"-1\"] = { experience_reward = 999", "unit.-1", "a record id has no sign" },
        { "}, [\"1000\"] = { experience_reward = 999", "unit.1000", "unit has records 0 to 999" },
        { "} }, item = { [\"5\"] = { inventory_count = 9", "item.5.inventory_count", "item.inventory_count is saved state: a load brings the saved value back, so a file does not set it" },
        { "} }, item = { [\"12\"] = { name = 5", "item.12.name", "takes a string, not 5" },
        { "} }, item = { [\"12\"] = { name = \"Lily 한\"", "item.12.name", "a string cell holds text the save can encode (code page 932); U+D55C is not in it" },
        { "} }, stage_enemy = { [\"3\"] = { unit_id = 761", "stage_enemy", "stage_enemy is built by stage, which this format does not edit" },
        { "} }, units = { [\"3\"] = { level = 1", "units", "no record table is named units (tables: item, skill, stage_enemy, unit)" },
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public void EachProblemIsReportedWithTheModTheFileAndThePathAndNothingOfTheFileIsWritten(string part, string path,
                                                                                               string reason)
    {
        Scripting native = Started("kamidori");
        native.Memory.SeedInteger(0x2c1524, 12);   // unit 761's max_hp as EBINIT leaves it
        var (applied, message, again) = ApplyTwice(native, "kamidori", "data/bad.toml",
            "{ unit = { [\"761\"] = { stats = { max_hp = 150 }, " + part + " } } }");
        Assert.False(applied);
        Assert.Equal($"example.data data/bad.toml: 1 problem, so nothing of it is applied\n  {path}: {reason}", message);
        // The second delivery of the file passes it over: nothing written, and no second error.
        Assert.Equal("0", again);
        Assert.Equal(12, native.Memory.Integer(0x2c1524));
        Assert.Single(native.Memory.Integers);
        Assert.Empty(native.Memory.Strings);
    }

    [Fact]
    public void ADocumentsProblemsAreListedByPathAndALongListIsCut()
    {
        Scripting native = Started("kamidori");
        var (applied, message, _) = ApplyTwice(native, "kamidori", "data/three.toml", """
            { unit = { ["761"] = { experiance_reward = 1 }, ["0x2f9"] = { stats = { max_hp = 1 } } }, item = { ["5"] = { growth_level = 3 } } }
            """);
        Assert.False(applied);
        Assert.Equal("""
            example.data data/three.toml: 3 problems, so nothing of it is applied
              item.5.growth_level: item.growth_level is saved state: a load brings the saved value back, so a file does not set it
              unit.0x2f9: a record id is written in decimal: 761
              unit.761.experiance_reward: unit has no field experiance_reward
            """.ReplaceLineEndings("\n").TrimEnd('\n'), message);

        // Each file is checked by itself, and only once a session: 25 bad ids show 20 lines and a count.
        string text = Chunk(native, "kamidori-base", """
            require("tables")
            local units = {}
            for id = 1000, 1024 do units[tostring(id)] = { experience_reward = 1 } end
            local ok, message = pcall(apply_boot, nil, { mod = "example.data", file = "data/many.toml", data = { unit = units } })
            return message
            """);
        string[] lines = text.Split('\n');
        Assert.Equal("example.data data/many.toml: 25 problems, so nothing of it is applied", lines[0]);
        Assert.Equal(22, lines.Length);
        Assert.Equal("  unit.1000: unit has records 0 to 999", lines[1]);
        Assert.Equal("  and 5 more", lines[^1]);
        Assert.Empty(native.Memory.Integers);
    }

    // ------------------------------------------------------------------------------------------------- the library

    /// <summary>A base mod with each kind of field: a 1-D integer, a row with a named and an unnamed column and a
    /// column named like a Lua keyword, a string, a float, state, and a table another part of the game builds.</summary>
    private static readonly GameLayout HeroLayout = GameLayout.Parse("""
        {"layout": 1, "profile": "test", "globals": [
        {"name": "hero_levels", "bank": "G", "address": "0x100", "dims": [4]},
        {"name": "hero_stats", "bank": "G", "address": "0x200", "dims": [4, 3], "columns": {"hp": 0, "end": 2}},
        {"name": "hero_names", "bank": "GS", "address": "0x10", "dims": [4]},
        {"name": "hero_speeds", "bank": "GF", "address": "0x20", "dims": [4]},
        {"name": "hero_seen", "bank": "G", "address": "0x40", "dims": [4]},
        {"name": "wave_sizes", "bank": "G", "address": "0x300", "dims": [2]}
        ]}
        """);

    private const string HeroMap = """
        return { format = 1, tables = {
            hero = { records = 4, built_by = "boot", fields = {
                level = { global = "hero_levels", kind = "integer" },
                stats = { global = "hero_stats", kind = "integer", width = 3, columns = { hp = 0, ["end"] = 2 } },
                name = { global = "hero_names", kind = "string" },
                speed = { global = "hero_speeds", kind = "float" },
                seen = { global = "hero_seen", kind = "integer", state = true },
            } },
            wave = { records = 2, built_by = "stage", fields = { size = { global = "wave_sizes", kind = "integer" } } },
        } }
        """;

    private Scripting Library(string documents)
    {
        string records = File.ReadAllText(Path.Combine(Paths.Repo, "mods", "ome-records", "lua", "init.lua"));
        var (stack, plan) = new LuaStackBuilder()
            .Mod("ome-records", "-- file: init.lua\n" + records, vm: false, library: true)
            .Mod("docs", "-- file: init.lua\n" + documents, vm: false, library: true)
            .Base("test-base", HeroLayout, "-- file: init.lua\nreturn {}\n-- file: generated/tables.lua\n" + HeroMap,
                  "ome-records", "docs")
            .Build();
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup { Runtime = _runtime, Stack = stack, Plan = plan });
        _owned.Add(native);
        Assert.Null(native.StartSession(SessionReason.Boot));
        return native;
    }

    [Fact]
    public void TheLibraryTakesADocumentAsAReadOnlyViewAndWritesEachKindOfCell()
    {
        // Milestone 1 hands the applier a read-only view (spec §5.3): here another mod's export stands in for it.
        Scripting native = Library("""
            return {
                good = { hero = { ["1"] = { level = 7, stats = { hp = 30, ["1"] = 4, ["end"] = 9 }, name = "リリィ", speed = 0.5 },
                                  ["3"] = { level = -2 } } },
                row = { hero = { ["2"] = { stats = { 1, 2, 3 } } } },
                stage = { wave = { ["1"] = { size = 3 } } },
            }
            """);
        Assert.Equal("userdata\t7\ttrue\tfalse\tfalse\t0", Chunk(native, "test-base", """
            local records = require("ome-records")
            local docs = require("docs")
            local applier = records.applier { map = require("generated.tables"), table = maid.names.table, labels = { "boot" } }
            local good = { mod = "docs", file = "data/good.toml", data = docs.good }
            local written = applier:apply(good, { built_by = "boot" })
            local stage = { mod = "docs", file = "data/stage.toml", data = docs.stage }
            local staged = applier:check(stage)
            local ok = pcall(applier.apply, applier, good, { built_by = "stage" })
            return type(docs.good), written, applier:check(good), staged, ok, applier:apply(good, { built_by = "boot" }) - 7
            """));
        ScriptMemory memory = native.Memory;
        Assert.Equal((7, -2), (memory.Integer(0x101), memory.Integer(0x103)));
        Assert.Equal((30, 4, 9), (memory.Integer(0x203), memory.Integer(0x204), memory.Integer(0x205)));
        Assert.Equal("リリィ", memory.String(0x11));
        Assert.Equal(0.5f, BitConverter.Int32BitsToSingle(unchecked((int)memory.Float(0x21))));

        string row = Chunk(native, "test-base", """
            local applier = require("ome-records").applier { map = require("generated.tables"), table = maid.names.table, labels = { "boot" } }
            local fits, message = applier:check({ mod = "docs", file = "data/row.toml", data = require("docs").row })
            local _, unknown = pcall(applier.apply, applier, { mod = "docs", file = "data/row.toml", data = {} }, { built_by = "stage" })
            return tostring(fits), message, unknown
            """);
        string[] parts = row.Split('\t');
        Assert.Equal(3, parts.Length);
        Assert.Equal("false", parts[0]);
        Assert.Equal("docs data/row.toml: 1 problem, so nothing of it is applied\n"
                     + "  hero.2.stats: hero.stats takes its columns by name, or by number where a column has no name, not a whole row as an array",
                     parts[1]);
        Assert.EndsWith("apply: options.built_by is one of the applier's labels", parts[2]);
        Assert.Equal(0, memory.Integer(0x200 + 2 * 3));
    }

    [Fact]
    public void TheLibraryRefusesAStateFieldAColumnByNumberThatHasANameAndATableOfAnotherLabel()
    {
        Scripting native = Library("return { bad = { hero = { [\"0\"] = { seen = 1, stats = { [\"0\"] = 5, [\"2\"] = 6 } } }, wave = { [\"1\"] = { size = 2 } } } }");
        string text = Chunk(native, "test-base", """
            local applier = require("ome-records").applier { map = require("generated.tables"), table = maid.names.table, labels = { "boot" } }
            local _, message = applier:check({ mod = "docs", file = "data/bad.toml", data = require("docs").bad })
            return message
            """);
        Assert.Equal("""
            docs data/bad.toml: 4 problems, so nothing of it is applied
              hero.0.seen: hero.seen is saved state: a load brings the saved value back, so a file does not set it
              hero.0.stats.0: column 0 is named: write hero.0.stats.hp
              hero.0.stats.2: column 2 is named: write hero.0.stats.end
              wave: wave is built by stage, which this format does not edit
            """.ReplaceLineEndings("\n").TrimEnd('\n'), text);
        Assert.Empty(native.Memory.Integers);
    }
}
