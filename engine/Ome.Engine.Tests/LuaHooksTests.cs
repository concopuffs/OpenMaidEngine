using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 4 of docs/superpowers/specs/2026-11-10-lua-scripting-design.md, the wider hooks, over synthetic scripts
/// and hand-built stacks (the link's output): site hooks and <c>after</c> (§7.1), <c>skip</c> (§7.1) and their order
/// (§7.4). The link's
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
        Assert.Contains("\"kind\":\"site\",\"when\":\"before\",\"at\":\"ROOT.BIN@0x0\"", native.RecentTrace[0]);
        Assert.Contains("\"kind\":\"site\",\"when\":\"after\",\"at\":\"ROOT.BIN@0x0\"", native.RecentTrace[1]);
        Assert.Contains("\"result\":\"error\"", native.RecentTrace[1]);
        Assert.Contains(native.RecentLog, line => line.Contains("hooks.lua:after failed at ROOT.BIN@0x0 (after)", StringComparison.Ordinal));
    }
}
