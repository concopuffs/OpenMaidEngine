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
/// that a file may set (300,000 values); the bound file adds unit records up to the 400,000 values a document may
/// hold; the refused file is the bound file with every value of the wrong kind; and a file of 1,000 values, the size a
/// balance mod writes, for what an ordinary file costs. Since milestone 3 the shipped base mod's shape refuses the
/// refused file at the link, which is timed, and an owner without a shape (the base mod copied without it) meets it at
/// the first delivery, where it is measured as before (plan §9.2). The measurement gives each file's
/// instructions for each value, which the allowance must cover twice over, and the time the item file adds to a boot
/// and to a load, against the plan's ceiling of 10 seconds. The budget test runs the bound files with the engine's
/// own limits.
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
    /// game's own values (<paramref name="refused"/>: each of the wrong kind), until it holds <paramref name="limit"/>
    /// values or the tables end.
    /// </summary>
    private static (string Text, int Values) TablesFile(IEnumerable<(string Table, int Records, List<RecordField> Fields)> tables,
                                                        GameSession game, int limit, bool refused)
    {
        var text = new StringBuilder();
        int values = 0;
        foreach (var (table, records, fields) in tables)
            for (int record = 0; record < records && values < limit; record++)
            {
                text.Append('[').Append(table).Append('.').Append(record.ToString(CultureInfo.InvariantCulture)).Append("]\n");
                foreach (RecordField field in fields.Where(field => !field.State))
                {
                    string[] keys = field.Columns.Length == 0 ? [field.Name] : field.Columns.Select(column => field.Name + "." + column).ToArray();
                    for (int column = 0; column < keys.Length && values < limit; column++, values++)
                    {
                        int cell = field.Columns.Length == 0 ? field.Global.Address + record
                                                             : field.Global.Address + record * field.Global.RowWidth + column;
                        bool isString = field.Global.Kind == CellKind.String;
                        string value = refused
                            ? (isString ? "0" : "\"x\"")
                            : isString ? TomlString(game.GlobalStrings.GetValueOrDefault(cell) ?? "")
                                       : game.Globals.GetValueOrDefault(cell).ToString(CultureInfo.InvariantCulture);
                        text.Append(keys[column]).Append(" = ").Append(value).Append('\n');
                    }
                }
            }
        return (text.ToString(), values);
    }

    /// <summary>The three files of plan §4.3, from the installed Kamidori, after a file of the size a balance mod
    /// writes.</summary>
    private (string Name, string Text, int Values)[] SizeFiles(ModLinkBase linkBase, GameSession game)
    {
        List<RecordField> items = RecordFields(linkBase, "item"), units = RecordFields(linkBase, "unit");
        var (balanceText, balanceValues) = TablesFile([("item", 2000, items)], game, 1_000, refused: false);
        var (itemText, itemValues) = TablesFile([("item", 2000, items)], game, int.MaxValue, refused: false);
        var (boundText, boundValues) = TablesFile([("item", 2000, items), ("unit", 1000, units)], game, ValueBound, refused: false);
        var (refusedText, refusedValues) = TablesFile([("item", 2000, items), ("unit", 1000, units)], game, ValueBound, refused: true);
        Assert.Equal(300_000, itemValues);
        Assert.Equal(ValueBound, boundValues);
        Assert.Equal(ValueBound, refusedValues);
        return [("balance", balanceText, balanceValues), ("items", itemText, itemValues), ("bound", boundText, boundValues),
                ("refused", refusedText, refusedValues)];
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

    /// <summary>The refused file against the shipped base mod: its shape refuses every value at the link, the first 20
    /// with their lines and the rest counted, and no delivery is planned. The time the link takes.</summary>
    private TimeSpan RefusedAtTheLink(ModLinkBase linkBase, ModPackage refused)
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
        Assert.StartsWith("data/refused.toml:2: item.0.", stops[0].Message);
        Assert.Equal($"data/refused.toml: and {ValueBound - 20} more problems", stops[^1].Message);
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

        // Measured with an allowance no file reaches, so that each delivery runs to its end.
        ModLimits measuring = ModLimits.Default with { DataInstructionsPerValue = 1_000_000 };
        var perValue = new Dictionary<string, double>();
        foreach (var (name, text, values) in SizeFiles(linkBase, game))
        {
            ModPackage contributor = DataMod("kamidori", "test.size-" + name, name + ".toml", text);
            string refusedLine = "";
            if (name == "refused")
            {
                // Plan §9.2: the shipped base mod's shape refuses the file at the link, before any delivery; timed.
                TimeSpan refusedLink = RefusedAtTheLink(linkBase, contributor);
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
            perValue[name] = (double)instructions / values;
            string line = $"{name}: {refusedLine}{values} values, {Encoding.UTF8.GetByteCount(text)} bytes, {instructions} instructions "
                          + $"({perValue[name]:F1} a value), {delivery.GetProperty("result").GetString()}; link {link.TotalSeconds:F2} s, "
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
        Assert.Equal(4, perValue.Count);
        double largest = perValue.Values.Max();
        _output?.WriteLine($"largest {largest:F1} instructions a value; allowance {ModLimits.Default.DataInstructionsPerValue}");
        Assert.True(2 * largest <= ModLimits.Default.DataInstructionsPerValue,
                    $"the allowance {ModLimits.Default.DataInstructionsPerValue} does not cover twice the largest cost, {largest:F1} a value");
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
        foreach (var (name, text, _) in SizeFiles(linkBase, game).Where(file => file.Name is "bound" or "refused"))
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
                Assert.Contains($"test.size-refused data/refused.toml: {ValueBound} problems, so nothing of it is applied",
                                delivery.GetProperty("error").GetString(), StringComparison.Ordinal);
            }
        }
    }
}
