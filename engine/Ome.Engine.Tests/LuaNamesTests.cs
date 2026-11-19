using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Scripting.Lua;

/// <summary>
/// The accessors of docs/superpowers/specs/2026-11-10-lua-scripting-design.md §9.3 (milestone 3, piece 2): a mod that
/// requires a base mod reads any name it exports and writes the names its list gives; a base mod's own
/// <c>maid.names</c> and the console's accessors have no list; indices reach only the named global's cells. Over a
/// synthetic layout and hand-built stacks (the link's output); the link's side is in <see cref="ModLuaLinkTests"/>.
/// </summary>
public sealed class LuaNamesTests : IDisposable
{
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        // The natives' sessions end before the runtime that built them.
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
    }

    /// <summary>A scalar with an alias, a 1-D table with a column name, an open 2-D table that the next global ends
    /// after three rows, a string table and a float.</summary>
    private static readonly GameLayout Layout = GameLayout.Parse("""
        {"layout": 1, "profile": "test", "globals": [
        {"name": "current_unit_id", "bank": "G", "address": "0x10", "dims": []},
        {"name": "stage_turn_limit", "bank": "G", "address": "0x11", "dims": [], "aliases": ["turn_limit"]},
        {"name": "unit_current_levels", "bank": "G", "address": "0x100", "dims": [4], "columns": {"hero": 0}},
        {"name": "unit_condition_levels", "bank": "G", "address": "0x200", "dims": [null, 3], "columns": {"charm": 1}},
        {"name": "after_conditions", "bank": "G", "address": "0x209", "dims": []},
        {"name": "unit_names", "bank": "GS", "address": "0x0", "dims": [4]},
        {"name": "exp_rate", "bank": "GF", "address": "0x0", "dims": []}
        ]}
        """);

    private const string Requires = "test-base";

    private Scripting Native(LuaStackBuilder builder, Func<BaseModLayout?>? console = null)
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        var (stack, plan) = builder.Build();
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = runtime, Stack = stack, Plan = plan, ConsoleLayout = console,
        });
        _owned.Add(native);
        return native;
    }

    /// <summary>A base mod and a mod that requires it, with the given write list, and a session.</summary>
    private Scripting Stack(params string[] writes)
    {
        Scripting native = Native(new LuaStackBuilder()
            .Base(Requires, Layout)
            .Mod("user", "-- file: init.lua\nreturn {}", false, false, Requires)
            .Writes("user", writes));
        Assert.Null(native.StartSession(SessionReason.Boot));
        return native;
    }

    private static ConsoleResult TryChunk(Scripting native, string? mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        Assert.True(request.Done);
        return request.Result!;
    }

    private static string Chunk(Scripting native, string? mod, string code)
    {
        ConsoleResult result = TryChunk(native, mod, code);
        Assert.True(result.Succeeded, result.Text);
        return result.Text;
    }

    private static string Fails(Scripting native, string? mod, string code)
    {
        ConsoleResult result = TryChunk(native, mod, code);
        Assert.False(result.Succeeded, result.Text);
        return result.Text;
    }

    [Fact]
    public void AModThatRequiresTheBaseModReadsEveryExportedNameWithoutVm()
    {
        Scripting native = Stack();
        ScriptMemory memory = native.Memory;
        memory.SetInteger(0x10, 7);
        memory.SetInteger(0x11, 30);
        memory.SetInteger(0x102, 12);
        memory.SetInteger(0x100, 99);
        memory.SetInteger(0x200 + 2 * 3 + 1, 4);
        memory.SetString(0x3, "リリィ");
        memory.SetFloat(0x0, BitConverter.SingleToInt32Bits(1.5f));
        Assert.Equal("7\t30\t30\t12\t99\t4\tリリィ\t1.5\t4\t3\t3", Chunk(native, "user", """
            local game = require("test-base")
            local levels = game.table("unit_current_levels")
            local conditions = game.table("unit_condition_levels")
            return game.g.current_unit_id, game.g.stage_turn_limit, game.g.turn_limit, levels[2], levels.hero,
                   conditions[2].charm, game.table("unit_names")[3], game.g.exp_rate, #levels, #conditions, #conditions[0]
            """));
        Assert.Equal("nil", Chunk(native, "user", "return maid.vm"));
    }

    [Fact]
    public void AModWritesTheNamesItListsAndAWriteItDoesNotListIsAnErrorThatNamesTheList()
    {
        Scripting native = Stack("stage_turn_limit", "unit_condition_levels", "unit_names", "exp_rate");
        Chunk(native, "user", """
            local game = require("test-base")
            game.g.turn_limit = 45
            game.table("unit_condition_levels")[1][2] = -3
            game.table("unit_condition_levels")[0].charm = 2
            game.table("unit_names")[1] = "ユイ"
            game.g.exp_rate = 0.1
            """);
        ScriptMemory memory = native.Memory;
        Assert.Equal(45, memory.Integer(0x11));
        Assert.Equal(-3, (int)memory.Integer(0x200 + 3 + 2));
        Assert.Equal(2, memory.Integer(0x201));
        Assert.Equal("ユイ", memory.String(0x1));
        Assert.Equal(0.1f, BitConverter.Int32BitsToSingle((int)memory.Float(0x0)));

        string error = Fails(native, "user", "require('test-base').g.current_unit_id = 1");
        Assert.Contains("user writes current_unit_id, which its [lua] writes list does not name "
                        + "(writes = [exp_rate, stage_turn_limit, unit_condition_levels, unit_names])", error);
        Assert.Contains("which its [lua] writes list does not name (it lists none)",
                        Fails(Stack(), "user", "require('test-base').table('unit_current_levels')[0] = 1"));
        Assert.Equal(0, memory.Integer(0x10));
        // pcall catches the refusal, as it catches any Lua error.
        Assert.Equal("false", Chunk(native, "user", "return (pcall(function() require('test-base').g.current_unit_id = 1 end))"));
    }

    [Fact]
    public void UnderLuaDevAnUndeclaredWriteGoesThroughAndTheLogNamesItOnce()
    {
        Scripting native = Native(new LuaStackBuilder()
            .Base(Requires, Layout)
            .Mod("user", "-- file: init.lua\nreturn {}", false, false, Requires)
            .Writes("user", "unit_names")
            .LuaDev("user"));
        Assert.Null(native.StartSession(SessionReason.Boot));

        Chunk(native, "user", "local game = require('test-base') game.g.turn_limit = 7 game.g.stage_turn_limit = 8 "
                              + "game.table('unit_current_levels')[1] = 2");
        Assert.Equal(8, native.Memory.Integer(0x11));
        Assert.Equal(2, native.Memory.Integer(0x101));
        string line = Assert.Single(native.RecentLog, entry => entry.Contains("stage_turn_limit", StringComparison.Ordinal));
        Assert.Contains("user: writes stage_turn_limit, which its [lua] writes list does not name (writes = [unit_names]); "
                        + "--lua-dev lets it through", line);
        Assert.Single(native.RecentLog, entry => entry.Contains("unit_current_levels", StringComparison.Ordinal));
        // The rules other than the list still hold.
        Assert.Contains("an integer cell holds a whole number", Fails(native, "user", "require('test-base').g.stage_turn_limit = 1.5"));
    }

    [Theory]
    [InlineData("return game.table('unit_current_levels')[4]", "unit_current_levels[4]: an index is a whole number from 0 to 3")]
    [InlineData("return game.table('unit_current_levels')[-1]", "from 0 to 3")]
    [InlineData("return game.table('unit_current_levels')[1.5]", "unit_current_levels[1.5]")]
    [InlineData("return game.table('unit_condition_levels')[3]", "unit_condition_levels[3]: an index is a whole number from 0 to 2")]
    [InlineData("return game.table('unit_condition_levels')[0][3]", "a column is a whole number from 0 to 2, not 3")]
    [InlineData("return game.table('unit_condition_levels')[0].fear", "has no column 'fear' (its columns: charm)")]
    [InlineData("return game.table('unit_condition_levels').charm", "is a table of rows: index a row first")]
    [InlineData("return game.table('unit_names').hero", "unit_names names no columns")]
    [InlineData("return game.g.unit_current_levels", "unit_current_levels is a table: use .table(\"unit_current_levels\")")]
    [InlineData("return game.table('current_unit_id')", "current_unit_id is no table: read it as .g.current_unit_id")]
    [InlineData("return game.g.no_such_name", "test-base exports no global named 'no_such_name'")]
    [InlineData("game.g.stage_turn_limit = 'thirty'", "stage_turn_limit holds numbers, not a string")]
    [InlineData("game.g.stage_turn_limit = 1.5", "an integer cell holds a whole number in 32 bits")]
    [InlineData("game.table('unit_names')[0] = 5", "unit_names holds strings, not a number")]
    [InlineData("game.table('unit_names')[0] = true", "unit_names takes a string, not boolean")]
    [InlineData("game.table('unit_condition_levels')[0] = 1", "is a table of rows: write a cell of a row")]
    [InlineData("game.extra = 1", "what test-base gives is read-only")]
    public void IndicesReachOnlyTheNamedGlobalsCellsAndValuesKeepTheirKind(string code, string message)
    {
        Scripting native = Stack("stage_turn_limit", "unit_names", "unit_condition_levels");
        Assert.Contains(message, Fails(native, "user", "local game = require('test-base')\n" + code));
    }

    [Fact]
    public void ABaseModsOwnNamesAndTheConsolesHaveNoWriteList()
    {
        Scripting native = Stack();
        Chunk(native, Requires, "maid.names.g.current_unit_id = 3; maid.names.table('unit_current_levels').hero = 8");
        Chunk(native, null, "require('test-base').g.stage_turn_limit = 9; maid.names.g.turn_limit = maid.names.g.turn_limit + 1");
        Assert.Equal((3, 8, 10), ((int)native.Memory.Integer(0x10), (int)native.Memory.Integer(0x100), (int)native.Memory.Integer(0x11)));
        // A mod that requires the base mod has no maid.names: the base mod's names are its own.
        Assert.Equal("nil", Chunk(native, "user", "return maid.names"));
    }

    [Fact]
    public void TheConsoleReadsTheProfilesBaseModWhenNoModMountsIt()
    {
        int asked = 0;
        Scripting native = Native(new LuaStackBuilder(), () =>
        {
            asked++;
            return new BaseModLayout(Requires, Layout);
        });
        Assert.Null(native.StartSession(SessionReason.Boot));
        Assert.Equal(0, asked);
        native.Memory.SetInteger(0x10, 5);
        Assert.Equal("5\t5", Chunk(native, null, "return maid.names.g.current_unit_id, require('test-base').g.current_unit_id"));
        Chunk(native, null, "maid.names.g.current_unit_id = 6");
        Assert.Equal((6, 1), ((int)native.Memory.Integer(0x10), asked));
        Assert.Contains("module 'other-base' not found", Fails(native, null, "return require('other-base')"));
    }

    [Fact]
    public void WhatABaseModsInitReturnsSitsBesideTheAccessorsReadOnly()
    {
        Scripting native = Native(new LuaStackBuilder()
            .Base(Requires, Layout, "-- file: init.lua\nreturn { label = 'names', twice = function(x) return x * 2 end, nested = { a = 1 } }")
            .Mod("user", "-- file: init.lua\nreturn {}", false, false, Requires));
        Assert.Null(native.StartSession(SessionReason.Boot));
        Assert.Equal("names\t8\t1\t0", Chunk(native, "user", """
            local game = require("test-base")
            return game.label, game.twice(4), game.nested.a, game.g.current_unit_id
            """));
        Assert.Contains("read-only", Fails(native, "user", "require('test-base').nested.a = 2"));
        Assert.Equal("true", Chunk(native, "user", "return require('test-base') == require('test-base')"));
    }

    [Fact]
    public void ABaseModWhoseInitReturnsAnAccessorsNameIsOffAndSoAreTheModsThatRequireIt()
    {
        Scripting native = Native(new LuaStackBuilder()
            .Base(Requires, Layout, "-- file: init.lua\nreturn { table = {} }")
            .Mod("user", "-- file: init.lua\nreturn {}", false, false, Requires));
        Assert.Null(native.StartSession(SessionReason.Boot));
        Assert.Contains("is off for this session", Fails(native, "user", "return 1"));
        Assert.Contains("is off for this session", Fails(native, Requires, "return 1"));
    }

    [Fact]
    public void AModThatDoesNotRequireTheBaseModCannotNameItsCells()
    {
        Scripting native = Native(new LuaStackBuilder()
            .Base(Requires, Layout)
            .Mod("other", "-- file: init.lua\nreturn {}", false));
        Assert.Null(native.StartSession(SessionReason.Boot));
        Assert.Contains("module 'test-base' not found", Fails(native, "other", "return require('test-base')"));
    }

    [Fact]
    public void AnOpenFirstDimensionEndsWhereTheNextGlobalOfItsBankStarts()
    {
        LayoutGlobal open = Assert.Single(Layout.Globals, global => global.Name == "unit_condition_levels");
        Assert.Equal((3, 3), (open.Rows, open.RowWidth));
        Assert.Equal(4, Layout.Globals.Single(global => global.Name == "unit_current_levels").Rows);
        Assert.Null(Layout.Globals.Single(global => global.Name == "current_unit_id").Rows);
        // An open array that nothing follows is bounded by the last address alone.
        GameLayout last = GameLayout.Parse("""
            {"layout": 1, "profile": "t", "globals": [{"name": "tail", "bank": "G", "address": "0x7ffffff0", "dims": [null, 4]}]}
            """);
        LayoutGlobal tail = last.Globals[0];
        Assert.Null(tail.Rows);
        Assert.Equal(int.MaxValue - 3, LayoutCells.Address(tail, LayoutCells.Row(tail, 3), 0));
        Assert.Throws<ScriptAccessException>(() => LayoutCells.Row(tail, 4));
        // Indices handed straight to the face are checked again.
        Assert.Throws<ScriptAccessException>(() => LayoutCells.Address(open, 3, 0));
        Assert.Throws<ScriptAccessException>(() => LayoutCells.Address(open, 0, -1));
    }

    [Fact]
    public void TextFitsSaysWhatAStringCellWouldTakeAndWritesNothing()
    {
        // Data patches spec §6.2: a mod can test a text before its first write. The answer is the write's own: a text
        // CP932 encodes fits, and one it cannot is refused with the reason the write would give. A mod without vm and
        // without a write list has it, and the console too.
        Scripting native = Stack("unit_names");
        ScriptMemory memory = native.Memory;
        memory.SetString(0x1, "ユイ");
        Assert.Equal("true\ttrue\ttrue", Chunk(native, "user", """
            return maid.text_fits("リリィ"), maid.text_fits(""), maid.text_fits("Lily ~ 100%")
            """));
        string refused = Chunk(native, "user", "return maid.text_fits(\"Lily 한\")");
        Assert.Equal("false\ta string cell holds text the save can encode (code page 932); U+D55C is not in it", refused);
        string written = Fails(native, "user", "require('test-base').table('unit_names')[1] = \"Lily 한\"");
        Assert.Contains(refused["false\t".Length..], written);
        Assert.Equal("ユイ", memory.String(0x1));
        Assert.Equal("true", Chunk(native, null, "return maid.text_fits('名前')"));
        Assert.Contains("bad argument #1 to 'text_fits' (a string expected, got number)",
                        Fails(native, "user", "return maid.text_fits(5)"));
        Assert.Contains("bad argument #1 to 'text_fits' (a string expected, got nil)",
                        Fails(native, "user", "return maid.text_fits()"));
    }
}
