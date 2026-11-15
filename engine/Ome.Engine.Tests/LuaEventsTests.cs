using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Manifest;
using Ome.Scripting.Lua;

/// <summary>
/// Game events (docs/superpowers/specs/2026-11-10-lua-scripting-design.md §7.4, §8.2, §9.2; milestone 3, piece 3) and
/// the runtime half of gate 5: an event's subscribers and the point hooks at its instruction run in one merged order,
/// each seeing the cells as the one before left them; an event raised from Lua runs its subscribers inside the emit.
/// Over synthetic scripts, a synthetic layout and hand-built stacks (the link's output); the link's side is in
/// <see cref="ModLuaLinkTests"/>.
/// </summary>
public sealed class LuaEventsTests : IDisposable
{
    private const int T_IMM = 0, T_GINT = 3, T_LINT = 9;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
    }

    private static readonly GameLayout Layout = GameLayout.Parse("""
        {"layout": 1, "profile": "test", "globals": [
        {"name": "current_unit_id", "bank": "G", "address": "0x200", "dims": [], "aliases": ["unit_id"]},
        {"name": "unit_current_levels", "bank": "G", "address": "0x300", "dims": [4], "columns": {"hero": 0}},
        {"name": "unit_condition_levels", "bank": "G", "address": "0x400", "dims": [4, 3], "columns": {"charm": 1}},
        {"name": "unit_names", "bank": "GS", "address": "0x0", "dims": [4]},
        {"name": "exp_rate", "bank": "GF", "address": "0x0", "dims": []}
        ]}
        """);

    private const string Base = "test-base";

    private static GameEventField Field(string name, string type, string access, string? at, bool atPoint = true)
        => GameEvent.BuildField(name, type, access, at, atPoint, Layout);

    /// <summary>An event at <c>test.point</c>: the award in L[1], the unit, its level, and a flag in G[0x201].</summary>
    private static GameEvent Award() => new("test.award", Base, "test.point", null,
    [
        Field("amount", "int", "rw", "L[0x1]"),
        Field("unit", "int", "r", "current_unit_id"),
        Field("level", "int", "r", "unit_current_levels[current_unit_id]"),
        Field("flag", "bool", "rw", "G[0x201]"),
    ]);

    /// <summary>An event its owner raises from Lua.</summary>
    private static GameEvent Computed() => new("test.computed", Base, null, null,
    [
        Field("amount", "int", "rw", null, atPoint: false),
        Field("name", "string", "r", null, atPoint: false),
        Field("ratio", "float", "rw", null, atPoint: false),
        Field("crit", "bool", "rw", null, atPoint: false),
    ]);

    private Scripting Native(LuaStackBuilder builder, LuaErrorPolicy errors = LuaErrorPolicy.Continue, ModLimits? limits = null)
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        var (stack, plan) = builder.Build();
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = runtime, Stack = stack, Plan = plan, Errors = errors, Limits = limits ?? ModLimits.Default,
        });
        _owned.Add(native);
        return native;
    }

    private static VirtualMachine Run(Script script, Scripting native)
    {
        var vm = new VirtualMachine(script, Table, new HeadlessWaits(), scripting: native);
        vm.Run();
        return vm;
    }

    private static Operand G(int address) => new(T_GINT, address);
    private static Operand L(int index) => new(T_LINT, index);
    private static Operand Imm(long value) => new(T_IMM, value);

    /// <summary>ROOT: the unit is 2 at level 9, the flag 5, L[1] = 3; then G[0x100] = L[1] (instruction 4, the point);
    /// exit.</summary>
    private static Script Root() => ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
    {
        (0x55, [G(0x200), Imm(2)]),
        (0x55, [G(0x302), Imm(9)]),
        (0x55, [G(0x201), Imm(5)]),
        (0x55, [L(1), Imm(3)]),
        (0x55, [G(0x100), L(1)]),
        (0x2, []),
    }, []);

    private const int PointIndex = 4;

    private static ConsoleResult TryChunk(Scripting native, string? mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        return request.Result!;
    }

    private static void NoErrors(Scripting native)
        => Assert.DoesNotContain(native.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));

    // ------------------------------------------------------------------------------------------- an event at a point

    [Fact]
    public void SubscribersAndAPointHookAtOneInstructionRunInTheMergedOrderAndSeeEachOthersChanges()
    {
        Script root = Root();
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout)
            .Mod("mod.a", """
                -- file: a.lua
                function on_award(ctx)
                    assert(maid.vm == nil, "a subscriber needs no vm")
                    assert(ctx.event == "test.award")
                    assert(ctx.unit == 2 and ctx.level == 9 and ctx.flag == true, "the fields read their cells")
                    ctx.amount = ctx.amount * 2
                    local ok, err = pcall(function() ctx.unit = 1 end)
                    assert(not ok and err:find("field unit of test.award is read%-only"), err)
                    ok, err = pcall(function() ctx.flag = 1 end)
                    assert(not ok and err:find("field flag is bool, not number"), err)
                    ok, err = pcall(function() ctx.amount = 1.5 end)
                    assert(not ok and err:find("whole number"), err)
                    ok, err = pcall(function() ctx.nothing = 1 end)
                    assert(not ok and err:find("test.award has no field nothing"), err)
                    assert(ctx.nothing == nil)
                    ctx.flag = false
                end
                """, false, false, Base)
            .Mod("mod.b", """
                -- file: b.lua
                function on_point(ctx)
                    assert(maid.vm.l[1] == 6, "the point hook sees the first subscriber's change")
                    maid.vm.l[1] = maid.vm.l[1] + 1
                end
                """)
            .Mod("mod.c", """
                -- file: c.lua
                function on_award(ctx)
                    assert(ctx.amount == 7 and ctx.flag == false, tostring(ctx.amount))
                    ctx.amount = ctx.amount + 100
                end
                """, false, false, Base)
            .Event(Award())
            .Point(root, PointIndex, "mod.c", "c.lua:on_award", priority: 10, gameEvent: "test.award")
            .Point(root, PointIndex, "mod.a", "a.lua:on_award", gameEvent: "test.award")
            .Point(root, PointIndex, "mod.b", "b.lua:on_point", priority: 5));

        VirtualMachine vm = Run(root, native);

        NoErrors(native);
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(107, vm.Globals[0x100]);
        Assert.Equal(0, vm.Globals[0x201]);
        Assert.Equal(2, vm.Globals[0x200]);
    }

    [Fact]
    public void ASkippedInstructionsEventStillReachesItsSubscribersAndTheirWritesAreMade()
    {
        // Spec §7.1: a point hook skips the point's G[0x100] = L[1]; the award's subscriber still runs, its write to the
        // flag is made, and the copy that would have used the amount does not run.
        Script root = Root();
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout)
            .Mod("mod.a", "-- file: a.lua\nfunction skips(ctx) ctx:skip() end")
            .Mod("mod.b", """
                -- file: b.lua
                function on_award(ctx)
                    assert(ctx.amount == 3 and ctx.flag == true)
                    ctx.flag = false
                    ctx.amount = 40
                end
                """, false, false, Base)
            .Event(Award())
            .Point(root, PointIndex, "mod.a", "a.lua:skips", priority: -1, skip: true)
            .Point(root, PointIndex, "mod.b", "b.lua:on_award", gameEvent: "test.award"));

        VirtualMachine vm = Run(root, native);

        NoErrors(native);
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(0, vm.Globals[0x201]);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x100));
    }

    [Fact]
    public void AFieldWhoseCellHoldsAnotherKindIsAnErrorThatNamesItsLocation()
    {
        Script root = Root();
        // The operand form: arg[2] of the point's instruction is L[1], an integer cell a write changes.
        var award = new GameEvent("test.award", Base, "test.point", null,
        [
            Field("value", "int", "rw", "arg[2]"),
            Field("target", "int", "r", "arg[1]"),
            Field("wrong", "float", "r", "arg[2]"),
            Field("missing", "int", "r", "arg[3]"),
        ]);
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout)
            .Mod("mod.a", """
                -- file: a.lua
                function on_award(ctx)
                    assert(ctx.value == 3 and ctx.target == 0, tostring(ctx.target))
                    ctx.value = 40
                    local ok, err = pcall(function() return ctx.wrong end)
                    assert(not ok and err:find("field wrong: arg%[2%] holds an integer"), err)
                    ok, err = pcall(function() return ctx.missing end)
                    assert(not ok and err:find("has 2 operands; there is no arg%[3%]"), err)
                end
                """, false, false, Base)
            .Event(award)
            .Point(root, PointIndex, "mod.a", "a.lua:on_award", gameEvent: "test.award"));

        VirtualMachine vm = Run(root, native);

        NoErrors(native);
        Assert.Equal(40, vm.Globals[0x100]);
    }

    // ------------------------------------------------------------------------------------------- raised from Lua

    /// <summary>The base mod raises <c>test.computed</c> at the point from the frame's L[1], and keeps what its
    /// subscribers leave.</summary>
    private const string Raiser = """
        -- file: base.lua
        function on_point(ctx)
            local fields = maid.events.emit("test.computed", { amount = maid.vm.l[1], name = "x", ratio = 0.5, crit = false })
            assert(fields.name == "x")
            maid.vm.l[1] = fields.amount
            maid.vm.g[0x110] = fields.crit and 1 or 0
        end
        """;

    [Fact]
    public void AnEventRaisedFromLuaRunsItsSubscribersInsideTheEmitInTheirOwnOrder()
    {
        Script root = Root();
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout, Raiser)
            .Mod("mod.a", """
                -- file: a.lua
                function on_computed(ctx)
                    assert(ctx.event == "test.computed" and ctx.amount == 3)
                    ctx.amount = ctx.amount * 2
                    ctx.crit = true
                end
                """, false, false, Base)
            .Mod("mod.c", """
                -- file: c.lua
                function on_computed(ctx)
                    assert(ctx.amount == 6 and ctx.ratio == 0.5 and ctx.crit == true)
                    ctx.amount = ctx.amount + 1
                    local ok, err = pcall(function() ctx.name = "y" end)
                    assert(not ok and err:find("read%-only"), err)
                end
                """, false, false, Base)
            .Event(Computed())
            .Point(root, PointIndex, Base, "base.lua:on_point")
            .LuaEvent("test.computed", "mod.c", "c.lua:on_computed", priority: 1)
            .LuaEvent("test.computed", "mod.a", "a.lua:on_computed"));

        VirtualMachine vm = Run(root, native);

        NoErrors(native);
        Assert.Equal(7, vm.Globals[0x100]);
        Assert.Equal(1, vm.Globals[0x110]);
    }

    [Theory]
    [InlineData(Base, "maid.events.emit('test.none', {})", "no mod of the stack declares the event 'test.none'")]
    [InlineData("mod.a", "maid.events.emit('test.computed', { amount = 1, name = 'x', ratio = 0, crit = false })",
                "test.computed is raised by test-base, which declares it; mod.a cannot raise it")]
    [InlineData(Base, "maid.events.emit('test.award', { amount = 1, unit = 1, level = 1, flag = false })",
                "test.award is raised at its point (test.point), not from Lua")]
    [InlineData(Base, "maid.events.emit('test.computed', { amount = 1, name = 'x', ratio = 0 })", "test.computed: field crit is missing")]
    [InlineData(Base, "maid.events.emit('test.computed', { amount = 1, name = 'x', ratio = 0, crit = false, extra = 1 })",
                "test.computed has no field 'extra'")]
    [InlineData(Base, "maid.events.emit('test.computed', { amount = 's', name = 'x', ratio = 0, crit = false })",
                "field amount is int, not string")]
    [InlineData(Base, "maid.events.emit('test.computed', { amount = 1, name = 'x', ratio = 0, crit = 1 })",
                "field crit is bool, not number")]
    [InlineData(Base, "maid.events.emit('test.computed', { amount = 2^40, name = 'x', ratio = 0, crit = false })", "32 bits")]
    [InlineData(Base, "maid.events.emit('test.computed', { [1] = 2 })", "a field is named by a string, not number")]
    [InlineData(Base, "maid.events.emit('test.computed')", "bad argument #2 to 'emit'")]
    public void EmitChecksTheEventItsRaiserAndItsFields(string mod, string code, string message)
    {
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout, "-- file: init.lua\nreturn {}")
            .Mod("mod.a", "-- file: init.lua\nreturn {}", false, false, Base)
            .Event(Award())
            .Event(Computed()));
        Assert.Null(native.StartSession(SessionReason.Boot));

        ConsoleResult result = TryChunk(native, mod, code);
        Assert.False(result.Succeeded);
        Assert.Contains(message, result.Text);
    }

    [Fact]
    public void ASubscribersErrorIsItsOwnAndTheEmitGoesOnUnlessTheRunHalts()
    {
        LuaStackBuilder Stack() => new LuaStackBuilder()
            .Base(Base, Layout, Raiser)
            .Mod("mod.a", "-- file: a.lua\nfunction on_computed(ctx) error('broken subscriber') end", false, false, Base)
            .Mod("mod.c", "-- file: c.lua\nfunction on_computed(ctx) ctx.amount = ctx.amount + 1 end", false, false, Base)
            .Event(Computed())
            .LuaEvent("test.computed", "mod.a", "a.lua:on_computed")
            .LuaEvent("test.computed", "mod.c", "c.lua:on_computed", priority: 1);

        // Continue: the failure is the subscriber's, the next subscriber runs, and the raiser keeps the values.
        Script root = Root();
        Scripting native = Native(Stack().Point(root, PointIndex, Base, "base.lua:on_point"));
        VirtualMachine vm = Run(root, native);
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(4, vm.Globals[0x100]);
        string error = Assert.Single(native.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));
        Assert.Contains("mod.a", error);
        Assert.Contains("a.lua:on_computed failed at test.computed: ", error);
        Assert.Contains("broken subscriber", error);

        // Halt: the instruction the raiser ran at ends the run, with the subscriber's reason.
        root = Root();
        native = Native(Stack().Point(root, PointIndex, Base, "base.lua:on_point"), LuaErrorPolicy.Halt);
        vm = Run(root, native);
        Assert.StartsWith("lua-error: mod.a a.lua:on_computed at test.computed: ", vm.HaltReason);
        Assert.False(vm.Globals.ContainsKey(0x100));   // the instruction did not run
    }

    [Fact]
    public void ASubscriberHasABudgetOfItsOwnAndARecursiveEmitStopsAtTheNestingCap()
    {
        Script root = Root();
        Scripting native = Native(new LuaStackBuilder()
            .Base(Base, Layout, Raiser + "\n" + """
                -- file: again.lua
                depth = 0
                function on_computed(ctx)
                    depth = depth + 1
                    maid.events.emit("test.computed", { amount = ctx.amount, name = "x", ratio = 0, crit = false })
                end
                """)
            .Mod("mod.a", "-- file: a.lua\nfunction on_computed(ctx) while true do end end", false, false, Base)
            .Event(Computed())
            .Point(root, PointIndex, Base, "base.lua:on_point")
            .LuaEvent("test.computed", "mod.a", "a.lua:on_computed")
            .LuaEvent("test.computed", Base, "again.lua:on_computed", priority: 1),
            limits: ModLimits.Default with { HandlerInstructions = 50_000, NestingCap = 4 });

        VirtualMachine vm = Run(root, native);

        // The raiser finished: its write after the emit happened.
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(3, vm.Globals[0x100]);
        Assert.Contains(native.RecentLog, line => line.Contains("a.lua:on_computed ran out of its instruction budget", StringComparison.Ordinal));
        Assert.Contains(native.RecentLog, line => line.Contains("nested too deep", StringComparison.Ordinal));
        Assert.DoesNotContain(native.RecentLog, line => line.Contains("base.lua:on_point", StringComparison.Ordinal)
                                                        && line.Contains(" error ", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------- locations (§9.2)

    [Fact]
    public void ALocationReadsAsItsCell()
    {
        var cases = new (string Text, FieldLocation Expected)[]
        {
            ("L[0x1]", new FieldLocation.Local(CellKind.Integer, 1)),
            ("LF[2]", new FieldLocation.Local(CellKind.Float, 2)),
            (" LS[ 3 ] ", new FieldLocation.Local(CellKind.String, 3)),
            ("G[0x62ccb]", new FieldLocation.Global(CellKind.Integer, 0x62ccb, null)),
            ("GF[1]", new FieldLocation.Global(CellKind.Float, 1, null)),
            ("GS[0x2]", new FieldLocation.Global(CellKind.String, 2, null)),
            ("arg[1]", new FieldLocation.Operand(0)),
            ("current_unit_id", new FieldLocation.Global(CellKind.Integer, 0x200, "current_unit_id")),
            ("unit_id", new FieldLocation.Global(CellKind.Integer, 0x200, "current_unit_id")),   // an alias
            ("exp_rate", new FieldLocation.Global(CellKind.Float, 0, "exp_rate")),
        };
        foreach (var (text, expected) in cases) Assert.Equal(expected, FieldLocation.Parse(text, Layout));
    }

    [Fact]
    public void ATableElementIsIndexedByOtherLocations()
    {
        var element = Assert.IsType<FieldLocation.Element>(FieldLocation.Parse("unit_current_levels[current_unit_id]", Layout));
        Assert.Equal("unit_current_levels", element.Table.Name);
        Assert.Equal(new FieldLocation.Global(CellKind.Integer, 0x200, "current_unit_id"), element.Row);
        Assert.Null(element.Column);

        var named = Assert.IsType<FieldLocation.Element>(FieldLocation.Parse("unit_condition_levels[L[2]].charm", Layout));
        Assert.Equal(new FieldLocation.Local(CellKind.Integer, 2), named.Row);
        Assert.Equal(new FieldLocation.Number(1), named.Column);

        var cell = Assert.IsType<FieldLocation.Element>(FieldLocation.Parse("unit_condition_levels[3][arg[2]]", Layout));
        Assert.Equal((new FieldLocation.Number(3), (FieldLocation)new FieldLocation.Operand(1)), (cell.Row, cell.Column!));
        Assert.Equal(CellKind.String, FieldLocation.Parse("unit_names[0]", Layout).Kind);
    }

    [Theory]
    [InlineData("arg[0]", "operands are numbered from 1")]
    [InlineData("L[x]", "a local's index is a number, not 'x'")]
    [InlineData("G[1] G[2]", "nothing more expected at column 5")]
    [InlineData("no_such_name", "the layout names no global 'no_such_name'")]
    [InlineData("unit_current_levels", "unit_current_levels is a table: index it")]
    [InlineData("current_unit_id[1]", "current_unit_id is no table")]
    [InlineData("unit_condition_levels[1]", "is a table of rows: name a column")]
    [InlineData("unit_current_levels[1].hero", "is one row: its column names name elements")]
    [InlineData("unit_current_levels[unit_names[0]]", "an index of unit_current_levels is an integer location")]
    [InlineData("unit_current_levels[", "a location expected")]
    [InlineData("-1", "a location expected at column 1")]
    public void ATextThatIsNoLocationSaysWhy(string text, string message)
        => Assert.Contains(message, Assert.Throws<FormatException>(() => FieldLocation.Parse(text, Layout)).Message);

    [Fact]
    public void AFixedIndexOutsideItsTableAndANameWithoutALayoutAreRefused()
    {
        Assert.Contains("unit_current_levels[4]: an index is a whole number from 0 to 3", Assert.Throws<ScriptAccessException>(() => FieldLocation.Parse("unit_current_levels[4]", Layout)).Message);
        Assert.Contains("no base mod's layout names globals here",
                        Assert.Throws<FormatException>(() => FieldLocation.Parse("current_unit_id", null)).Message);
        Assert.Equal(new FieldLocation.Local(CellKind.Integer, 1), FieldLocation.Parse("L[1]", null));
    }

    [Theory]
    [InlineData("event", "int", "r", "L[1]", true, "a field's name is a Lua name other than 'event'")]
    [InlineData("a-b", "int", "r", "L[1]", true, "a field's name is a Lua name")]
    [InlineData("x", "long", "r", "L[1]", true, "type 'long' is not int, float, string or bool")]
    [InlineData("x", "int", "w", "L[1]", true, "access 'w' is not \"r\" or \"rw\"")]
    [InlineData("x", "int", "r", null, true, "an event at a point gives each field's location")]
    [InlineData("x", "int", "r", "L[1]", false, "an event raised from Lua has no locations")]
    [InlineData("x", "int", "r", "7", true, "'7' is a number, not a cell")]
    [InlineData("x", "int", "r", "LF[1]", true, "'LF[1]' is a float cell, and the field's type is int")]
    [InlineData("x", "string", "r", "current_unit_id", true, "is an integer cell, and the field's type is string")]
    [InlineData("x", "int", "r", "unit_current_levels[9]", true, "field x: 'unit_current_levels[9]': ")]
    public void AFieldThatBreaksARuleSaysWhich(string name, string type, string access, string? at, bool atPoint, string message)
        => Assert.Contains(message, Assert.Throws<FormatException>(() => GameEvent.BuildField(name, type, access, at, atPoint, Layout)).Message);

    [Fact]
    public void ABoolFieldLivesInAnIntegerCell()
    {
        GameEventField field = Field("flag", "bool", "rw", "G[0x201]");
        Assert.Equal((EventFieldType.Bool, true), (field.Type, field.Writable));
        Assert.Equal(CellValue.Integer(1), EventFieldRules.Check(field, CellValue.Integer(1)));
        Assert.Throws<ScriptAccessException>(() => EventFieldRules.Check(field, CellValue.Integer(2)));
    }

    // ------------------------------------------------------------------------------------------- the declarations

    private const string Declaration = """
        [[event]]
        name = "test.award"
        point = "test.point"
        description = "One award."

        [[event.field]]
        name = "amount"
        at = "L[0x1]"
        type = "int"
        access = "rw"

        [[event.field]]
        name = "unit"
        at = "current_unit_id"
        type = "int"

        [[event]]
        name = "test.computed"
        raised_by = "lua"

        [[event.field]]
        name = "amount"
        type = "int"
        access = "rw"
        """;

    [Fact]
    public void AnEventsFileReadsAsItsDeclarationsWithReadOnlyAsTheDefault()
    {
        IReadOnlyList<ModEventDeclaration> events = ManifestReader.ParseEvents(Declaration, "events/test.toml");
        Assert.Equal(["test.award", "test.computed"], events.Select(e => e.Name));
        Assert.Equal(("test.point", "One award.", false), (events[0].Point, events[0].Description, events[0].RaisedByLua));
        Assert.Equal([new ModEventFieldDeclaration("amount", "int", "rw", "L[0x1]"),
                      new ModEventFieldDeclaration("unit", "int", "r", "current_unit_id")], events[0].Fields);
        Assert.True(events[1].RaisedByLua);
    }

    [Theory]
    [InlineData("[[event]]\nname = \"a.b\"\npoint = \"p.q\"\nraised_by = \"lua\"", "not both")]
    [InlineData("[[event]]\nname = \"a.b\"", "an event names its point, or raised_by = \"lua\"")]
    [InlineData("[[event]]\nname = \"a.b\"\nraised_by = \"game\"", "raised_by: 'game' is not \"lua\"")]
    [InlineData("[[event]]\nname = \"nodots\"\nraised_by = \"lua\"", "name: 'nodots' is not a dotted name")]
    [InlineData("[[event]]\nname = \"a.b\"\npoint = \"p\"", "point: 'p' is not a dotted name")]
    [InlineData("[[event]]\nname = \"a.b\"\nraised_by = \"lua\"\nwhen = 1", "event[0].when: unknown key")]
    [InlineData("[[event]]\nname = \"a.b\"\nraised_by = \"lua\"\n[[event.field]]\nname = \"x\"\ntype = \"int\"\nwhere = \"L[1]\"",
                "event[0].field[0].where: unknown key")]
    [InlineData("[[event]]\nname = \"a.b\"\nraised_by = \"lua\"\n[[event.field]]\nname = \"x\"\ntype = \"int\"\n[[event.field]]\nname = \"x\"\ntype = \"int\"",
                "the event has two fields named x")]
    [InlineData("[[event]]\nname = \"a.b\"\nraised_by = \"lua\"\n[[event]]\nname = \"a.b\"\nraised_by = \"lua\"", "declares a.b twice")]
    [InlineData("name = \"a.b\"", "name: unknown key")]
    [InlineData("", "the file declares no event")]
    [InlineData("[[event]\n", "not valid TOML")]
    public void AnEventsFileThatBreaksARuleIsRefused(string text, string message)
        => Assert.Contains(message, Assert.Throws<ModManifestException>(() => ManifestReader.ParseEvents(text, "events/x.toml")).Message);
}
