using System.Text.RegularExpressions;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Mods.Linking;
using Ome.Mods.Tools;
using Ome.Scripting.Lua;

/// <summary>
/// <c>omp lua-stubs</c> (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §13; milestone 3, piece 6): the
/// lua-language-server definitions for <c>maid</c>, the events and each base mod's names, checked against a synthetic
/// layout and against what the runtime itself gives a mod, so the definitions cannot drift from the API. The command
/// on the install is in <see cref="ModLuaLinkTests"/>.
/// </summary>
public sealed class LuaStubsTests : IDisposable
{
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
    }

    private static readonly GameLayout Layout = GameLayout.Parse("""
        {"layout": 1, "profile": "test", "globals": [
        {"name": "stage_turn_limit", "bank": "G", "address": "0x11", "dims": [], "aliases": ["turn_limit"]},
        {"name": "exp_rate", "bank": "GF", "address": "0x0", "dims": []},
        {"name": "unit_current_levels", "bank": "G", "address": "0x300", "dims": [4], "columns": {"hero": 0}},
        {"name": "unit_condition_levels", "bank": "G", "address": "0x400", "dims": [4, 3], "columns": {"charm": 1},
         "aliases": ["conditions"]},
        {"name": "unit_names", "bank": "GS", "address": "0x0", "dims": [null]}
        ]}
        """);

    private static GameEvent Award() => new("test.award", "test-base", "test.point", "One award.\nAfter the multipliers.",
    [
        GameEvent.BuildField("amount", "int", "rw", "L[0x1]", true, Layout),
        GameEvent.BuildField("unit", "int", "r", "stage_turn_limit", true, Layout),
    ]);

    private static GameEvent Computed() => new("test.computed", "test-base", null, null,
    [
        GameEvent.BuildField("ratio", "float", "rw", null, false, Layout),
        GameEvent.BuildField("crit", "bool", "r", null, false, Layout),
        GameEvent.BuildField("label", "string", "r", null, false, Layout),
    ]);

    /// <summary>A base mod's init.lua: three helpers on the table it returns, two without annotations (a plain comment is
    /// none); a local function and a function on another table, which are not helpers.</summary>
    private const string HelperInit = """
        local base = {}
        local other = {}

        local function whole(value) return value end

        ---Give an item.
        ---@param item integer an item id
        ---@return integer count
        function base.give_item( item, count )
            return whole(item)
        end

        function other.hidden() end

        function base.bare() end
        -- a plain comment is no annotation
        function base.after_comment(x) end

        return base
        """;

    private static IReadOnlyDictionary<string, string> Stubs()
        => LuaStubs.Files([new BaseModLayout("test-base", Layout)], [Computed(), Award()],
                          new Dictionary<string, string> { ["test-base"] = HelperInit });

    [Fact]
    public void ABaseModsHelpersAreTheFunctionsOnTheTableItsInitReturnsWithTheirAnnotations()
    {
        Assert.Equal(
        [
            "---Give an item.\n---@param item integer an item id\n---@return integer count\nfunction M.give_item(item, count) end\n",
            "function M.bare() end\n",
            "function M.after_comment(x) end\n",
        ], LuaStubs.Helpers(HelperInit.Replace("\n", "\r\n")));
        Assert.Empty(LuaStubs.Helpers("function M.f() end\n"));   // no return: nothing is known to be exported
        string text = Stubs()["test-base.lua"];
        Assert.EndsWith("-- The helpers test-base's lua/init.lua returns.\n\n---Give an item.\n---@param item integer an item id\n"
                        + "---@return integer count\nfunction M.give_item(item, count) end\n\nfunction M.bare() end\n\n"
                        + "function M.after_comment(x) end\n\nreturn M\n", text);
        // Without an init.lua the file has the accessors only.
        Assert.DoesNotContain("helpers", LuaStubs.Files([new BaseModLayout("test-base", Layout)], [])["test-base.lua"]);
    }

    private static string[] Lines(string text) => text.Split('\n');

    /// <summary>The fields a <c>---@class NAME</c> block declares, up to the next blank line or code.</summary>
    private static List<string> Fields(string text, string className)
    {
        string[] lines = Lines(text);
        int start = Array.IndexOf(lines, $"---@class {className}");
        Assert.True(start >= 0, $"no class {className}");
        var fields = new List<string>();
        for (int i = start + 1; i < lines.Length && lines[i].StartsWith("---@field ", StringComparison.Ordinal); i++)
            fields.Add(lines[i]["---@field ".Length..].Split(' ')[0].TrimEnd('?'));
        return fields;
    }

    [Fact]
    public void ABaseModsFileTypesEveryNameItsAliasesItsTablesAndTheirRows()
    {
        string text = Stubs()["test-base.lua"];
        Assert.StartsWith("---@meta test-base\n", text);
        Assert.Contains("---@field stage_turn_limit integer G[0x11]\n", text);
        Assert.Contains("---@field turn_limit integer G[0x11], the old name of stage_turn_limit\n", text);
        Assert.Contains("---@field exp_rate number GF[0x0]\n", text);
        Assert.Equal(["exp_rate", "stage_turn_limit", "turn_limit"], Fields(text, "test_base.g"));
        // A 1-D table: its elements and its named ones.
        Assert.Equal(["[integer]", "hero"], Fields(text, "test_base.unit_current_levels"));
        Assert.Contains("---unit_current_levels, from G[0x300]: 4 rows.\n", text);
        // A 2-D table: rows of named columns.
        Assert.Equal(["[integer]", "charm"], Fields(text, "test_base.unit_condition_levels.row"));
        Assert.Contains("---@field [integer] test_base.unit_condition_levels.row\n", text);
        // An open table with no global after it reaches the bank's end.
        Assert.Contains("---unit_names, from GS[0x0]: rows to the end of the bank.\n", text);
        Assert.Contains("---@field [integer] string\n", text);
        // table(name) narrows by name, an alias too.
        Assert.Equal(["unit_condition_levels", "conditions", "unit_current_levels", "unit_names"],
                     Regex.Matches(text, "---@overload fun\\(name: \"([a-z_]+)\"\\)").Select(match => match.Groups[1].Value));
        Assert.Contains("---@overload fun(name: \"conditions\"): test_base.unit_condition_levels\n", text);
        Assert.Equal(["g"], Fields(text, "test_base"));
        Assert.Contains("function M.table(name) end\n\n-- The helpers", text);
    }

    [Fact]
    public void TheEventsFileTypesEachFieldAndSaysWhoDeclaresIt()
    {
        string text = Stubs()[LuaStubs.EventsFile];
        Assert.Equal(["event", "amount", "unit"], Fields(text, "test.award"));
        Assert.Contains("---One award.\n---After the multipliers.\n---Declared by test-base, raised at test.point.", text);
        Assert.Contains("---@field event \"test.award\"\n---@field amount integer rw: L[0x1]\n"
                        + "---@field unit integer read-only: stage_turn_limit\n", text);
        Assert.Contains("---@field ratio number rw\n---@field crit boolean read-only\n---@field label string read-only\n", text);
        // In name order, whatever order the stack holds them in.
        Assert.True(text.IndexOf("---@class test.award", StringComparison.Ordinal)
                    < text.IndexOf("---@class test.computed", StringComparison.Ordinal));
        Assert.Contains("-- The stack declares no event.", LuaStubs.Files([], [])[LuaStubs.EventsFile]);
    }

    [Fact]
    public void EveryStubFileIsLuaThatCompiles()
    {
        using var runtime = new LuaModRuntime();
        foreach (var (name, text) in Stubs())
        {
            ModSourceCheck check = runtime.Check(new ModSource("stubs", name, text));
            Assert.True(check.Parsed, $"{name}: {string.Join("; ", check.Errors)}");
        }
    }

    [Fact]
    public void TheMaidDefinitionsNameWhatTheRuntimeGivesAMod()
    {
        string maid = Stubs()[LuaStubs.MaidFile];
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        var (stack, plan) = new LuaStackBuilder()
            .Base("test-base", Layout)
            .Mod("mod.vm", "-- file: init.lua\nreturn {}")
            .Build();
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup { Runtime = runtime, Stack = stack, Plan = plan });
        _owned.Add(native);
        Assert.Null(native.StartSession(SessionReason.Boot));
        string Run(string? mod, string code)
        {
            ConsoleRequest request = native.SubmitConsole(mod, code);
            native.ServiceRequests();
            Assert.True(request.Result!.Succeeded, request.Result.Text);
            return request.Result.Text;
        }

        // maid: a mod with vm has every field but names, which a base mod has (spec §8.4); the definitions name both.
        const string Keys = "local t = {} for k in pairs(maid) do t[#t+1] = k end table.sort(t) return table.concat(t, ',')";
        var declared = Fields(maid, "maid").Order(StringComparer.Ordinal).ToList();
        var inMod = Run("mod.vm", Keys).Split(',').ToList();
        var inBase = Run("test-base", Keys).Split(',').ToList();
        Assert.Equal(declared, inMod.Union(inBase).Order(StringComparer.Ordinal));

        // maid.vm's fields, each present (frame is nil outside a frame), and the sub-tables'.
        foreach (string field in Fields(maid, "maid.vm").Where(field => field != "frame"))
            Assert.Equal("true", Run("mod.vm", $"return tostring(maid.vm.{field} ~= nil)"));
        foreach (var (table, className) in new[] { ("log", "maid.log"), ("mod", "maid.mod"), ("events", "maid.events") })
            foreach (string field in Fields(maid, className).Where(field => field != "state_version"))
                Assert.Equal("true", Run("mod.vm", $"return tostring(maid.{table}.{field} ~= nil)"));
        Assert.Equal(["reason", "restoring"], Fields(maid, "maid.session"));
        Assert.Equal("boot false", Run("mod.vm", "return maid.session.reason .. ' ' .. tostring(maid.session.restoring)"));
        foreach (string field in Fields(maid, "maid.names"))
            Assert.Equal("true", Run("test-base", $"return tostring(maid.names.{field} ~= nil)"));

        // Each engine event this engine raises has its context class, with its one field.
        Assert.Equal(ModLuaLinker.BuiltEngineEvents.Order(StringComparer.Ordinal), LuaStubs.EngineEventFields.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, (field, _, _)) in LuaStubs.EngineEventFields)
            Assert.Equal(["event", field], Fields(maid, name));
    }
}
