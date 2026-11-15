using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 4 of docs/superpowers/specs/2026-11-10-lua-scripting-design.md, the wider hooks, over synthetic scripts
/// and hand-built stacks (the link's output): site hooks and <c>after</c> (§7.1), <c>skip</c> (§7.1), script entry and
/// exit (§7.2), watches (§7.3) and their order (§7.4). A load's and a return to title's causes and records are in
/// <see cref="LuaStateTests"/>. The link's
/// side is in <see cref="ModLuaLinkTests"/>, a load across an <c>after</c> hook in <see cref="LuaStateTests"/>.
/// </summary>
public sealed class LuaHooksTests : IDisposable
{
    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly int Mov = Table.ByLabel("mov")!.Value;
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        foreach (IDisposable item in _owned) item.Dispose();
    }

    private Scripting Native(LuaStackBuilder builder, bool trace = false)
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        var (stack, plan) = builder.Build();
        return new Scripting(new ScriptMemory(), new ScriptingSetup { Runtime = runtime, Stack = stack, Plan = plan, Trace = trace });
    }

    private static VirtualMachine Run(Script script, Scripting native, IInputAndWaits? waits = null,
                                      IScriptProvider? provider = null)
    {
        var vm = new VirtualMachine(script, Table, waits ?? new HeadlessWaits(), provider: provider, scripting: native);
        vm.Run();
        return vm;
    }

    private static Operand G(int address) => new(T_GINT, address);
    /// <summary>The mod's log lines, without the time, the level and the mod.</summary>
    private static string[] Lines(Scripting native, string mod = "mod.a")
        => native.RecentLog.Where(line => line.Contains($" {mod}: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf($" {mod}: ", StringComparison.Ordinal) + mod.Length + 3)..]).ToArray();

    /// <summary>A watch handler that logs each record: the cell, the values, the writer, and where.</summary>
    private const string Logging = """
        -- file: watch.lua
        function on_write(ctx)
            local by = ctx.writer == "script" and (ctx.script .. "@" .. ctx.offset) or (ctx.mod or ctx.writer)
            maid.log.info(string.format("%s 0x%x %s->%s %s", ctx.watch, ctx.address, tostring(ctx.old), tostring(ctx.new), by))
        end
        """;

    private static Operand Imm(long value) => new(T_IMM, value);

    private static Script Assemble(string name, params (int, Operand[])[] code)
        => ScriptAssembler.Assemble(Table, name, code.ToList(), []);

    private static void NoErrors(Scripting native)
        => Assert.True(!native.RecentLog.Any(line => line.Contains(" error ", StringComparison.Ordinal)),
                       string.Join("\n", native.RecentLog));

    // ------------------------------------------------------------------------------------------- sites and after (§7.1)

    [Fact]
    public void ASiteHookRunsBeforeItsInstructionAndAnAfterHookOnceItHasFinished()
    {
        // ROOT: G[0x100] = 5; G[0x101] = G[0x100]; exit. Both hooks are on instruction 1, which is no point.
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x100), Imm(5)]), (Mov, [G(0x101), G(0x100)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function before(ctx)
                    assert(ctx.point == nil and ctx.script == "ROOT.BIN" and ctx.site == ctx.offset and ctx.opcode == 0x55)
                    assert(maid.vm.g[0x101] == 0)
                    maid.vm.g[0x100] = 6
                end
                function after(ctx)
                    assert(ctx.point == nil and ctx.site == ctx.offset and ctx.args[2].value == 6)
                    assert(maid.vm.g[0x101] == 6, "the instruction has run")
                    ctx.args[1].value = 9
                end
                """)
            .Site(root, 1, "mod.a", "hooks.lua:before")
            .Site(root, 1, "mod.a", "hooks.lua:after", "after"));

        VirtualMachine vm = Run(root, native);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(9, vm.Globals[0x101]);   // the after hook's write is the last
        NoErrors(native);
    }

    [Fact]
    public void BeforeAndAfterEachRunInOneListByPriorityThenLoadOrder()
    {
        // Every handler appends its digit to G[0x200]; the instruction copies what the before list left.
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x101), G(0x200)]), (0x2, []));
        static string Append(string name, int digit)
            => $"function {name}(ctx) maid.vm.g[0x200] = maid.vm.g[0x200] * 10 + {digit} end\n";
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", "-- file: a.lua\n" + Append("point", 2) + Append("after", 4))
            .Mod("mod.b", "-- file: b.lua\n" + Append("before", 1) + Append("after", 3))
            .Point(root, 0, "mod.a", "a.lua:point")
            .Site(root, 0, "mod.b", "b.lua:before", priority: -1)
            .Site(root, 0, "mod.a", "a.lua:after", "after", priority: 1)
            .Site(root, 0, "mod.b", "b.lua:after", "after"));

        VirtualMachine vm = Run(root, native);

        // Before: b's site (-1), a's point (0); the copy; after: b (0), a (1).
        Assert.Equal(12, vm.Globals[0x101]);
        Assert.Equal(1234, vm.Globals[0x200]);
        NoErrors(native);
    }

    [Fact]
    public void AnAfterHookOnACallFiresOnceTheCalleeHasReturnedAndNeverOnAnInstructionThatEnds()
    {
        // ROOT calls SUB (id 5), which writes G[0x300] and returns; ROOT then exits.
        Script root = Assemble("ROOT.BIN", (0x3, [Imm(5)]), (Mov, [G(0x302), Imm(1)]), (0x2, []));
        Script sub = Assemble("SUB.BIN", (Mov, [G(0x300), Imm(7)]), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function after_call(ctx)
                    assert(maid.vm.g[0x300] == 7 and maid.vm.g[0x302] == 0, "SUB has returned and ROOT has not gone on")
                    maid.vm.g[0x301] = maid.vm.g[0x301] + 1
                end
                function after_end(ctx) maid.vm.g[0x303] = 1 end
                """)
            .Site(root, 0, "mod.a", "hooks.lua:after_call", "after")
            .Site(sub, 1, "mod.a", "hooks.lua:after_end", "after")
            .Site(root, 2, "mod.a", "hooks.lua:after_end", "after"));

        VirtualMachine vm = Run(root, native, provider: new MapProvider(new() { [5] = sub }));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[0x301]);
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x303));   // the ret and the exit ended their frames
        NoErrors(native);
    }

    /// <summary>A recording host that notes, at each op 0xc8 sleep, how many lines the scripting native had logged.</summary>
    private sealed class MarkedSleep(Func<int> mark) : RecordingWaits
    {
        public readonly List<int> Marks = new();

        public override void Sleep(long duration)
        {
            Marks.Add(mark());
            base.Sleep(duration);
        }
    }

    [Fact]
    public void AWaitsBeforeHooksFireBeforeTheWaitAndItsAfterHooksOnceTheWaitHasEnded()
    {
        // Spec §7.1, gate 8: ROOT sleeps (op 0xc8, which may wait), then exits.
        Script root = Assemble("ROOT.BIN", (0xc8, [Imm(10)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function before(ctx) maid.log.info("before") end
                function after(ctx) maid.log.info("after") end
                """)
            .Site(root, 0, "mod.a", "hooks.lua:before")
            .Site(root, 0, "mod.a", "hooks.lua:after", "after"));
        var waits = new MarkedSleep(() => native.RecentLog.Count);

        VirtualMachine vm = Run(root, native, waits);

        NoErrors(native);
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal([10L], waits.SleptDurations);
        Assert.Equal(["before", "after"], Lines(native));
        int before = native.RecentLog.ToList().FindIndex(line => line.EndsWith(" mod.a: before", StringComparison.Ordinal));
        int after = native.RecentLog.ToList().FindIndex(line => line.EndsWith(" mod.a: after", StringComparison.Ordinal));
        int slept = Assert.Single(waits.Marks);
        Assert.True(before < slept && slept <= after, $"before at {before}, the sleep after {slept} lines, after at {after}");
    }

    [Fact]
    public void AnInstructionThatRaisesAScriptErrorFiresNoAfterHookAndTheCallAroundItStillDoes()
    {
        // OUTER arms a mouse callback and calls INNER, whose op 0xcd dispatches it: a Depth error (another script's
        // callback). 再試行 raises it again, 無視 resumes past it, and the add after it runs three times: its ret pops the
        // return pc each dispatch pushed before its owner check (CallbackOwnershipTests has the native behaviour).
        Script outer = Assemble("OUTER", (0xcc, [Imm(0), Imm(9)]), (0x3, [Imm(5)]), (0x2, []),
                                (Mov, [G(0x120), Imm(1)]), (0x5, []));
        Script inner = Assemble("INNER", (0xcd, []), (0x50, [G(0x121), G(0x121), Imm(1)]), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                local function count(address) maid.vm.g[address] = maid.vm.g[address] + 1 end
                function before_dispatch(ctx) count(0x400) end
                function after_dispatch(ctx) count(0x401) end
                function after_add(ctx) count(0x402) end
                function after_call(ctx) count(0x403) end
                """)
            .Site(inner, 0, "mod.a", "hooks.lua:before_dispatch")
            .Site(inner, 0, "mod.a", "hooks.lua:after_dispatch", "after")
            .Site(inner, 1, "mod.a", "hooks.lua:after_add", "after")
            .Site(outer, 1, "mod.a", "hooks.lua:after_call", "after"));
        var waits = new RecordingWaits();
        waits.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Retry);
        waits.ScriptErrorAnswers.Enqueue(ScriptErrorChoice.Ignore);

        VirtualMachine vm = Run(outer, native, waits, new MapProvider(new() { [5] = inner }));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(2, waits.Diagnostics.Count);
        Assert.Equal(2, vm.Globals[0x400]);                       // 再試行 runs its before hooks again
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x401));     // it never finished
        Assert.Equal((3, 3), (vm.Globals[0x121], vm.Globals[0x402]));
        Assert.Equal(1, vm.Globals[0x403]);                       // the error was INNER's instruction's, not the call's
        NoErrors(native);
    }

    // ------------------------------------------------------------------------------------------- script entry and exit (§7.2)

    [Fact]
    public void EnterAndExitFireForACalledFrameWithItsCauseAndItsLocals()
    {
        // ROOT calls SUB (id 5) twice, then exits; SUB sets its local and returns.
        Script root = Assemble("ROOT.BIN", (0x3, [Imm(5)]), (0x3, [Imm(5)]), (0x2, []));
        Script sub = Assemble("SUB.BIN", (Mov, [new Operand(9, 1), Imm(7)]), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function root_enter(ctx)
                    assert(ctx.script == "ROOT.BIN" and ctx.cause == "root" and ctx.args == nil and ctx.offset == nil)
                    maid.vm.g[0x300] = 1
                end
                function sub_enter(ctx)
                    assert(ctx.script == "SUB.BIN" and ctx.cause == "call")
                    assert(maid.vm.l[1] == 0 and maid.vm.frame.script == "SUB.BIN" and maid.vm.frame.offset == 0)
                    maid.vm.g[0x301] = maid.vm.g[0x301] + 1
                end
                function sub_exit(ctx)
                    assert(ctx.cause == "call" and maid.vm.l[1] == 7, "the returning frame is the running one")
                    maid.vm.g[0x302] = maid.vm.g[0x302] * 10 + 1
                end
                function sub_exit_later(ctx) maid.vm.g[0x302] = maid.vm.g[0x302] * 10 + 2 end
                function root_exit(ctx) maid.vm.g[0x303] = 1 end
                """)
            .ScriptHook("ROOT.BIN", "enter", "mod.a", "hooks.lua:root_enter")
            .ScriptHook("SUB.BIN", "enter", "mod.a", "hooks.lua:sub_enter")
            .ScriptHook("SUB.BIN", "exit", "mod.a", "hooks.lua:sub_exit_later", priority: 1)
            .ScriptHook("SUB.BIN", "exit", "mod.a", "hooks.lua:sub_exit")
            .ScriptHook("ROOT.BIN", "exit", "mod.a", "hooks.lua:root_exit"), trace: true);

        VirtualMachine vm = Run(root, native, provider: new MapProvider(new() { [5] = sub }));

        NoErrors(native);
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((1, 2, 1212), (vm.Globals[0x300], vm.Globals[0x301], vm.Globals[0x302]));
        Assert.Equal(1, vm.Globals[0x303]);   // op 0x2 returns ROOT's frame too, and then the run ends
        Assert.Contains("\"kind\":\"script\",\"when\":\"enter\",\"cause\":\"call\",\"at\":\"SUB.BIN\"",
                        native.RecentTrace[1]);
    }

    // ------------------------------------------------------------------------------------------- watches (§7.3)

    [Fact]
    public void AWatchFiresOncePerWriteAfterTheInstructionAndItsAfterHooksWithItsHooksWritesFirst()
    {
        // ROOT writes G[0x100] = 5 twice (one value, two writes) and exits; instruction 0 has a before hook that writes
        // the cell first and an after hook that logs.
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x100), Imm(5)]), (Mov, [G(0x100), Imm(5)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", Logging + "\n" + """
                -- file: hooks.lua
                function before(ctx) maid.vm.g[0x100] = 1 end
                function after(ctx) maid.log.info("after") end
                function check(ctx)
                    assert(ctx.name == nil and ctx.index == nil and ctx.column == nil and ctx.watch == "G[0x100]")
                    if ctx.writer == "script" then
                        assert(ctx.script == "ROOT.BIN" and ctx.site == ctx.offset and ctx.mod == nil)
                    else
                        assert(ctx.writer == "mod" and ctx.mod == "mod.a" and ctx.script == nil and ctx.offset == nil)
                    end
                end
                """)
            .Site(root, 0, "mod.a", "hooks.lua:before")
            .Site(root, 0, "mod.a", "hooks.lua:after", "after")
            .Watch("G[0x100]", 0x100, "mod.a", "watch.lua:on_write")
            .Watch("G[0x100]", 0x100, "mod.a", "hooks.lua:check"));

        VirtualMachine vm = Run(root, native);

        NoErrors(native);
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(["after", "G[0x100] 0x100 0->1 mod.a", "G[0x100] 0x100 1->5 ROOT.BIN@0",
                      "G[0x100] 0x100 5->5 ROOT.BIN@5"], Lines(native));
    }

    [Fact]
    public void AnInnerInstructionDeliversItsOwnRecordsAndLeavesTheOuterOnesForTheOutersReturn()
    {
        // ROOT's call has a before hook that writes G[0x200]; SUB writes G[0x201] and returns, and its exit hook writes
        // G[0x202]. SUB's own instruction delivers its record when it returns; the call's two wait for the call.
        Script root = Assemble("ROOT.BIN", (0x3, [Imm(5)]), (0x2, []));
        Script sub = Assemble("SUB.BIN", (Mov, [G(0x201), Imm(7)]), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", Logging + "\n" + """
                -- file: hooks.lua
                function before_call(ctx) maid.vm.g[0x200] = 1 end
                function sub_exit(ctx) maid.vm.g[0x202] = 2 end
                function outer_record(ctx) assert(maid.vm.g[0x201] == 7, "SUB has run") end
                """)
            .Site(root, 0, "mod.a", "hooks.lua:before_call")
            .ScriptHook("SUB.BIN", "exit", "mod.a", "hooks.lua:sub_exit")
            .Watch("G[0x200]", 0x200, "mod.a", "watch.lua:on_write")
            .Watch("G[0x200]", 0x200, "mod.a", "hooks.lua:outer_record")
            .Watch("G[0x201]", 0x201, "mod.a", "watch.lua:on_write")
            .Watch("G[0x202]", 0x202, "mod.a", "watch.lua:on_write"));

        Run(root, native, provider: new MapProvider(new() { [5] = sub }));

        NoErrors(native);
        Assert.Equal(["G[0x201] 0x201 0->7 SUB.BIN@0", "G[0x200] 0x200 0->1 mod.a", "G[0x202] 0x202 0->2 mod.a"],
                     Lines(native));
    }

    [Fact]
    public void LuaWritesOutsideAnyInstructionGoWithTheNextInstructionsRecords()
    {
        // init.lua, a session start handler and a console chunk write before the first instruction; each record is
        // delivered once that instruction has returned, after its own.
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x103), Imm(3)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", Logging + "\n" + """
                -- file: init.lua
                maid.vm.g[0x100] = 1
                -- file: hooks.lua
                function on_start(e) maid.vm.g[0x101] = 2 end
                function check(ctx) assert(maid.vm.g[0x103] == 3, "the first instruction has run") end
                """)
            .Engine(Scripting.SessionStartEvent, "mod.a", "hooks.lua:on_start")
            .Watch("G[0x100..]", 0x100, "mod.a", "watch.lua:on_write", count: 4)
            .Watch("G[0x100..]", 0x100, "mod.a", "hooks.lua:check", count: 4));
        native.SubmitConsole(null, "maid.vm.g[0x102] = 4");

        Run(root, native);

        NoErrors(native);
        Assert.Equal(["G[0x100..] 0x100 0->1 mod.a", "G[0x100..] 0x101 0->2 mod.a", "G[0x100..] 0x102 0->4 console",
                      "G[0x100..] 0x103 0->3 ROOT.BIN@0"], Lines(native));
    }

    [Fact]
    public void AWatchHandlersWriteIsDeliveredAfterTheHandlersDueAndTheNestingCapEndsALoop()
    {
        // The before hook writes G[0x101], the instruction G[0x100]; the handler on G[0x101] writes G[0x102], delivered
        // after G[0x100]'s. The handler on G[0x110] writes its own cell again, which comes back to it a delivery deeper
        // until the cap (32) refuses it.
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x100), Imm(1)]), (Mov, [G(0x110), Imm(1)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", Logging + "\n" + """
                -- file: hooks.lua
                function before(ctx) maid.vm.g[0x101] = 1 end
                function chain(ctx) if ctx.address == 0x101 then maid.vm.g[0x102] = 1 end end
                function again(ctx) maid.vm.g[0x110] = ctx.new + 1 end
                """)
            .Site(root, 0, "mod.a", "hooks.lua:before")
            .Watch("G[0x100..]", 0x100, "mod.a", "watch.lua:on_write", count: 3)
            .Watch("G[0x100..]", 0x100, "mod.a", "hooks.lua:chain", count: 3)
            .Watch("G[0x110]", 0x110, "mod.a", "hooks.lua:again"));

        VirtualMachine vm = Run(root, native);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(["G[0x100..] 0x101 0->1 mod.a", "G[0x100..] 0x100 0->1 ROOT.BIN@0", "G[0x100..] 0x102 0->1 mod.a"],
                     Lines(native).Where(line => line.StartsWith("G[", StringComparison.Ordinal)));
        Assert.Equal(33, vm.Globals[0x110]);   // the instruction's 1, then 32 handler calls
        Assert.Contains(native.RecentLog, line => line.Contains("hooks.lua:again nested too deep at watch G[0x110]: watch "
                                                                + "handlers' writes nest 32 deep, the cap is 32", StringComparison.Ordinal));
    }

    [Fact]
    public void AWatchOnANameGivesTheRowAndColumnAndTwoWatchesOnACellRunInOneOrder()
    {
        // A 2-D table of 2 rows of 3 at G[0x500], and an address watch on one of its cells from a second mod. The mod
        // with the name watch has no vm.
        var layout = GameLayout.Parse("""
            {"layout": 1, "profile": "test", "globals": [
              {"name": "grid", "bank": "G", "address": "0x500", "dims": [2, 3]}]}
            """);
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x505), Imm(9)]), (Mov, [G(0x501), Imm(4)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Base("test-base", layout)
            .Mod("mod.a", """
                -- file: watch.lua
                function on_grid(ctx)
                    maid.log.info(string.format("a %s %s[%d][%d] %d", ctx.watch, ctx.name, ctx.index, ctx.column, ctx.new))
                end
                """, vm: false, requires: ["test-base"])
            .Mod("mod.b", """
                -- file: watch.lua
                function on_cell(ctx)
                    maid.log.info(string.format("b %s %s[%d][%d] %d", ctx.watch, ctx.name, ctx.index, ctx.column, ctx.new))
                end
                """)
            .Watch("grid[*]", 0x500, "mod.a", "watch.lua:on_grid", count: 6, global: "grid", dimensions: 2, rowWidth: 3)
            .Watch("G[0x505]", 0x505, "mod.b", "watch.lua:on_cell", priority: -1));

        Run(root, native);

        NoErrors(native);
        Assert.Equal(["b G[0x505] grid[1][2] 9", "a grid[*] grid[1][2] 9", "a grid[*] grid[0][1] 4"],
                     native.RecentLog.Where(entry => entry.Contains(" mod.a: ", StringComparison.Ordinal)
                                                     || entry.Contains(" mod.b: ", StringComparison.Ordinal))
                         .Select(entry => entry[(entry.IndexOf(": ", 20, StringComparison.Ordinal) + 2)..]));
    }

    [Fact]
    public void AStringWatchGivesTheTextsAndTheTraceNamesTheCellAndTheWriter()
    {
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x100), Imm(1)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function before(ctx) maid.vm.gs[0x40] = "after" end
                function on_text(ctx)
                    assert(ctx.old == "" and ctx.new == "after" and ctx.writer == "mod", "the texts")
                    error("on purpose")
                end
                """)
            .Site(root, 0, "mod.a", "hooks.lua:before")
            .Watch("GS[0x40]", 0x40, "mod.a", "hooks.lua:on_text", kind: CellKind.String), trace: true);

        Run(root, native);

        string line = native.RecentTrace[1];
        Assert.Contains("\"hook\":\"GS[0x40]\",\"kind\":\"watch\",\"cell\":\"GS[0x40]\",\"old\":\"\",\"new\":\"after\","
                        + "\"writer\":\"mod\",\"by\":\"mod.a\"", line);
        Assert.Contains("\"result\":\"error\"", line);
        Assert.Contains(native.RecentLog, entry => entry.Contains("hooks.lua:on_text failed at watch GS[0x40]: ", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------- skip (§7.1)

    [Fact]
    public void ASkipPassesOverTheInstructionAndEveryHandlerBeforeItStillRuns()
    {
        // ROOT: G[0x100] = 5 (skipped); G[0x101] = G[0x100]; exit.
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x100), Imm(5)]), (Mov, [G(0x101), G(0x100)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: a.lua
                function skips(ctx)
                    assert(not ctx.skipped)
                    ctx:skip()
                    ctx:skip()   -- a second skip changes nothing
                    assert(ctx.skipped)
                end
                """)
            .Mod("mod.b", """
                -- file: b.lua
                function later(ctx)
                    assert(ctx.skipped, "a handler before this one skipped")
                    local ok, e = pcall(ctx.skip, ctx)
                    assert(not ok and tostring(e):find("did not declare skip = true"), tostring(e))
                    ok, e = pcall(ctx.skip)
                    assert(not ok and tostring(e):find("call it with a colon"), tostring(e))
                    maid.vm.g[0x200] = 1
                end
                function after(ctx) maid.vm.g[0x201] = 1 end
                function after_next(ctx)
                    local ok, e = pcall(ctx.skip, ctx)
                    assert(not ok and tostring(e):find("only a hook before it may skip it"), tostring(e))
                    assert(ctx.skipped == false)
                    maid.vm.g[0x202] = 1
                end
                """)
            .Point(root, 0, "mod.a", "a.lua:skips", skip: true)
            .Site(root, 0, "mod.b", "b.lua:later", priority: 1)
            .Site(root, 0, "mod.b", "b.lua:after", "after")
            .Site(root, 1, "mod.b", "b.lua:after_next", "after"), trace: true);

        VirtualMachine vm = Run(root, native);

        Assert.Equal("exit", vm.HaltReason);
        NoErrors(native);
        Assert.Equal((0, 0), (vm.Globals.GetValueOrDefault(0x100), vm.Globals.GetValueOrDefault(0x101)));   // wrote nothing
        Assert.Equal((1, 0, 1), (vm.Globals[0x200], vm.Globals.GetValueOrDefault(0x201), vm.Globals[0x202]));   // no after
        NoErrors(native);
        // The trace names the handler that skipped, and only it.
        Assert.Contains("\"skip\":true", Assert.Single(native.RecentTrace, line => line.Contains("a.lua:skips")));
        Assert.Single(native.RecentTrace, line => line.Contains("\"skip\":true"));
    }

    [Fact]
    public void TheTraceNamesASiteHookAndWhenItRanAndAnErrorSaysAfter()
    {
        Script root = Assemble("ROOT.BIN", (Mov, [G(0x100), Imm(5)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function before(ctx) end
                function after(ctx) error("after went wrong") end
                """)
            .Site(root, 0, "mod.a", "hooks.lua:before")
            .Site(root, 0, "mod.a", "hooks.lua:after", "after"), trace: true);

        Run(root, native);

        Assert.Equal(2, native.RecentTrace.Count);
        Assert.Contains("\"hook\":\"ROOT.BIN@0x0\",\"kind\":\"site\",\"when\":\"before\",\"at\":\"ROOT.BIN@0x0\"", native.RecentTrace[0]);
        Assert.Contains("\"kind\":\"site\",\"when\":\"after\",\"at\":\"ROOT.BIN@0x0\"", native.RecentTrace[1]);
        Assert.Contains("\"result\":\"error\"", native.RecentTrace[1]);
        Assert.Contains(native.RecentLog, line => line.Contains("hooks.lua:after failed at ROOT.BIN@0x0 (after)", StringComparison.Ordinal));
    }
}
