using System.Globalization;
using System.Text;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Mods.Data;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Mods.Tools;
using Ome.Script.Patch;

/// <summary>
/// Milestone 3 of docs/superpowers/specs/2026-11-17-data-patches-design.md (§7, plan §6.2): each base mod's shape,
/// which <c>tools/base_mod_build.py</c> writes from the record map, against the applier that reads the same map. The
/// drift tests read the map through a console chunk, as the applier does, and build documents from it: every offered
/// field and column of each table's last record and of each scope's last id, each state field, and gate 7's mistakes.
/// Each document goes to the shape, as TOML, and to the applier, as a Lua table, and the two must agree on whether it
/// passes and at which paths it does not. Gate 12 runs through <c>omp check</c> and <c>omp build</c>.
/// </summary>
public sealed partial class DataPatchTests
{
    private sealed record MapField(string Table, string Name, string Kind, int Width, bool State,
                                   IReadOnlyDictionary<int, string> ColumnNames);

    private sealed record MapTable(string Name, int Records, IReadOnlyList<MapField> Fields)
    {
        public string Last => (Records - 1).ToString(CultureInfo.InvariantCulture);
    }

    private sealed record MapScope(string Name, int First, int Last, IReadOnlyDictionary<string, string> Tables);

    /// <summary>The record map as the base mod's own Lua reads it, one line for each table, field and scope.</summary>
    private const string DumpMap = """
        local map = require("generated.tables")
        local function sorted(t)
            local names = {}
            for name in pairs(t) do names[#names + 1] = name end
            table.sort(names)
            return names
        end
        local lines = {}
        for _, name in ipairs(sorted(map.tables)) do
            local spec = map.tables[name]
            lines[#lines + 1] = "table|" .. name .. "|" .. spec.records
            for _, field in ipairs(sorted(spec.fields)) do
                local f = spec.fields[field]
                local columns = {}
                for _, column in ipairs(sorted(f.columns or {})) do columns[#columns + 1] = column .. "=" .. f.columns[column] end
                lines[#lines + 1] = "field|" .. name .. "|" .. field .. "|" .. f.kind .. "|" .. (f.width or 0) .. "|"
                                    .. tostring(f.state == true) .. "|" .. table.concat(columns, ",")
            end
        end
        for _, name in ipairs(sorted(map.scopes or {})) do
            local scope = map.scopes[name]
            local shorts = {}
            for _, short in ipairs(sorted(scope.tables)) do shorts[#shorts + 1] = short .. "=" .. scope.tables[short] end
            lines[#lines + 1] = "scope|" .. name .. "|" .. scope.first .. "|" .. scope.last .. "|" .. table.concat(shorts, ",")
        end
        return table.concat(lines, "\n")
        """;

    private static (Dictionary<string, MapTable> Tables, List<MapScope> Scopes) RecordMap(Scripting native, string profile)
    {
        var fields = new Dictionary<string, List<MapField>>(StringComparer.Ordinal);
        var records = new Dictionary<string, int>(StringComparer.Ordinal);
        var scopes = new List<MapScope>();
        foreach (string line in Chunk(native, $"{profile}-base", DumpMap).Split('\n'))
        {
            string[] part = line.Split('|');
            switch (part[0])
            {
                case "table":
                    records[part[1]] = int.Parse(part[2], CultureInfo.InvariantCulture);
                    fields[part[1]] = [];
                    break;
                case "field":
                    var names = part[6].Length == 0 ? new Dictionary<int, string>()
                        : part[6].Split(',').Select(pair => pair.Split('='))
                                 .ToDictionary(pair => int.Parse(pair[1], CultureInfo.InvariantCulture), pair => pair[0]);
                    fields[part[1]].Add(new MapField(part[1], part[2], part[3], int.Parse(part[4], CultureInfo.InvariantCulture),
                                                     part[5] == "true", names));
                    break;
                case "scope":
                    scopes.Add(new MapScope(part[1], int.Parse(part[2], CultureInfo.InvariantCulture),
                                            int.Parse(part[3], CultureInfo.InvariantCulture),
                                            part[4].Split(',').Select(pair => pair.Split('='))
                                                   .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal)));
                    break;
            }
        }
        return (records.ToDictionary(pair => pair.Key, pair => new MapTable(pair.Key, pair.Value, fields[pair.Key]),
                                     StringComparer.Ordinal), scopes);
    }

    /// <summary>A document as a tree of keys, written out as TOML for the shape and as a Lua table for the applier.</summary>
    private sealed class Doc
    {
        private readonly SortedDictionary<string, object> _root = new(StringComparer.Ordinal);

        public Doc Set(object value, params string[] path)
        {
            SortedDictionary<string, object> node = _root;
            foreach (string key in path[..^1])
            {
                if (!node.TryGetValue(key, out object? next))
                    node[key] = next = new SortedDictionary<string, object>(StringComparer.Ordinal);
                node = (SortedDictionary<string, object>)next;
            }
            node[path[^1]] = value;
            return this;
        }

        public string Toml()
        {
            var text = new StringBuilder();
            void Walk(SortedDictionary<string, object> node, string prefix)
            {
                foreach (var (key, value) in node)
                {
                    string path = prefix + "\"" + key + "\"";
                    if (value is SortedDictionary<string, object> inner) Walk(inner, path + ".");
                    else text.Append(path).Append(" = ").Append(Literal(value, toml: true)).Append('\n');
                }
            }
            Walk(_root, "");
            return text.ToString();
        }

        public string Lua() => LuaTable(_root);

        private static string LuaTable(SortedDictionary<string, object> node)
            => "{ " + string.Join(", ", node.Select(pair => "[\"" + pair.Key + "\"] = "
                + (pair.Value is SortedDictionary<string, object> inner ? LuaTable(inner) : Literal(pair.Value, toml: false)))) + " }";

        private static string Literal(object value, bool toml) => value switch
        {
            long number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            object[] items => (toml ? "[" : "{ ") + string.Join(", ", items.Select(item => Literal(item, toml))) + (toml ? "]" : " }"),
            _ => throw new ArgumentException($"no literal for {value}"),
        };
    }

    private sealed record DriftCase(string What, Doc Document, string? Path, bool OwnerOnly = false);

    private static object CellValue(string kind) => kind switch { "integer" => 7L, "float" => 1.5, _ => "x" };

    /// <summary>The cases of the drift test, from the record map: what both take, what both refuse and where, and what
    /// only the owner refuses.</summary>
    private static List<DriftCase> DriftCases(Dictionary<string, MapTable> tables, List<MapScope> scopes)
    {
        var cases = new List<DriftCase>();
        var scoped = scopes.SelectMany(scope => scope.Tables.Select(pair => (Scope: scope, Short: pair.Key, Table: pair.Value)))
                           .ToDictionary(entry => entry.Table, StringComparer.Ordinal);
        string P(params string[] parts) => string.Join(".", parts);
        string[] Then(string[] path, params string[] more) => [.. path, .. more];
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        // Where each table's last record is reached: at the top, or through its scope's last id.
        string[] RecordAt(MapTable table) => scoped.TryGetValue(table.Name, out var at)
            ? [at.Scope.Name, Number(at.Scope.Last), at.Short, table.Last]
            : [table.Name, table.Last];

        foreach (MapTable table in tables.Values)
        {
            string[] record = RecordAt(table);
            var every = new Doc();
            foreach (MapField field in table.Fields.Where(field => !field.State))
            {
                if (field.Width == 0) every.Set(CellValue(field.Kind), Then(record, field.Name));
                else
                    for (int column = 0; column < field.Width; column++)
                        every.Set(CellValue(field.Kind), Then(record, field.Name,
                                  field.ColumnNames.TryGetValue(column, out string? name) ? name : Number(column)));
            }
            cases.Add(new DriftCase($"every offered field and column of {P(record)}", every, null));
            foreach (MapField field in table.Fields.Where(field => field.State))
                cases.Add(new DriftCase($"the state field {table.Name}.{field.Name}", new Doc().Set(1L, Then(record, field.Name)),
                                        P(Then(record, field.Name))));
        }

        // Gate 7's mistakes, on the first field of each sort in the tables at the top.
        var topFields = tables.Values.Where(table => !scoped.ContainsKey(table.Name)).SelectMany(table => table.Fields).ToList();
        MapField single = topFields.First(field => field is { Width: 0, Kind: "integer", State: false });
        MapField named = topFields.First(field => field is { Width: > 0, State: false } && field.ColumnNames.Count > 0);
        MapField? unnamed = topFields.FirstOrDefault(field => field is { Width: > 0, State: false } && field.ColumnNames.Count == 0);
        MapField text = topFields.First(field => field is { Width: 0, Kind: "string", State: false });
        MapTable singleTable = tables[single.Table];
        string[] value = Then(RecordAt(singleTable), single.Name);
        string[] row = Then(RecordAt(tables[named.Table]), named.Name);
        string[] textAt = Then(RecordAt(tables[text.Table]), text.Name);
        string namedIndex = Number(named.ColumnNames.Keys.Min());

        void Refused(string what, Doc document, params string[] path) => cases.Add(new DriftCase(what, document, P(path)));
        string[] misspelt = Then(RecordAt(singleTable), single.Name + "x");
        Refused("a misspelt field", new Doc().Set(1L, misspelt), misspelt);
        Refused("a named column by its number", new Doc().Set(1L, Then(row, namedIndex)), Then(row, namedIndex));
        Refused("a column the row lacks", new Doc().Set(1L, Then(row, "nosuch")), Then(row, "nosuch"));
        Refused("a row as an array", new Doc().Set(new object[] { 1L, 2L }, row), row);
        Refused("a column past a named row's width", new Doc().Set(1L, Then(row, Number(named.Width))), Then(row, Number(named.Width)));
        if (unnamed != null)
        {
            string[] numbered = Then(RecordAt(tables[unnamed.Table]), unnamed.Name);
            Refused("a column past a row's width", new Doc().Set(1L, Then(numbered, Number(unnamed.Width))),
                    Then(numbered, Number(unnamed.Width)));
            Refused("a row given one value", new Doc().Set(150L, numbered), numbered);
        }
        Refused("a string for a whole number", new Doc().Set("1", value), value);
        Refused("a fraction for a whole number", new Doc().Set(1.5, value), value);
        Refused("a whole number past 2^32 - 1", new Doc().Set(4294967296L, value), value);
        Refused("a whole number below -2^31", new Doc().Set(-2147483649L, value), value);
        Refused("a boolean for a whole number", new Doc().Set(true, value), value);
        Refused("an array for one value", new Doc().Set(new object[] { 999L }, value), value);
        Refused("a table for one value", new Doc().Set(1L, Then(value, "a")), value);
        foreach (string id in new[] { "0x10", "0" + singleTable.Last, "-1", Number(singleTable.Records) })
            Refused($"the record id {id}", new Doc().Set(1L, single.Table, id, single.Name), single.Table, id);
        Refused("a record given one value", new Doc().Set(5L, RecordAt(singleTable)), RecordAt(singleTable));
        Refused("a table given one value", new Doc().Set(5L, single.Table), single.Table);
        Refused("a number for text", new Doc().Set(5L, textAt), textAt);
        Refused("a table no map has", new Doc().Set(1L, "tablez", "3", "level"), "tablez");
        foreach (MapScope scope in scopes)
        {
            var (shortName, tableName) = scope.Tables.First();
            MapTable scopedTable = tables[tableName];
            string field = scopedTable.Fields.First(entry => !entry.State).Name;
            string first = Number(scope.First);
            Refused($"the scoped table {tableName} at the top", new Doc().Set(1L, tableName, "1", field), tableName);
            Refused($"{scope.Name} id {scope.First - 1}", new Doc().Set(1L, scope.Name, Number(scope.First - 1), shortName, "0", field),
                    scope.Name, Number(scope.First - 1));
            Refused($"{scope.Name} id {scope.Last + 1}", new Doc().Set(1L, scope.Name, Number(scope.Last + 1), shortName, "0", field),
                    scope.Name, Number(scope.Last + 1));
            Refused($"a short name {scope.Name} lacks", new Doc().Set(1L, scope.Name, first, shortName + "y", "0", field),
                    scope.Name, first, shortName + "y");
            Refused($"a record past {tableName}", new Doc().Set(1L, scope.Name, first, shortName, Number(scopedTable.Records), field),
                    scope.Name, first, shortName, Number(scopedTable.Records));
            Refused($"{scope.Name} given one value", new Doc().Set(5L, scope.Name), scope.Name);
            Refused($"a {scope.Name} id given one value", new Doc().Set(5L, scope.Name, first), scope.Name, first);
        }
        // What only the owner checks (plan §6.2): text the save's code page cannot encode.
        cases.Add(new DriftCase("text code page 932 cannot encode", new Doc().Set("Harpía", textAt), P(textAt), OwnerOnly: true));
        return cases;
    }

    /// <summary>The shipped base mod's shape, read: the link has no problem with it.</summary>
    private static DataShape ShippedShape(string profile)
    {
        using ModPackage baseMod = ShippedPackages.Open($"{profile}-base")!;
        Assert.Equal("tables.schema.json", Assert.Single(baseMod.Manifest.Accepts).Shape);
        DataShapeRead read = DataShape.Read(baseMod.ReadText(Assert.Single(baseMod.Layout.Shapes).File));
        Assert.Empty(read.Problems);
        return read.Shape!;
    }

    private static (string Path, string Reason) Split(string text)
    {
        int at = text.IndexOf(": ", StringComparison.Ordinal);
        return (text[..at], text[(at + 2)..]);
    }

    /// <summary>The applier's problems, from the message its check raises.</summary>
    private static List<(string Path, string Reason)> ApplierProblems(string message)
    {
        const string Lead = ", so nothing of it is applied: ";
        string[] lines = message.Split('\n');
        int at = lines[0].IndexOf(Lead, StringComparison.Ordinal);
        Assert.True(at >= 0, message);
        return [Split(lines[0][(at + Lead.Length)..]),
                .. lines.Skip(1).Select(line => line.Trim()).Where(line => !line.StartsWith("and ", StringComparison.Ordinal)).Select(Split)];
    }

    [Theory]
    [InlineData("himegari")]
    [InlineData("kamidori")]
    public void TheShapeAndTheApplierAgreeOnEachFieldEachStateFieldAndEachMistake(string profile)
    {
        DataShape shape = ShippedShape(profile);
        Scripting native = Started(profile);
        var (tables, scopes) = RecordMap(native, profile);
        Assert.NotEmpty(scopes);
        List<DriftCase> cases = DriftCases(tables, scopes);
        var disagreements = new List<string>();
        int number = 0;
        foreach (DriftCase drift in cases)
        {
            string file = $"data/case-{number++}.toml";
            DataReadResult read = DataDocumentReader.Read(drift.Document.Toml(), file);
            Assert.True(read.Document != null, $"{drift.What}: {string.Join("; ", read.Problems.Select(problem => problem.Text))}");
            DataShapeResult shaped = DataShapeCheck.Check(shape, read);
            Assert.Null(shaped.StepLimit);
            var shapePaths = shaped.Problems.Select(problem => Split(problem.Text)).ToList();

            string result = Chunk(native, $"{profile}-base", """
                require("tables")
                local ok, result = pcall(apply_boot, nil, { mod = "example.data", file = "FILE", data = DOCUMENT })
                return tostring(ok) .. "\n" .. tostring(result)
                """.Replace("FILE", file).Replace("DOCUMENT", drift.Document.Lua()));
            bool applied = result.StartsWith("true\n", StringComparison.Ordinal);
            var applierPaths = applied ? [] : ApplierProblems(result[(result.IndexOf('\n') + 1)..]);

            string Show() => $"{drift.What}: the shape says [{string.Join("; ", shaped.Problems.Select(problem => problem.Text))}], "
                             + $"the applier says [{(applied ? "applied" : result)}]";
            if (drift.Path == null)
            {
                if (!shaped.Ok || !applied) disagreements.Add(Show());
            }
            else if (drift.OwnerOnly)
            {
                if (!shaped.Ok || applierPaths.Select(problem => problem.Path).SingleOrDefault() != drift.Path)
                    disagreements.Add(Show());
            }
            else
            {
                bool same = shapePaths.Select(problem => problem.Path).SequenceEqual([drift.Path])
                            && applierPaths.Select(problem => problem.Path).SequenceEqual([drift.Path]);
                // A state field is called saved state by both, not an unknown field.
                if (drift.What.StartsWith("the state field", StringComparison.Ordinal))
                    same &= shapePaths.Concat(applierPaths).All(problem => problem.Reason.Contains("is saved state", StringComparison.Ordinal));
                if (!same) disagreements.Add(Show());
            }
        }
        Assert.True(disagreements.Count == 0, string.Join("\n", disagreements));
        Assert.Contains(cases, drift => drift.What.StartsWith("the state field", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------- gate 12

    /// <summary><see cref="LinkBase"/> with a synthetic script at each of the base mod's patch points, and the points
    /// bound there, so every event the base mod declares at a point is raised and a file of its kind is delivered.</summary>
    private ModLinkBase BoundBase(string profile)
    {
        ModLinkBase plain = LinkBase(profile);
        using ModPackage baseMod = ShippedPackages.Open($"{profile}-base")!;
        IReadOnlyCollection<OmsPatchPoint> copy = baseMod.BasePoints!.Points;
        var scripts = copy.Select(point => point.Script).Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(name => name, name => Encoding.ASCII.GetBytes("synthetic " + name),
                                        StringComparer.OrdinalIgnoreCase);
        var points = new OmsPatchPoints(copy.Select(point => point with
        {
            At = [new OmsPatchPointSite(OmsPatchPlan.FingerprintOf(scripts[point.Script]), point.At[0].Site)],
        }));
        return plain with
        {
            ReadScript = name => scripts.TryGetValue(name, out byte[]? bytes) ? bytes : plain.ReadScript(name),
            PatchPoints = points,
        };
    }

    private string Contributor(string profile, string text, string id = "example.shape")
    {
        string folder = Path.Combine(_root, id);
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), $"""
            omp = 4
            id = "{id}"
            name = "Shape"
            version = "1.0.0"

            [game]
            profile = "{profile}"
            target = "extended"

            [[requires]]
            id = "{profile}-base"
            version = ">=1.1, <2"

            [[data]]
            kind = "{profile}.tables"
            file = "a.toml"
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "data", "a.toml"), text, new UTF8Encoding(false));
        return folder;
    }

    public static TheoryData<string, string, string> ShapeErrors => new()
    {
        { "kamidori", "[unit.761]\nexperiance_reward = 999\n",
          "data/a.toml:2: unit.761.experiance_reward: no such key; the nearest name here is experience_reward" },
        { "kamidori", "[item.5]\ninventory_count = 9\n",
          "data/a.toml:2: item.5.inventory_count: item.inventory_count is saved state: a load brings the saved value back, so a file does not set it" },
        { "himegari", "[unit.5]\nexperiance_reward = 40\n",
          "data/a.toml:2: unit.5.experiance_reward: no such key; the nearest name here is experience_reward" },
        { "himegari", "[skill.3]\nacquired_flag = 1\n",
          "data/a.toml:2: skill.3.acquired_flag: skill.acquired_flag is saved state: a load brings the saved value back, so a file does not set it" },
        // A value of the wrong kind where a table goes says what to write, in the shape's words (plan §12.5).
        { "kamidori", "[unit.762]\nstats = [1, 2, 3]\n",
          "data/a.toml:2: unit.762.stats: takes a table, not an array (unit.stats: unit_base_stats, a row of 14 columns by name; write stats.<column> = ...)" },
        { "himegari", "unit.5 = 3\n",
          "data/a.toml:1: unit.5: takes a table, not 3 (unit: one record's fields, by name; write unit.<id>.<field> = ...)" },
        // The mistake twins' files before milestone 3 (plan §9.2): each mistake in a stage part fails the link now. A
        // name cut short has its nearest name (plan §12.5).
        { "kamidori", "[stage.51.enemy.1]\nunit_id = 761\n\n[stage_enemy.3]\nunit_id = 762\n",
          "data/a.toml:4: stage_enemy: stage_enemy is reached through its scope: write stage.<id>.enemy" },
        { "himegari", "[stage.1.enemy.2]\nunit_id = 810\n\n[stage.2.enemy.3]\nunit = 834\n",
          "data/a.toml:5: stage.2.enemy.3.unit: no such key; the nearest name here is unit_id" },
    };

    [Theory]
    [MemberData(nameof(ShapeErrors))]
    public void OmpCheckAndOmpBuildRefuseAFileThatFailsItsShapeWithTheFileTheLineThePathAndTheReason(string profile,
                                                                                                    string text, string error)
    {
        // Gate 12: the contributor's link error, before any code runs; omp build writes no package.
        string folder = Contributor(profile, text);
        var console = new StringWriter();
        Assert.Equal(1, OmpCommands.Check(BoundBase(profile), null, [folder], false, console));
        Assert.Contains($"error: [example.shape] {error}", console.ToString());
        Assert.Contains("omp check: does not link", console.ToString());

        string output = Path.Combine(_root, "out", "shape.omp");
        console = new StringWriter();
        Assert.Equal(1, OmpCommands.Build(BoundBase(profile), folder, output, [], console));
        Assert.Contains($"error: [example.shape] {error}", console.ToString());
        Assert.Contains("omp build: example.shape does not link", console.ToString());
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("kamidori", "[unit.761]\nexperience_reward = 999\nstats.max_hp = 150\n\n[stage.52.enemy.3]\nunit_id = 762\n")]
    [InlineData("himegari", "[unit.5]\nexperience_reward = 40\n\n[stage.2.enemy.3]\nunit_id = 834\n")]
    public void AFileThatPassesItsShapeChecksAndBuilds(string profile, string text)
    {
        string folder = Contributor(profile, text);
        var console = new StringWriter();
        Assert.True(OmpCommands.Check(BoundBase(profile), null, [folder], false, console) == 0, console.ToString());
        string output = Path.Combine(_root, "out", "shape.omp");
        console = new StringWriter();
        Assert.True(OmpCommands.Build(BoundBase(profile), folder, output, [], console) == 0, console.ToString());
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void OmpCheckRefusesAShapeWithAKeywordOutsideTheSubsetAsItsOwnersError()
    {
        string owner = Path.Combine(_root, "example.owner");
        foreach (var (path, text) in new Dictionary<string, string>
        {
            ["manifest.toml"] = """
                omp = 4
                id = "example.owner"
                name = "Owner"
                version = "1.0.0"

                [game]
                profile = "himegari"
                target = "extended"

                [lua]
                api = 1

                [[accepts]]
                kind = "example.lists"
                description = "Lists of names."
                shape = "lists.schema.json"

                [[accepts.at]]
                event = "example.owner.ready"
                handler = "lists.lua:apply"
                """,
            ["events/owner.toml"] = "[[event]]\nname = \"example.owner.ready\"\nraised_by = \"lua\"\n",
            ["lua/lists.lua"] = "error('never at link')\nfunction apply(event, contribution) end\n",
            ["shapes/lists.schema.json"] = """{ "type": "object", "required": ["names"] }""",
        })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(owner, path))!);
            File.WriteAllText(Path.Combine(owner, path), text, new UTF8Encoding(false));
        }
        string contributor = Path.Combine(_root, "example.lists");
        Directory.CreateDirectory(Path.Combine(contributor, "data"));
        File.WriteAllText(Path.Combine(contributor, "manifest.toml"), """
            omp = 4
            id = "example.lists"
            name = "Lists"
            version = "1.0.0"

            [game]
            profile = "himegari"
            target = "extended"

            [[requires]]
            id = "example.owner"
            version = ">=1"

            [[data]]
            kind = "example.lists"
            file = "a.toml"
            """);
        File.WriteAllText(Path.Combine(contributor, "data", "a.toml"), "names = []\n");
        var console = new StringWriter();
        Assert.Equal(1, OmpCommands.Check(LinkBase("himegari"), null, [owner, contributor], false, console));
        string report = console.ToString();
        Assert.Contains("error: [example.owner] accepts[0] example.lists: shapes/lists.schema.json: #/required: the keyword "
                        + "required is outside the shape subset", report);
        Assert.DoesNotContain("[example.lists]", report);
        Assert.DoesNotContain("never at link", report);
    }

    [Fact]
    public void EveryShippedDataExampleLinksAndPassesItsShape()
    {
        // Spec §12: every shipped example passes its kind's shape, the mistake twins too (their mistake is one only
        // the owner checks, plan §6.2).
        string examples = Path.Combine(Paths.Repo, "oms", "examples", "mods");
        int linked = 0;
        foreach (string folder in Directory.GetDirectories(examples).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(folder, "manifest.toml"))) continue;
            ModPackage mod = ModPackage.Open(folder);
            _opened.Add(mod);
            if (mod.Layout.Data.Count == 0) continue;
            string profile = mod.Manifest.Profile!;
            IReadOnlyList<ModPackage> required = ShippedPackages.Required([mod], new ModLinkReport());
            _opened.AddRange(required);
            ModLinkResult result = ModLinker.Link(BoundBase(profile), [mod, .. required], []);
            Assert.True(result.Succeeded, $"{mod.Id}: {result.Report.Render()}");
            Assert.Equal(mod.Layout.Data.Count, result.Lua.Stack.ContributionList.Count(found => found.Mod == mod.Id));
            linked++;
        }
        Assert.Equal(8, linked);
    }
}
