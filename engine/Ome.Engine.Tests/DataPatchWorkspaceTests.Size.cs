using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
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

/// <summary>
/// Gate 10 of docs/superpowers/specs/2026-11-17-data-patches-design.md, on the installed Kamidori (data patches plan
/// §4.3): three files built from the record map and the game's own values, each shipped by a mod and delivered while
/// the installed INIT2 runs, at boot and at a load. The full item file sets every field of the game's largest table
/// that a file may set (300,000 values); the bound file adds unit records up to the size a document may have, 400,000
/// with each table counting as each value does (plan §12.6); the refused file is the bound file with every value of the
/// wrong kind; and a file of size 1,000, what a balance mod writes, for what an ordinary file costs. Since milestone 3
/// the shipped base mod's shape refuses the refused file at the link, which is timed, and an owner without a shape (the
/// base mod copied without it) meets it at the first delivery, where it is measured as before (plan §9.2); a file at
/// the bound whose every key is unknown, the costliest problem the shape gives, is timed at the link too (§12.5). The
/// measurement gives each file's instructions for each unit of its size, which the allowance must cover twice over,
/// and the time the item file adds to a boot and to a load, against the plan's ceiling of 10 seconds. The budget test
/// runs the bound files with the engine's own limits.
/// </summary>
public sealed partial class DataPatchWorkspaceTests
{
    /// <summary>The ceiling of plan §4.3, named before measuring: what the full item file may add to a boot or a load.</summary>
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(10);

    private const int ValueBound = 400_000;

    /// <summary>A field of a record table as the record map gives it: its columns in order, a number where a column
    /// has no name, and none for a field with one cell for each record.</summary>
    private sealed record RecordField(string Name, LayoutGlobal Global, bool State, string[] Columns);

    /// <summary>kamidori-base's record map, read in its own Lua: each field of <paramref name="table"/> by name.</summary>
    private List<RecordField> RecordFields(ModLinkBase linkBase, string table)
    {
        ModPackage baseMod = ShippedPackages.Open("kamidori-base")!;
        _opened.Add(baseMod);
        IReadOnlyList<ModPackage> required = ShippedPackages.Required([baseMod], new ModLinkReport());
        _opened.AddRange(required);
        ModLinkResult linked = ModLinker.Link(linkBase with { Lua = _runtime }, [baseMod, .. required], []);
        Assert.True(linked.Succeeded, linked.Report.Render());
        Scripting native = Native(linked, LuaErrorPolicy.Halt);
        Assert.Null(native.StartSession(SessionReason.Boot));
        ConsoleRequest request = native.SubmitConsole("kamidori-base", $$"""
            local spec = require("generated.tables").tables["{{table}}"]
            local names, lines = {}, {}
            for name in pairs(spec.fields) do names[#names + 1] = name end
            table.sort(names)
            for _, name in ipairs(names) do
                local field, columns = spec.fields[name], {}
                if field.width then
                    local named = {}
                    for column, index in pairs(field.columns or {}) do named[index] = column end
                    for index = 0, field.width - 1 do columns[#columns + 1] = named[index] or tostring(index) end
                end
                lines[#lines + 1] = name .. "\t" .. field.global .. "\t" .. tostring(field.state == true) .. "\t" .. table.concat(columns, ",")
            end
            return table.concat(lines, "\n")
            """);
        native.ServiceRequests();
        Assert.True(request.Done && request.Result!.Succeeded, request.Result?.Text);
        GameLayout layout = baseMod.BaseLayout!;
        return request.Result!.Text.Split('\n').Select(line =>
        {
            string[] parts = line.Split('\t');
            Assert.True(layout.TryGet(parts[1], out LayoutGlobal global));
            return new RecordField(parts[0], global, parts[2] == "true", parts[3].Length == 0 ? [] : parts[3].Split(','));
        }).ToList();
    }

    private static string TomlString(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (char c in text)
        {
            if (c == '"' || c == '\\') builder.Append('\\').Append(c);
            else if (c < 0x20 || c == 0x7f) builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else builder.Append(c);
        }
        return builder.Append('"').ToString();
    }

    /// <summary>
    /// A <c>kamidori.tables</c> file that sets every cell of each field a file may set, record by record, with the
    /// game's own values (<paramref name="refused"/>: each of the wrong kind), until its size, which counts each table
    /// as each value (plan §12.6), is <paramref name="limit"/> or the tables end. <c>Values</c> counts the cells.
    /// </summary>
    private static (string Text, int Values, int Size) TablesFile(IEnumerable<(string Table, int Records, List<RecordField> Fields)> tables,
                                                                  GameSession game, int limit, bool refused)
    {
        var text = new StringBuilder();
        int values = 0, size = 0;
        foreach (var (table, records, fields) in tables)
        {
            // The table's own, then each record's, then each row's, the first time a key passes through it.
            int tableSize = 1;
            for (int record = 0; record < records && size + tableSize + 1 <= limit; record++)
            {
                text.Append('[').Append(table).Append('.').Append(record.ToString(CultureInfo.InvariantCulture)).Append("]\n");
                size += tableSize + 1;
                tableSize = 0;
                foreach (RecordField field in fields.Where(field => !field.State))
                {
                    string[] keys = field.Columns.Length == 0 ? [field.Name] : field.Columns.Select(column => field.Name + "." + column).ToArray();
                    for (int column = 0; column < keys.Length; column++)
                    {
                        int cost = field.Columns.Length > 0 && column == 0 ? 2 : 1;
                        if (size + cost > limit) break;
                        int cell = field.Columns.Length == 0 ? field.Global.Address + record
                                                             : field.Global.Address + record * field.Global.RowWidth + column;
                        bool isString = field.Global.Kind == CellKind.String;
                        string value = refused
                            ? (isString ? "0" : "\"x\"")
                            : isString ? TomlString(game.GlobalStrings.GetValueOrDefault(cell) ?? "")
                                       : game.Globals.GetValueOrDefault(cell).ToString(CultureInfo.InvariantCulture);
                        text.Append(keys[column]).Append(" = ").Append(value).Append('\n');
                        size += cost;
                        values++;
                    }
                }
            }
        }
        return (text.ToString(), values, size);
    }

    /// <summary>The three files of plan §4.3, from the installed Kamidori, after a file of the size a balance mod
    /// writes.</summary>
    private (string Name, string Text, int Values, int Size)[] SizeFiles(ModLinkBase linkBase, GameSession game)
    {
        List<RecordField> items = RecordFields(linkBase, "item"), units = RecordFields(linkBase, "unit");
        var balance = TablesFile([("item", 2000, items)], game, 1_000, refused: false);
        var item = TablesFile([("item", 2000, items)], game, int.MaxValue, refused: false);
        var bound = TablesFile([("item", 2000, items), ("unit", 1000, units)], game, ValueBound, refused: false);
        var refused = TablesFile([("item", 2000, items), ("unit", 1000, units)], game, ValueBound, refused: true);
        Assert.Equal(300_000, item.Values);
        Assert.Equal(ValueBound, bound.Size);
        Assert.Equal(ValueBound, refused.Size);
        _output?.WriteLine($"sizes: balance {balance.Size} ({balance.Values} cells), items {item.Size} ({item.Values} cells), "
                           + $"bound {bound.Size} ({bound.Values} cells)");
        return [("balance", balance.Text, balance.Values, balance.Size), ("items", item.Text, item.Values, item.Size),
                ("bound", bound.Text, bound.Values, bound.Size), ("refused", refused.Text, refused.Values, refused.Size)];
    }

    /// <summary>kamidori-base as the repository has it, without its shape: an owner without a shape, which checks a file
    /// only at its first delivery (data patches plan §9.2). The refused file reaches its check this way.</summary>
    private ModPackage ShapelessKamidoriBase()
    {
        string from = Path.Combine(Paths.Repo, "mods", "kamidori-base"), to = Path.Combine(_root, "kamidori-base-shapeless");
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);
            if (relative.StartsWith("shapes", StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(to, relative))!);
            File.Copy(file, Path.Combine(to, relative));
        }
        string manifest = Path.Combine(to, "manifest.toml");
        string text = File.ReadAllText(manifest);
        Assert.Contains("shape = \"tables.schema.json\"\n", text);
        File.WriteAllText(manifest, text.Replace("shape = \"tables.schema.json\"\n", ""));
        ModPackage mod = ModPackage.Open(to);
        _opened.Add(mod);
        return mod;
    }

    /// <summary>A file at the size bound whose every key is one the unit record does not take (data patches plan
    /// §12.5): 1,000 records of keys of 30 characters, 400 to a record until the file's size, its tables counted, is the
    /// bound. Its problem is the costliest the shape gives, since the reason looks for the nearest names among the
    /// record's, and the check builds it only for the 20 it keeps. <c>Keys</c> counts the problems.</summary>
    private static (string Text, int Keys) UnknownKeysFile()
    {
        var text = new StringBuilder();
        int size = 1, keys = 0;
        for (int record = 0; record < 1000 && size < ValueBound; record++)
        {
            text.Append("[unit.").Append(record.ToString(CultureInfo.InvariantCulture)).Append("]\n");
            size++;
            for (int key = 0; key < 400 && size < ValueBound; key++, size++, keys++)
                text.Append("zz_unknown_field_name_").Append(record.ToString("D4", CultureInfo.InvariantCulture))
                    .Append(key.ToString("D4", CultureInfo.InvariantCulture)).Append(" = 1\n");
        }
        Assert.Equal(ValueBound, size);
        return (text.ToString(), keys);
    }

    /// <summary>A file at the size bound against the shipped base mod, whose shape refuses each of its
    /// <paramref name="problems"/> values at the link: the first 20 with their lines (the first starting
    /// <paramref name="first"/>) and the rest counted, and no delivery is planned. The time the link takes.</summary>
    private TimeSpan RefusedAtTheLink(ModLinkBase linkBase, ModPackage refused, string name, string first, int problems)
    {
        IReadOnlyList<ModPackage> shipped = ShippedPackages.Required([refused], new ModLinkReport());
        _opened.AddRange(shipped);
        var watch = Stopwatch.StartNew();
        ModLinkResult linked = ModLinker.Link(linkBase with { Lua = _runtime }, [refused, .. shipped], [refused.Id]);
        watch.Stop();
        Assert.False(linked.Succeeded);
        var stops = linked.Report.Lines.Where(line => line.Stops).ToList();
        Assert.Equal(21, stops.Count);
        Assert.All(stops, line => Assert.Equal([refused.Id], line.Mods));
        Assert.StartsWith($"data/{name}.toml:2: {first}", stops[0].Message);
        Assert.Equal($"data/{name}.toml: and {problems - 20} more problems", stops[^1].Message);
        Assert.Empty(linked.Lua.Stack.ContributionList);
        return watch.Elapsed;
    }

    /// <summary>The delivery's trace line of the file <c>data/NAME.toml</c>.</summary>
    private static JsonElement Delivery(Scripting native, string name)
        => JsonDocument.Parse(Assert.Single(native.RecentTrace,
               line => line.Contains($"\"file\":\"data/{name}.toml\"", StringComparison.Ordinal))).RootElement;

    // ------------------------------------------------------------------------------------------------- a load

    private static readonly Lazy<NativeSaveIdentity> KamidoriIdentity = new(() =>
    {
        var manifest = Ome.Engine.Profiles.GameProfileRegistry.BuiltIn.Find("kamidori")!;
        return manifest.Persistence.CreateExpectedNativeIdentity(EngineBuilds.ForProfile(manifest).Rules.Saves);
    });

    private static INativeDatStore KamidoriStore(string folder)
        => new SidecarNativeDatStore(new DirectoryNativeDatStore(folder, KamidoriIdentity.Value), folder,
                                     Scripting.StateFileSuffix, currentSidecar: null);

    /// <summary>A synthetic root over the installed Kamidori, as the Himegari load test builds it: the prologue calls
    /// INIT2, op 0xae ends a load's prologue, then a save to slot 1 and the end. And the loader of that slot.</summary>
    private static (WithRoot Provider, Script Root, Script Loader) KamidoriRoot(Sys4ScriptProvider scripts)
    {
        int callScript = KamidoriTable.ByLabel("call-script")!.Value;
        uint init2 = scripts.RequireByName("INIT2.BIN").PackedId;
        Script root = WithId(ScriptAssembler.Assemble(KamidoriTable, "ROOT.BIN", new List<(int, Operand[])>
        {
            (callScript, [new(T_IMM, init2)]),
            (0xae, []),
            (0x1ad, []),
            (0x19e, [new(T_GINT, 0x20), new(T_IMM, 1)]),
            (0x2, []),
        }, []), RootId);
        Script loader = ScriptAssembler.Assemble(KamidoriTable, "LOADER.BIN", new List<(int, Operand[])>
        {
            (0x1a1, [new(T_LINT, 0), new(T_IMM, 1)]),
            (0x2, []),
        }, []);
        Script callback = ScriptAssembler.Assemble(KamidoriTable, "CALLBACK_LOAD.BIN", new List<(int, Operand[])> { (0x2, []) }, []);
        return (new WithRoot(scripts, root, callback), root, loader);
    }

    private static TimeSpan RunKamidori(Script start, WithRoot provider, string saves, Scripting? native)
    {
        Directory.CreateDirectory(saves);
        var vm = new VirtualMachine(start, KamidoriTable, new HeadlessWaits(), provider: provider,
                                    saves: new SavesAndSettings(store: KamidoriStore(saves)), build: EngineBuilds.Age433G,
                                    scripting: native);
        var watch = Stopwatch.StartNew();
        vm.Run();
        watch.Stop();
        Assert.Equal("exit", vm.HaltReason);
        return watch.Elapsed;
    }

    // ------------------------------------------------------------------------------------------------- gate 10

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void TheLargestFilesCostLessThanHalfTheAllowanceAndTheItemFileStaysUnderTheCeiling()
    {
        var (linkBase, scripts) = Kamidori(translated: false);
        KamidoriBoot(scripts, null);   // the first run takes the JIT's time, which no later run pays
        var watch = Stopwatch.StartNew();
        GameSession game = KamidoriBoot(scripts, null);
        TimeSpan plainBoot = watch.Elapsed;
        var (provider, root, loader) = KamidoriRoot(scripts);
        string saved = Path.Combine(_root, "saves-size");
        RunKamidori(root, provider, saved, null);
        string plainLoadSaves = Path.Combine(_root, "saves-size-plain");
        CopyFolder(saved, plainLoadSaves);
        TimeSpan plainLoad = RunKamidori(loader, provider, plainLoadSaves, null);

        // Measured with an allowance no file reaches, so that each delivery runs to its end. The allowance is for each
        // unit of a document's size, which counts its tables as its values (plan §12.6), so the cost is measured so.
        ModLimits measuring = ModLimits.Default with { DataInstructionsPerValue = 1_000_000 };
        var perValue = new Dictionary<string, double>();
        foreach (var (name, text, values, size) in SizeFiles(linkBase, game))
        {
            ModPackage contributor = DataMod("kamidori", "test.size-" + name, name + ".toml", text);
            string refusedLine = "";
            if (name == "refused")
            {
                // Plan §9.2: the shipped base mod's shape refuses the file at the link, before any delivery; timed.
                TimeSpan refusedLink = RefusedAtTheLink(linkBase, contributor, name, "item.0.", values);
                refusedLine = $"refused at the link in {refusedLink.TotalSeconds:F2} s; ";
                Assert.True(refusedLink < Ceiling, refusedLine);
            }
            watch.Restart();
            // An owner without a shape meets the refused file at its first delivery (§10.1), measured as before.
            ModLinkResult linked = name == "refused" ? LinkedInOrder(linkBase, ShapelessKamidoriBase(), contributor)
                                                     : LinkedInOrder(linkBase, contributor);
            TimeSpan link = watch.Elapsed;
            var native = new Scripting(new ScriptMemory(), new ScriptingSetup
            {
                Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Continue,
                Limits = measuring, Trace = true,
            });
            _owned.Add(native);
            watch.Restart();
            KamidoriBoot(scripts, native);
            TimeSpan boot = watch.Elapsed;
            JsonElement delivery = Delivery(native, name);
            long instructions = delivery.GetProperty("instructions").GetInt64();
            perValue[name] = (double)instructions / size;
            string line = $"{name}: {refusedLine}size {size} ({values} cells), {Encoding.UTF8.GetByteCount(text)} bytes, "
                          + $"{instructions} instructions ({perValue[name]:F1} a unit of size, {(double)instructions / values:F1} a cell), "
                          + $"{delivery.GetProperty("result").GetString()}; link {link.TotalSeconds:F2} s, "
                          + $"boot {boot.TotalSeconds:F2} s against {plainBoot.TotalSeconds:F2} s with no mod";
            if (name == "items")
            {
                Assert.Equal("ok", delivery.GetProperty("result").GetString());
                // At a load the link is done; the session is new, so the view, the check and the writes come again.
                string loadSaves = Path.Combine(_root, "saves-size-" + name);
                CopyFolder(saved, loadSaves);
                var loading = new Scripting(new ScriptMemory(), new ScriptingSetup
                {
                    Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt,
                    Limits = measuring, Trace = true,
                });
                _owned.Add(loading);
                TimeSpan load = RunKamidori(loader, provider, loadSaves, loading);
                Assert.Equal("ok", Delivery(loading, name).GetProperty("result").GetString());
                line += $"; load {load.TotalSeconds:F2} s against {plainLoad.TotalSeconds:F2} s with no mod";
                Assert.True(link + boot - plainBoot < Ceiling, line);
                Assert.True(load - plainLoad < Ceiling, line);
            }
            _output?.WriteLine(line);
        }

        // Plan §12.5: every key unknown, at the size bound, is refused at the link as fast as the refused file; its
        // reasons, the nearest names, are built for the 20 problems kept only.
        var (unknownText, unknownKeys) = UnknownKeysFile();
        ModPackage unknown = DataMod("kamidori", "test.size-unknown", "unknown.toml", unknownText);
        TimeSpan unknownLink = RefusedAtTheLink(linkBase, unknown, "unknown", "unit.0.zz_unknown_field_name_00000000: no such key",
                                                unknownKeys);
        string unknownLine = $"unknown: size {ValueBound} ({unknownKeys} keys), every key unknown; refused at the link in "
                             + $"{unknownLink.TotalSeconds:F2} s";
        _output?.WriteLine(unknownLine);
        Assert.True(unknownLink < Ceiling, unknownLine);

        Assert.Equal(4, perValue.Count);
        double largest = perValue.Values.Max();
        _output?.WriteLine($"largest {largest:F1} instructions a unit of size; allowance {ModLimits.Default.DataInstructionsPerValue}");
        Assert.True(2 * largest <= ModLimits.Default.DataInstructionsPerValue,
                    $"the allowance {ModLimits.Default.DataInstructionsPerValue} does not cover twice the largest cost, {largest:F1} a unit");
    }

    // ------------------------------------------------------------------------------------------- sparse files

    /// <summary>A value a cell of <paramref name="field"/> takes: 0, or a string the save can encode.</summary>
    private static string AnyValue(RecordField field) => field.Global.Kind == CellKind.String ? "\"x\"" : "0";

    /// <summary>
    /// A sparse <c>kamidori.tables</c> file over every record of <paramref name="tables"/> (plan §12.6): with
    /// <paramref name="mix"/> <c>rows</c>, each row field of each record an empty table; <c>row values</c>, each row field
    /// one value; <c>record values</c>, each record one value; <c>template</c>, each record one value and each row field
    /// empty. <paramref name="scope"/> names each record through the stage scope, at each stage from 1 to 999, as the
    /// stage's table must be. Its size counts each table as each value.
    /// </summary>
    private static (string Text, int Size) SparseFile(IEnumerable<(string Table, int Records, List<RecordField> Fields)> tables,
                                                      string mix, bool scope = false)
    {
        var text = new StringBuilder();
        int size = scope ? 1 : 0;
        foreach (var (table, records, fields) in tables)
        {
            var settable = fields.Where(field => !field.State).ToList();
            var rows = settable.Where(field => field.Columns.Length > 0).ToList();
            RecordField single = settable.First(field => field.Columns.Length == 0);
            foreach (int stage in scope ? Enumerable.Range(1, 999) : [0])
            {
                string prefix = scope ? $"stage.{stage}.enemy" : table;
                size += scope ? 2 : 1;
                for (int record = 0; record < records; record++)
                {
                    string id = $"{prefix}.{record.ToString(CultureInfo.InvariantCulture)}";
                    size++;
                    if (mix is "record values" or "template")
                    {
                        text.Append('[').Append(id).Append("]\n").Append(single.Name).Append(" = ").Append(AnyValue(single)).Append('\n');
                        size++;
                    }
                    if (mix == "record values") continue;
                    foreach (RecordField row in rows)
                    {
                        text.Append('[').Append(id).Append('.').Append(row.Name).Append("]\n");
                        size++;
                        if (mix != "row values") continue;
                        text.Append(row.Columns[0]).Append(" = ").Append(AnyValue(row)).Append('\n');
                        size++;
                    }
                }
            }
        }
        return (text.ToString(), size);
    }

    /// <summary>A scripting native that runs each delivery to its end, with the trace on.</summary>
    private Scripting Measuring(ModLinkResult linked)
    {
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = linked.Lua.Stack, Plan = linked.Lua.Plan, Errors = LuaErrorPolicy.Halt, Trace = true,
            Limits = ModLimits.Default with { DataInstructionsPerValue = 1_000_000 },
        });
        _owned.Add(native);
        return native;
    }

    /// <summary>The instructions of each delivery of the file <c>data/NAME.toml</c>.</summary>
    private static long[] Instructions(Scripting native, string name)
        => native.RecentTrace.Where(line => line.Contains($"\"file\":\"data/{name}.toml\"", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Select(line =>
            {
                Assert.Equal("ok", line.GetProperty("result").GetString());
                return line.GetProperty("instructions").GetInt64();
            }).ToArray();

    /// <summary>
    /// Plan §12.6: the files that cost most for their size are sparse ones, whose tables the base mod's applier walks
    /// with few values to write, and which cost far more than the allowance for each value while tables did not count.
    /// Each mix is delivered at boot, and a stage's mix at a stage's start too, where the first delivery of a session
    /// checks the whole document; each costs under half the allowance for each unit of its size.
    /// </summary>
    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void SparseFilesCostLessThanHalfTheAllowanceForTheirSizeAtBootAndAtAStage()
    {
        GateGame game = Game("kamidori");
        var boot = new[] { ("item", 2000), ("skill", 1000), ("unit", 1000) }
            .Select(table => (table.Item1, table.Item2, RecordFields(game.Base, table.Item1))).ToList();
        var stage = new[] { ("stage_enemy", 30, RecordFields(game.Base, "stage_enemy")) }.ToList();
        var files = new List<(string Name, string Text, int Size, bool Stage)>();
        foreach (string mix in new[] { "rows", "row values", "record values", "template" })
        {
            var (text, size) = SparseFile(boot, mix);
            files.Add(($"boot-{mix.Replace(' ', '-')}", text, size, false));
        }
        foreach (string mix in new[] { "rows", "row values", "record values" })
        {
            var (text, size) = SparseFile(stage, mix, scope: true);
            files.Add(($"stage-{mix.Replace(' ', '-')}", text, size, true));
        }
        double largest = 0;
        foreach (var (name, text, size, atStage) in files)
        {
            Assert.InRange(size, 1, ValueBound);
            ModLinkResult linked = LinkedInOrder(game.Base, DataMod("kamidori", "test.sparse-" + name, name + ".toml", text));
            Assert.Equal(size, linked.Lua.Stack.ContributionList.Single().Values);
            Scripting booted = Measuring(linked);
            game.Boot(booted);
            long[] costs = Instructions(booted, name);
            if (atStage)
            {
                GameSession session = game.Boot(Measuring(linked));
                session.Seed(Cell("kamidori", "current_stage_id", 0), 51);
                session.Seed(Cell("kamidori", "field_map_continue_mode", 0), 0);
                Scripting field = Measuring(linked);
                SceneResult run = session.RunScene(game.Scripts.RequireByName("FIELD.BIN"), game.Table, new HeadlessWaits(),
                                                   provider: game.Scripts, build: game.Build, scripting: field);
                Assert.Equal("STEP-LIMIT", run.Halt);
                long[] atField = Instructions(field, name);
                Assert.NotEmpty(atField);
                costs = [.. costs, .. atField];
            }
            double perUnit = (double)costs.Max() / size;
            largest = Math.Max(largest, perUnit);
            _output?.WriteLine($"{name}: size {size}, {Encoding.UTF8.GetByteCount(text)} bytes, deliveries {string.Join(", ", costs)} "
                               + $"instructions; {perUnit:F1} a unit of size");
        }
        _output?.WriteLine($"largest {largest:F1} instructions a unit of size; allowance {ModLimits.Default.DataInstructionsPerValue}");
        Assert.True(2 * largest <= ModLimits.Default.DataInstructionsPerValue,
                    $"the allowance {ModLimits.Default.DataInstructionsPerValue} does not cover twice the largest cost, {largest:F1} a unit");
    }

    [Fact]
    [Trait("Category", "Workspace")]
    [Trait("Profile", "kamidori")]
    public void AFileAtTheValueBoundIsAppliedInsideItsBudgetAtBootAndAtALoadAndARefusedOneFailsItsCheckNotItsBudget()
    {
        var (linkBase, scripts) = Kamidori(translated: false);
        GameSession game = KamidoriBoot(scripts, null);
        var (provider, root, loader) = KamidoriRoot(scripts);
        string saved = Path.Combine(_root, "saves-bound");
        RunKamidori(root, provider, saved, null);
        foreach (var (name, text, values, _) in SizeFiles(linkBase, game).Where(file => file.Name is "bound" or "refused"))
        {
            // The refused file fails the shipped base mod's shape at the link (the test above), so it reaches the
            // owner's check through an owner without a shape (plan §9.2).
            ModPackage contributor = DataMod("kamidori", "test.size-" + name, name + ".toml", text);
            ModLinkResult linked = name == "refused" ? LinkedInOrder(linkBase, ShapelessKamidoriBase(), contributor)
                                                     : LinkedInOrder(linkBase, contributor);
            Scripting booted = Native(linked, LuaErrorPolicy.Continue);
            KamidoriBoot(scripts, booted);
            string loadSaves = Path.Combine(_root, "saves-bound-" + name);
            CopyFolder(saved, loadSaves);
            Scripting loaded = Native(linked, LuaErrorPolicy.Continue);
            RunKamidori(loader, provider, loadSaves, loaded);
            foreach (Scripting native in new[] { booted, loaded })
            {
                JsonElement delivery = Delivery(native, name);
                Assert.Equal(ModLimits.Default.BudgetFor(linked.Lua.Stack.ContributionList.Single()), delivery.GetProperty("budget").GetInt64());
                if (name == "bound")
                {
                    Assert.Equal("ok", delivery.GetProperty("result").GetString());
                    continue;
                }
                Assert.Equal("error", delivery.GetProperty("result").GetString());
                Assert.Contains($"test.size-refused data/refused.toml: {values} problems, so nothing of it is applied",
                                delivery.GetProperty("error").GetString(), StringComparison.Ordinal);
            }
        }
    }
}
