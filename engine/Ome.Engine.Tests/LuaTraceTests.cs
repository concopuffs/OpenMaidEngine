using System.Text.Json;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Scripting.Lua;

/// <summary>
/// The attribution trace (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §12, <c>--lua-trace</c>; milestone 3,
/// piece 5): a line per handler call with the declared fields before and after and every write through
/// <c>maid.vm</c>, an accessor or <c>ctx.args</c>, each named for the innermost running handler. Gate 5's trace part,
/// and gate 15's base mod helper case. Over a synthetic script, a synthetic layout and hand-built stacks.
/// </summary>
public sealed class LuaTraceTests : IDisposable
{
    private const int T_IMM = 0, T_GINT = 3, T_LINT = 9;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private readonly List<IDisposable> _owned = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-lua-trace-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly GameLayout Layout = GameLayout.Parse("""
        {"layout": 1, "profile": "test", "globals": [
        {"name": "current_unit_id", "bank": "G", "address": "0x200", "dims": []},
        {"name": "unit_current_levels", "bank": "G", "address": "0x300", "dims": [4]},
        {"name": "unit_condition_levels", "bank": "G", "address": "0x400", "dims": [4, 3]},
        {"name": "unit_names", "bank": "GS", "address": "0x0", "dims": [4]}
        ]}
        """);

    private const string Base = "test-base";

    private static GameEvent Award() => new("test.award", Base, ["test.point"], null,
    [
        GameEvent.BuildField("amount", "int", "rw", "L[0x1]", true, Layout),
        GameEvent.BuildField("unit", "int", "r", "current_unit_id", true, Layout),
    ]);

    private Scripting Native(LuaStackBuilder builder, bool trace = true, string? diagnostics = null)
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        var (stack, plan) = builder.Build();
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = runtime, Stack = stack, Plan = plan, Trace = trace, DiagnosticsDirectory = diagnostics,
        });
        _owned.Add(native);
        return native;
    }

    private static Operand G(int address) => new(T_GINT, address);
    private static Operand L(int index) => new(T_LINT, index);
    private static Operand Imm(long value) => new(T_IMM, value);

    /// <summary>ROOT: the unit is 2, L[1] = 3; then G[0x100] = L[1] (instruction 2, the point); exit.</summary>
    private static Script Root() => ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
    {
        (0x55, [G(0x200), Imm(2)]),
        (0x55, [L(1), Imm(3)]),
        (0x55, [G(0x100), L(1)]),
        (0x2, []),
    }, []);

    private const int PointIndex = 2;

    private static VirtualMachine Run(Script script, Scripting native)
    {
        var vm = new VirtualMachine(script, Table, new HeadlessWaits(), scripting: native);
        vm.Run();
        return vm;
    }

    private static List<JsonElement> Lines(Scripting native)
        => native.RecentTrace.Select(line => JsonDocument.Parse(line).RootElement).ToList();

    private static (long Before, long After) Field(JsonElement line, string name)
    {
        JsonElement field = line.GetProperty("fields").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == name);
        return (field.GetProperty("before").GetInt64(), field.GetProperty("after").GetInt64());
    }

    private static void NoErrors(Scripting native)
        => Assert.DoesNotContain(native.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));

    [Fact]
    public void TwoSubscribersOnOneEventRunInTheReportedOrderAndTheTraceShowsTheValueAfterEach()
    {
        // Spec §12's chain: the base value, then each mod's.
        Script root = Root();
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout)
            .Mod("mod.gap", "-- file: gap.lua\nfunction on_award(e) e.amount = e.amount * 10 end", false, false, Base)
            .Mod("mod.bonus", "-- file: bonus.lua\nfunction on_award(e) e.amount = e.amount + 5 end", false, false, Base)
            .Event(Award())
            .Point(root, PointIndex, "mod.bonus", "bonus.lua:on_award", priority: 10, gameEvent: "test.award")
            .Point(root, PointIndex, "mod.gap", "gap.lua:on_award", gameEvent: "test.award"));

        VirtualMachine vm = Run(root, native);

        NoErrors(native);
        Assert.Equal(35, vm.Globals[0x100]);
        List<JsonElement> lines = Lines(native);
        Assert.Equal(2, lines.Count);
        Assert.Equal(("mod.gap", "gap.lua:on_award", "test.award", "event", "ok"),
                     (lines[0].GetProperty("mod").GetString(), lines[0].GetProperty("handler").GetString(),
                      lines[0].GetProperty("hook").GetString(), lines[0].GetProperty("kind").GetString(),
                      lines[0].GetProperty("result").GetString()));
        Assert.Equal($"ROOT.BIN@0x{root.Instructions[PointIndex].Offset:x}", lines[0].GetProperty("at").GetString());
        Assert.True(lines[0].GetProperty("ms").GetDouble() >= 0);
        Assert.Equal((3L, 30L), Field(lines[0], "amount"));
        Assert.Equal((2L, 2L), Field(lines[0], "unit"));
        Assert.Equal("mod.bonus", lines[1].GetProperty("mod").GetString());
        Assert.Equal((30L, 35L), Field(lines[1], "amount"));
        // A field write is in the fields, not in the writes.
        Assert.False(lines[0].TryGetProperty("writes", out _));
        Assert.False(lines[0].TryGetProperty("within", out _));
    }

    [Fact]
    public void EachWriteIsNamedForTheRunningHandlerAndABaseModsHelperWritesForItsCaller()
    {
        Script root = Root();
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout, """
                -- file: init.lua
                return { set_level = function(unit, level) maid.names.table("unit_current_levels")[unit] = level end }
                """)
            .Mod("mod.user", """
                -- file: user.lua
                function on_award(e)
                    local game = require("test-base")
                    game.g.current_unit_id = 3
                    game.set_level(1, 7)            -- the base mod's helper: no entry in this mod's list
                end
                """, false, false, Base)
            .Writes("mod.user", "current_unit_id")
            .Mod("mod.vm", """
                -- file: vm.lua
                function on_point(ctx)
                    maid.vm.g[0x204] = 4            -- a global the layout does not name
                    maid.vm.g[0x401] = 6            -- unit_condition_levels[0][1]
                    maid.vm.l[1] = 8
                    maid.vm.gs[0x1] = "名前"
                    ctx.args[1].value = 9
                end
                """)
            .Event(Award())
            .Point(root, PointIndex, "mod.user", "user.lua:on_award", gameEvent: "test.award")
            .Point(root, PointIndex, "mod.vm", "vm.lua:on_point", priority: 5));

        VirtualMachine vm = Run(root, native);

        NoErrors(native);
        Assert.Equal(7, vm.Globals[0x301]);
        Assert.Equal(8, vm.Globals[0x100]);
        List<JsonElement> lines = Lines(native);
        Assert.Equal(["mod.user", "mod.vm"], lines.Select(line => line.GetProperty("mod").GetString()));

        static List<(string?, string?, string?, string, string)> Writes(JsonElement line)
            => line.GetProperty("writes").EnumerateArray().Select(write => (
                write.GetProperty("cell").GetString(),
                write.TryGetProperty("name", out JsonElement name) ? name.GetString() : null,
                write.TryGetProperty("through", out JsonElement through) ? through.GetString() : null,
                write.GetProperty("before").ToString(), write.GetProperty("after").ToString())).ToList();

        Assert.Equal([("G[0x200]", "current_unit_id", null, "2", "3"),
                      ("G[0x301]", "unit_current_levels[1]", "test-base", "0", "7")], Writes(lines[0]));
        Assert.Equal([("G[0x204]", null, null, "0", "4"),
                      ("G[0x401]", "unit_condition_levels[0][1]", null, "0", "6"),
                      ("L[0x1]", null, null, "3", "8"),
                      ("GS[0x1]", "unit_names[1]", null, "", "名前"),
                      ("arg[1] G[0x100]", null, null, "0", "9")], Writes(lines[1]));
        // The field the subscriber read shows the unit before and after its own write.
        Assert.Equal((2L, 3L), Field(lines[0], "unit"));
    }

    [Fact]
    public void ACallInsideACallIsTracedWithTheHandlersItRanInsideAndAFailureWithItsResult()
    {
        Script root = Root();
        var computed = new GameEvent("test.computed", Base, [], null,
            [GameEvent.BuildField("amount", "int", "rw", null, false, Layout)]);
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout, """
                -- file: raise.lua
                function on_point(ctx)
                    local result = maid.events.emit("test.computed", { amount = 1 })
                    maid.vm.l[1] = result.amount
                end
                """)
            .Mod("mod.sub", """
                -- file: sub.lua
                function on_computed(e) e.amount = e.amount + 40 end
                function on_broken(e) error("broken on purpose") end
                """, false, false, Base)
            .Event(computed)
            .Point(root, PointIndex, Base, "raise.lua:on_point")
            .LuaEvent("test.computed", "mod.sub", "sub.lua:on_computed")
            .LuaEvent("test.computed", "mod.sub", "sub.lua:on_broken", priority: 5));

        VirtualMachine vm = Run(root, native);

        Assert.Equal(41, vm.Globals[0x100]);
        List<JsonElement> lines = Lines(native);
        Assert.Equal(["sub.lua:on_computed", "sub.lua:on_broken", "raise.lua:on_point"],
                     lines.Select(line => line.GetProperty("handler").GetString()));
        Assert.Equal(["test-base raise.lua:on_point"], lines[0].GetProperty("within").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal((1L, 41L), Field(lines[0], "amount"));
        Assert.False(lines[0].TryGetProperty("at", out _));
        Assert.Equal("error", lines[1].GetProperty("result").GetString());
        Assert.Contains("broken on purpose", lines[1].GetProperty("error").GetString());
        Assert.Equal((41L, 41L), Field(lines[1], "amount"));
        Assert.Equal(("point", "ok"), (lines[2].GetProperty("kind").GetString(), lines[2].GetProperty("result").GetString()));
        Assert.Equal([("L[0x1]", "3", "41")], lines[2].GetProperty("writes").EnumerateArray()
            .Select(write => (write.GetProperty("cell").GetString(), write.GetProperty("before").ToString(),
                              write.GetProperty("after").ToString())));
    }

    [Fact]
    public void TheTraceIsWrittenOnlyUnderTheFlagAndEachRunStartsTheFileAnew()
    {
        string diagnostics = Path.Combine(_root, "diagnostics");
        string file = Path.Combine(diagnostics, Scripting.TraceFile);
        LuaStackBuilder Stack(Script root) => new LuaStackBuilder()
            .Mod("mod.vm", "-- file: vm.lua\nfunction on_point(ctx) maid.vm.l[1] = maid.vm.l[1] + 1 end")
            .Point(root, PointIndex, "mod.vm", "vm.lua:on_point");

        Script quiet = Root();
        Scripting off = Native(Stack(quiet), trace: false, diagnostics: diagnostics);
        Assert.Equal(4, Run(quiet, off).Globals[0x100]);
        Assert.Empty(off.RecentTrace);
        Assert.False(File.Exists(file));

        for (int run = 0; run < 2; run++)
        {
            Script root = Root();
            Scripting native = Native(Stack(root), diagnostics: diagnostics);
            Run(root, native);
            native.Dispose();
            string[] written = File.ReadAllLines(file);
            Assert.Equal(native.RecentTrace, written);
            JsonElement line = JsonDocument.Parse(Assert.Single(written)).RootElement;
            Assert.Equal("test.point", line.GetProperty("hook").GetString());
        }
    }

    [Fact]
    public void ACellsNameInTheLayoutIsItsScalarOrItsTableWithTheRowAndColumn()
    {
        Assert.Equal("current_unit_id", Layout.NameAt(CellKind.Integer, 0x200));
        Assert.Equal("unit_current_levels[3]", Layout.NameAt(CellKind.Integer, 0x303));
        Assert.Null(Layout.NameAt(CellKind.Integer, 0x304));
        Assert.Equal("unit_condition_levels[2][1]", Layout.NameAt(CellKind.Integer, 0x407));
        // The table is open-ended only when the registry leaves it so; this one has 4 rows of 3.
        Assert.Null(Layout.NameAt(CellKind.Integer, 0x40c));
        Assert.Equal("unit_names[0]", Layout.NameAt(CellKind.String, 0x0));
        Assert.Null(Layout.NameAt(CellKind.Float, 0x0));
        GameLayout open = GameLayout.Parse("""
            {"layout": 1, "profile": "test", "globals": [{"name": "rows", "bank": "G", "address": "0x10", "dims": [null, 2]}]}
            """);
        Assert.Equal("rows[100][1]", open.NameAt(CellKind.Integer, 0x10 + 201));
    }
}
