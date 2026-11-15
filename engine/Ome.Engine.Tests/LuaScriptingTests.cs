using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 1 of docs/superpowers/specs/2026-11-10-lua-scripting-design.md: the scripting native, the runtime seam and
/// the Lua runtime over synthetic scripts and hand-built stacks (the link's output). Gates 1, 10 and 12, and the
/// session and point rules of §5.7 and §7.1. The link's own gates are in <see cref="ModLuaLinkTests"/>.
/// </summary>
public sealed class LuaScriptingTests : IDisposable
{
    private const int T_IMM = 0, T_STR = 2, T_GINT = 3, T_GFLOAT = 4, T_GSTR = 5, T_LINT = 9;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        foreach (IDisposable item in _owned) item.Dispose();
    }

    /// <summary>A stack and its hook plan, as the link would leave them.</summary>
    private sealed class StackBuilder
    {
        private readonly List<ModRuntimeMod> _mods = new();
        private readonly List<ModRuntimeHandler> _handlers = new();
        private readonly Dictionary<string, List<PlannedSite>> _sites = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<PlannedHandler>> _engine = new(StringComparer.Ordinal);

        public StackBuilder Mod(string id, string files, bool vm = true, bool library = false, params string[] requires)
            => Mod(id, Files(files), vm, library, requires);

        /// <summary>Files as <c>-- file: name.lua</c> sections of one text.</summary>
        private static IReadOnlyList<(string, string)> Files(string text)
        {
            var files = new List<(string, string)>();
            string? name = null;
            var body = new System.Text.StringBuilder();
            foreach (string line in text.Split('\n'))
            {
                if (line.TrimStart().StartsWith("-- file: ", StringComparison.Ordinal))
                {
                    if (name != null) files.Add((name, body.ToString()));
                    name = line.Trim()["-- file: ".Length..];
                    body.Clear();
                }
                else body.Append(line).Append('\n');
            }
            if (name != null) files.Add((name, body.ToString()));
            return files;
        }

        private StackBuilder Mod(string id, IReadOnlyList<(string Path, string Text)> files, bool vm, bool library, string[] requires)
        {
            _mods.Add(new ModRuntimeMod(id, "1.0.0", library, vm, requires,
                files.Select(file => new ModSource(id, file.Path, file.Text)).ToList()));
            return this;
        }

        private HandlerId Handler(string mod, string handler)
        {
            // One id per (mod, file, function), as the link gives it.
            int colon = handler.IndexOf(':');
            var entry = new ModRuntimeHandler(mod, handler[..colon], handler[(colon + 1)..]);
            int found = _handlers.IndexOf(entry);
            if (found >= 0) return new HandlerId(found);
            _handlers.Add(entry);
            return new HandlerId(_handlers.Count - 1);
        }

        private int LoadOrder(string mod) => _mods.FindIndex(entry => entry.Id == mod);

        public StackBuilder Point(Script script, int index, string mod, string handler, int priority = 0, string point = "test.point")
        {
            HandlerId id = Handler(mod, handler);
            int offset = script.Instructions[index].Offset;
            if (!_sites.TryGetValue(script.Name, out var sites)) _sites[script.Name] = sites = new();
            int at = sites.FindIndex(site => site.Offset == offset);
            var planned = new PlannedHandler(id, priority, LoadOrder(mod));
            if (at < 0) sites.Add(new PlannedSite(offset, offset, point, [planned]));
            else sites[at] = sites[at] with
            {
                Handlers = sites[at].Handlers.Append(planned).OrderBy(h => h.Priority).ThenBy(h => h.LoadOrder).ToList(),
            };
            return this;
        }

        public StackBuilder Engine(string name, string mod, string handler, int priority = 0)
        {
            HandlerId id = Handler(mod, handler);
            if (!_engine.TryGetValue(name, out var list)) _engine[name] = list = new();
            list.Add(new PlannedHandler(id, priority, LoadOrder(mod)));
            return this;
        }

        public (ModRuntimeStack Stack, HookPlan Plan) Build()
            => (new ModRuntimeStack(_mods, _handlers),
                new HookPlan(_sites.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlannedSite>)pair.Value),
                             _engine.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlannedHandler>)pair.Value)));
    }

    private LazyModRuntime Runtime()
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        return runtime;
    }

    private Scripting Native(StackBuilder builder, LazyModRuntime? runtime = null, LuaErrorPolicy errors = LuaErrorPolicy.Continue,
                             ModLimits? limits = null, string? diagnostics = null)
    {
        var (stack, plan) = builder.Build();
        return new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = runtime ?? Runtime(), Stack = stack, Plan = plan, Errors = errors,
            Limits = limits ?? ModLimits.Default, DiagnosticsDirectory = diagnostics,
        });
    }

    private static VirtualMachine Run(Script script, Scripting native, IScriptProvider? provider = null)
    {
        var vm = new VirtualMachine(script, Table, new HeadlessWaits(), provider: provider, scripting: native);
        vm.Run();
        return vm;
    }

    private static Operand G(int address) => new(T_GINT, address);
    private static Operand L(int index) => new(T_LINT, index);
    private static Operand Imm(long value) => new(T_IMM, value);

    /// <summary>ROOT: G[0x100] = 5; G[0x101] = G[0x100]; exit. Instruction 1 is the one the tests hook.</summary>
    private static Script Copy() => ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
    {
        (0x55, [G(0x100), Imm(5)]),
        (0x55, [G(0x101), G(0x100)]),
        (0x2, []),
    }, []);

    private static string Chunk(Scripting native, string? mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        Assert.True(request.Done);
        Assert.True(request.Result!.Succeeded, request.Result.Text);
        return request.Result.Text;
    }

    private static ConsoleResult TryChunk(Scripting native, string? mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        return request.Result!;
    }

    // ------------------------------------------------------------------------------------------- points (§7.1)

    [Fact]
    public void APointHandlerRunsBeforeItsInstructionAndCanWriteItsCells()
    {
        Script root = Copy();
        Scripting native = Native(new StackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function on_point(ctx)
                    assert(ctx.script == "ROOT.BIN" and ctx.point == "test.point" and ctx.opcode == 0x55)
                    assert(ctx.args[2].value == 5 and ctx.args[2].kind == "integer" and ctx.args[2].writable)
                    maid.vm.g[0x100] = 7
                end
                """)
            .Point(root, 1, "mod.a", "hooks.lua:on_point"));

        VirtualMachine vm = Run(root, native);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(7, vm.Globals[0x101]);
        Assert.DoesNotContain(native.RecentLog, line => line.Contains(" error "));
    }

    [Fact]
    public void AHandlerWritesAnOperandAndTheFramesLocals()
    {
        // ROOT: L[1] = 3; G[0x100] = L[1]; G[0x101] = 0x10 (an immediate, which no write changes); exit.
        Script root = ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
        {
            (0x55, [L(1), Imm(3)]),
            (0x55, [G(0x100), L(1)]),
            (0x55, [G(0x101), Imm(0x10)]),
            (0x2, []),
        }, []);
        Scripting native = Native(new StackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function double_local(ctx)
                    maid.vm.l[1] = maid.vm.l[1] * 2
                    local frame = maid.vm.frame
                    assert(frame.script == "ROOT.BIN" and frame.offset == ctx.offset and frame.site == ctx.site and frame.depth == 1)
                    -- spec §8.1 writes the functions with a dot; a colon works too.
                    local frames = maid.vm.frames()
                    assert(#frames == 1 and frames[1].script == "ROOT.BIN" and #maid.vm:frames() == 1)
                    assert(maid.vm.script_id("ROOT") == maid.vm:script_id("ROOT"))
                    local ok, e = pcall(maid.vm.call, "ROOT")
                    assert(not ok and tostring(e):find("not available"), tostring(e))
                end
                function write_immediate(ctx)
                    local ok, e = pcall(function() ctx.args[2].value = 1 end)
                    assert(not ok and tostring(e):find("an immediate"), tostring(e))
                    ctx.args[1].value = 0x20
                end
                """)
            .Point(root, 1, "mod.a", "hooks.lua:double_local")
            .Point(root, 2, "mod.a", "hooks.lua:write_immediate"));

        VirtualMachine vm = Run(root, native);

        Assert.Equal(6, vm.Globals[0x100]);
        // The handler wrote G[0x101] through the operand, then the instruction wrote its own value over it.
        Assert.Equal(0x10, vm.Globals[0x101]);
        Assert.DoesNotContain(native.RecentLog, line => line.Contains(" error "));
    }

    [Fact]
    public void HandlersAtOneInstructionRunByPriorityThenLoadOrder()
    {
        Script root = Copy();
        Scripting native = Native(new StackBuilder()
            .Mod("mod.a", "-- file: a.lua\nfunction a(ctx) maid.vm.g[0x100] = maid.vm.g[0x100] * 10 + 1 end")
            .Mod("mod.b", "-- file: b.lua\nfunction b(ctx) maid.vm.g[0x100] = maid.vm.g[0x100] * 10 + 2 end")
            .Point(root, 1, "mod.b", "b.lua:b", priority: 0)
            .Point(root, 1, "mod.a", "a.lua:a", priority: 0)
            .Point(root, 1, "mod.b", "b.lua:b", priority: -1));

        VirtualMachine vm = Run(root, native);

        // b at priority -1, then a (load order 0), then b (load order 1): 5 -> 52 -> 521 -> 5212.
        Assert.Equal(5212, vm.Globals[0x101]);
    }

    // ------------------------------------------------------------------------------------------- session (§5.7)

    [Fact]
    public void TheSessionIsBuiltBeforeTheFirstFrameAndRebuiltAfterAReturnToTitle()
    {
        // ROOT1 returns to title (op 0x9); the reloaded root, ROOT2, exits.
        Script first = ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
        {
            (0x55, [G(0x100), Imm(1)]),
            (0x9, []),
        }, []);
        Script second = ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
        {
            (0x55, [G(0x100), Imm(2)]),
            (0x2, []),
        }, []);
        var builder = new StackBuilder()
            .Mod("mod.a", """
                -- file: init.lua
                order = {}
                -- file: hooks.lua
                function on_start(e)
                    maid.log.info("start " .. e.reason .. " " .. tostring(seen) .. " " .. maid.session.reason)
                    seen = true
                    order[#order + 1] = "start"
                end
                function on_point(ctx)
                    order[#order + 1] = "point"
                    maid.log.info("order " .. table.concat(order, ","))
                end
                """)
            .Engine(Scripting.SessionStartEvent, "mod.a", "hooks.lua:on_start")
            // One point: sites are keyed by script name, so ROOT.BIN@0 is hooked in both roots.
            .Point(first, 0, "mod.a", "hooks.lua:on_point");
        Scripting native = Native(builder);

        VirtualMachine vm = Run(first, native, new MapProvider(new Dictionary<long, Script> { [0] = second }));

        Assert.Equal("exit", vm.HaltReason);
        string[] lines = native.RecentLog.Select(line => line[(line.IndexOf("mod.a: ", StringComparison.Ordinal) + 7)..]).ToArray();
        // A Lua global set in the boot session is gone in the title session: the session was rebuilt.
        Assert.Equal(["start boot nil boot", "order start,point", "start title nil title", "order start,point"], lines);
    }

    [Fact]
    public void OffMeansOff_NoLuaModCreatesNoRuntime()
    {
        LazyModRuntime runtime = Runtime();
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup { Runtime = runtime });

        VirtualMachine vm = Run(Copy(), native);
        var plain = new VirtualMachine(Copy(), Table, new HeadlessWaits());
        plain.Run();

        Assert.False(runtime.Created);
        Assert.False(native.HasSession);
        Assert.Equal(plain.HaltReason, vm.HaltReason);
        Assert.Equal(plain.Globals.OrderBy(pair => pair.Key), vm.Globals.OrderBy(pair => pair.Key));
        Assert.Null(native.HooksFor(Copy()));
    }

    // ------------------------------------------------------------------------------------------- gate 10: errors

    [Fact]
    public void ARunawayHandlerIsStoppedByItsBudgetAndTheGameGoesOn()
    {
        Script root = Copy();
        Scripting native = Native(new StackBuilder()
                .Mod("mod.a", "-- file: hooks.lua\nfunction spin(ctx) while true do pcall(function() while true do end end) end end")
                .Point(root, 1, "mod.a", "hooks.lua:spin"),
            limits: new ModLimits { HandlerInstructions = 200_000 });

        VirtualMachine vm = Run(root, native);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(5, vm.Globals[0x101]);
        Assert.Contains(native.RecentLog, line => line.Contains("mod.a: hooks.lua:spin ran out of its instruction budget at ROOT.BIN@0x")
                                                   && line.Contains("200,000"));
        Assert.Equal(1, native.NoticeCount);
    }

    [Fact]
    public void UnboundedRecursionEndsAsAnErrorThatNamesModHandlerScriptAndOffset()
    {
        Script root = Copy();
        Scripting native = Native(new StackBuilder()
            .Mod("mod.a", "-- file: hooks.lua\nlocal function f() return f() + 1 end\nfunction deep(ctx) f() end")
            .Point(root, 1, "mod.a", "hooks.lua:deep"));

        VirtualMachine vm = Run(root, native);

        Assert.Equal("exit", vm.HaltReason);
        string line = Assert.Single(native.RecentLog, entry => entry.Contains(" error "));
        Assert.Contains($"mod.a: hooks.lua:deep failed at ROOT.BIN@0x{root.Instructions[1].Offset:x}", line);
        Assert.Contains("stack overflow", line, StringComparison.OrdinalIgnoreCase);
        // The state still works after.
        Assert.Equal("2", Chunk(native, "mod.a", "return 1 + 1"));
    }

    [Fact]
    public void LuaErrorsHaltEndsTheRunBeforeTheInstruction()
    {
        Script root = Copy();
        Scripting native = Native(new StackBuilder()
                .Mod("mod.a", "-- file: hooks.lua\nfunction fail(ctx) error('boom') end")
                .Point(root, 1, "mod.a", "hooks.lua:fail"),
            errors: LuaErrorPolicy.Halt);

        VirtualMachine vm = Run(root, native);

        Assert.StartsWith($"lua-error: mod.a hooks.lua:fail at ROOT.BIN@0x{root.Instructions[1].Offset:x}: ", vm.HaltReason);
        Assert.Contains("boom", vm.HaltReason);
        Assert.False(vm.Globals.ContainsKey(0x101));
    }

    [Fact]
    public void AHandlerThatFailsThreeTimesInARowIsSwitchedOff()
    {
        // Four copies, each hooked by the same failing handler.
        Script root = ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
        {
            (0x55, [G(0x100), Imm(1)]), (0x55, [G(0x101), Imm(1)]), (0x55, [G(0x102), Imm(1)]), (0x55, [G(0x103), Imm(1)]),
            (0x2, []),
        }, []);
        var builder = new StackBuilder().Mod("mod.a", "-- file: hooks.lua\nfunction fail(ctx) calls = (calls or 0) + 1 error('no') end");
        for (int i = 0; i < 4; i++) builder.Point(root, i, "mod.a", "hooks.lua:fail");
        Scripting native = Native(builder);

        VirtualMachine vm = Run(root, native);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(3, native.RecentLog.Count(line => line.Contains("hooks.lua:fail failed")));
        Assert.Equal("3", Chunk(native, "mod.a", "return calls"));
        Assert.Contains("failed 3 times in a row and is off for this session", native.LatestNotice);
    }

    [Fact]
    public void AModWhoseInitFailsIsOffForTheSessionAndSoIsAModThatRequiresIt()
    {
        Script root = Copy();
        var builder = new StackBuilder()
            .Mod("mod.base", "-- file: init.lua\nerror('broken base')", library: true)
            .Mod("mod.dependent", "-- file: hooks.lua\nfunction on_point(ctx) maid.vm.g[0x100] = 9 end", requires: ["mod.base"])
            .Mod("mod.other", "-- file: hooks.lua\nfunction on_point(ctx) maid.vm.g[0x100] = maid.vm.g[0x100] + 1 end")
            .Point(root, 1, "mod.dependent", "hooks.lua:on_point")
            .Point(root, 1, "mod.other", "hooks.lua:on_point");
        Scripting native = Native(builder);

        VirtualMachine vm = Run(root, native);

        Assert.Equal(6, vm.Globals[0x101]);   // only mod.other ran
        Assert.Contains(native.RecentLog, line => line.Contains("mod.base: off for this session") && line.Contains("broken base"));
        Assert.Contains(native.RecentLog, line => line.Contains("mod.dependent: off for this session: it requires mod.base"));
        Assert.False(TryChunk(native, "mod.base", "return 1").Succeeded);

        Scripting halting = Native(builder, errors: LuaErrorPolicy.Halt);
        VirtualMachine halted = Run(Copy(), halting);
        Assert.StartsWith("lua-build-error: mod.base: ", halted.HaltReason);
        Assert.False(halted.Globals.ContainsKey(0x100));
    }

    [Fact]
    public void AHandlerThatIsNotAFunctionOnceItsFileRanSwitchesTheModOff()
    {
        Script root = Copy();
        Scripting native = Native(new StackBuilder()
            .Mod("mod.a", "-- file: hooks.lua\nfunction on_point(ctx) end\non_point = 3")
            .Point(root, 1, "mod.a", "hooks.lua:on_point"));

        Run(root, native);

        Assert.Contains(native.RecentLog, line => line.Contains("hooks.lua:on_point is not a function once its file has run"));
    }

    [Fact]
    public void ABudgetBoundsAModsStartUpCode()
    {
        Scripting native = Native(new StackBuilder().Mod("mod.a", "-- file: init.lua\nwhile true do end"),
            limits: new ModLimits { StartupInstructions = 100_000 });

        Run(Copy(), native);

        Assert.Contains(native.RecentLog, line => line.Contains("mod.a: off for this session: the mod's start-up code ran past its budget of 100,000"));
    }

    [Fact]
    public void LocalsAreAnErrorInAnEngineEvent()
    {
        Scripting native = Native(new StackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function on_start(e)
                    local ok, message = pcall(function() return maid.vm.l[1] end)
                    maid.log.info(tostring(ok) .. " " .. tostring(message) .. " " .. tostring(maid.vm.frame))
                end
                """)
            .Engine(Scripting.SessionStartEvent, "mod.a", "hooks.lua:on_start"));

        Run(Copy(), native);

        Assert.Contains(native.RecentLog, line => line.Contains("false") && line.Contains("locals exist only in a hook with a frame")
                                                   && line.EndsWith(" nil", StringComparison.Ordinal));
    }

    [Fact]
    public void WritesKeepTheCellRules()
    {
        Scripting native = Native(new StackBuilder().Mod("mod.a", "-- file: init.lua\n"));
        Run(Copy(), native);

        string Try(string code) => Chunk(native, null, $"local ok, e = pcall(function() {code} end) return tostring(ok) .. ' ' .. tostring(e)");

        Assert.StartsWith("false", Try("maid.vm.g[1] = 2^32"));
        Assert.StartsWith("false", Try("maid.vm.g[1] = 1.5"));
        Assert.StartsWith("false", Try("maid.vm.g[-1] = 1"));
        Assert.StartsWith("false", Try("maid.vm.g[1] = 'x'"));
        Assert.StartsWith("false", Try("maid.vm.gs[1] = '\U0001F600'"));
        Assert.StartsWith("false", Try("maid.vm.gp[1] = 1"));
        Assert.Equal("-1", Chunk(native, null, "maid.vm.g[1] = 4294967295 return maid.vm.g[1]"));
        Assert.Equal("0.10000000149011612", Chunk(native, null, "maid.vm.gf[1] = 0.1 return maid.vm.gf[1]"));
        Assert.Equal("姫狩り", Chunk(native, null, "maid.vm.gs[1] = '姫狩り' return maid.vm.gs[1]"));
        Assert.Equal("3\t4\t0", Chunk(native, null, "maid.vm.g[10] = 3 maid.vm.g[11] = 4 local r = maid.vm.g:range(10, 3) return r[1], r[2], r[3]"));
    }

    // ------------------------------------------------------------------------------------------- gate 12: capability

    [Fact]
    public void MaidVmIsAbsentInAModThatDidNotDeclareIt()
    {
        Scripting native = Native(new StackBuilder()
            .Mod("mod.plain", "-- file: init.lua\n", vm: false)
            .Mod("mod.raw", "-- file: init.lua\n"));
        Run(Copy(), native);

        Assert.Equal("true", Chunk(native, "mod.plain", "return maid.vm == nil"));
        Assert.Equal("true", Chunk(native, "mod.raw", "return maid.vm ~= nil"));
        Assert.Equal("mod.plain\t1.0.0", Chunk(native, "mod.plain", "return maid.mod.id, maid.mod.version"));
    }

    [Fact]
    public void AModCannotChangeALibraryFunctionTheStringMethodsOrAnExportForAnother()
    {
        Scripting native = Native(new StackBuilder()
            .Mod("mod.lib", "-- file: init.lua\nreturn { list = {1, 2}, f = function() return 3 end }", vm: false, library: true)
            .Mod("mod.a", """
                -- file: init.lua
                string.upper = function() return "x" end
                math.floor = nil
                table.insert = nil
                """, vm: false)
            .Mod("mod.b", "-- file: init.lua\nlib = require('mod.lib')", vm: false, requires: ["mod.lib"])
            .Mod("mod.c", "-- file: init.lua\nlib = require('mod.lib')", vm: false, requires: ["mod.lib"]));
        Run(Copy(), native);

        Assert.Equal("A\t1\ttrue", Chunk(native, "mod.b", "return string.upper('a'), math.floor(1.5), table.insert ~= nil"));
        Assert.Equal("x\tA", Chunk(native, "mod.a", "return string.upper('a'), ('a'):upper()"));
        Assert.Equal("false\tfalse", Chunk(native, "mod.b", "return getmetatable(''), (pcall(setmetatable, '', {}))"));
        Assert.Equal("false\tfalse\tfalse\tfalse\t1\t3\t2\tlocked",
            Chunk(native, "mod.b", """
                local ok1 = pcall(function() lib.x = 1 end)
                local ok2 = pcall(function() lib.list[1] = 9 end)
                local ok3 = pcall(rawset, lib, "x", 1)
                local ok4 = pcall(rawset, lib.list, 1, 9)
                return ok1, ok2, ok3, ok4, lib.list[1], lib.f(), #lib.list, getmetatable(lib)
                """));
        Assert.Equal("1,2", Chunk(native, "mod.c", "local t = {} for _, v in ipairs(lib.list) do t[#t + 1] = v end return table.concat(t, ',')"));
        Assert.Equal("2", Chunk(native, "mod.c", "local n = 0 for k in pairs(lib) do n = n + 1 end return n"));
    }

    [Fact]
    public void TheSandboxHasNoFilesProcessesClockOrLoader()
    {
        Scripting native = Native(new StackBuilder().Mod("mod.a", "-- file: init.lua\n"));
        Run(Copy(), native);

        Assert.Equal(string.Join("\t", Enumerable.Repeat("true", 11)), Chunk(native, "mod.a", """
            return io == nil, os == nil, debug == nil, package == nil, load == nil, loadstring == nil, dofile == nil,
                   loadfile == nil, collectgarbage == nil, math.randomseed == nil, string.dump == nil
            """));
    }

    [Fact]
    public void RequireResolvesTheModsOwnFilesOnceAndNothingElse()
    {
        Scripting native = Native(new StackBuilder()
            .Mod("mod.other", "-- file: init.lua\nreturn {}", vm: false)
            .Mod("mod.a", """
                -- file: util/names.lua
                loads = (loads or 0) + 1
                return { name = "x" }
                -- file: init.lua
                local a = require("util.names")
                local b = require("util.names")
                same = a == b
                """, vm: false));
        Run(Copy(), native);

        Assert.Equal("true\t1", Chunk(native, "mod.a", "return same, loads"));
        Assert.Contains("not found", TryChunk(native, "mod.a", "require('mod.other')").Text);
        Assert.Contains("not found", TryChunk(native, "mod.a", "require('missing')").Text);
    }

    [Fact]
    public void TheConsoleKeepsItsOwnEnvironmentAndRunsInAModsWhenNamed()
    {
        Scripting native = Native(new StackBuilder().Mod("mod.a", "-- file: init.lua\nsecret = 7", vm: false));
        Run(Copy(), native);

        Assert.Equal("", Chunk(native, null, "x = 41"));
        Assert.Equal("42", Chunk(native, null, "return x + 1"));
        Assert.Equal("nil", Chunk(native, null, "return secret"));
        Assert.Equal("7", Chunk(native, "mod.a", "return secret"));
        Assert.Equal("true", Chunk(native, null, "return maid.vm ~= nil"));
        ConsoleResult error = TryChunk(native, null, "error('oops')");
        Assert.False(error.Succeeded);
        // An error reads as reference Lua gives it, with the traceback after it, and without the library's name.
        Assert.StartsWith("[string \"console\"]:1: oops\n", error.Text);
        Assert.StartsWith("(error object is a table value)", TryChunk(native, null, "error({})").Text);
        // Lua 5.2 gives a number the position too (lua_isstring).
        Assert.StartsWith("[string \"console\"]:1: 7\n", TryChunk(native, null, "error(7)").Text);
        ConsoleResult syntax = TryChunk(native, null, "return (");
        Assert.False(syntax.Succeeded);
        Assert.DoesNotContain("Lua-CSharp", syntax.Text);
    }

    [Fact]
    public void TheConsoleAloneCreatesTheRuntimeOnFirstUse()
    {
        LazyModRuntime runtime = Runtime();
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup { Runtime = runtime });
        Run(Copy(), native);
        Assert.False(runtime.Created);

        Assert.Equal("5", Chunk(native, null, "return maid.vm.g[0x100]"));
        Assert.True(runtime.Created);
    }

    [Fact]
    public void AContextIsLiveOnlyDuringItsCall()
    {
        Script root = Copy();
        Scripting native = Native(new StackBuilder()
            .Mod("mod.a", "-- file: hooks.lua\nfunction keep(ctx) kept = ctx end")
            .Point(root, 1, "mod.a", "hooks.lua:keep"));
        Run(root, native);

        ConsoleResult result = TryChunk(native, "mod.a", "return kept.args[1].value");
        Assert.False(result.Succeeded);
        Assert.Contains("call has returned", result.Text);
        Assert.Equal("ROOT.BIN", Chunk(native, "mod.a", "return kept.script"));
    }

    [Fact]
    public void MathRandomDrawsFromTheEngineInRange()
    {
        Scripting native = Native(new StackBuilder().Mod("mod.a", "-- file: init.lua\n", vm: false));
        Run(Copy(), native);

        Assert.Equal("true", Chunk(native, "mod.a", """
            for i = 1, 200 do
                local d = math.random(6) if d < 1 or d > 6 or d % 1 ~= 0 then return false end
                local r = math.random(-3, 3) if r < -3 or r > 3 then return false end
                local w = math.random(0, 2^40) if w < 0 or w > 2^40 then return false end
                local f = math.random() if f < 0 or f >= 1 then return false end
            end
            return true
            """));
        Assert.Contains("interval is empty", TryChunk(native, "mod.a", "return math.random(0)").Text);
        Assert.Contains("2^45", TryChunk(native, "mod.a", "return math.random(0, 2^46)").Text);
    }

    // ------------------------------------------------------------------------------------------- plan §3.1

    /// <summary>An engine exception inside a callback leaves Lua at once, past any pcall, as itself.</summary>
    [Fact]
    public void AnEngineUnwindInsideACallbackPassesEveryLuaFrame()
    {
        var stack = new ModRuntimeStack(
            [new ModRuntimeMod("mod.a", "1.0.0", false, true, [], [new ModSource("mod.a", "hooks.lua", """
                function h(ctx)
                    local ok = pcall(function() return maid.vm.g[1] end)
                    after = true
                end
                """)])],
            [new ModRuntimeHandler("mod.a", "hooks.lua", "h")]);
        using var runtime = new LuaModRuntime();
        using IModSession session = runtime.BuildSession(stack, new UnwindingAccess(), ModState.Empty, SessionReason.Boot, ModLimits.Default);

        Assert.Throws<UnwindException>(() => session.Invoke(new HandlerId(0), HookContext.ForEngineEvent("test")));
        Assert.Equal("nil", session.RunChunk("mod.a", "return after").Text);
    }

    private sealed class UnwindException : Exception;

    private sealed class UnwindingAccess : IScriptAccess, IModAccess, IVmAccess
    {
        public IModAccess Mod(string modId) => this;
        public IModAccess Console => this;
        public bool Restoring => false;
        public string ModId => "mod.a";
        public IVmAccess? Vm => this;
        public int DrawRandom() => 0;
        public void Log(ModLogLevel level, string message) { }
        public double ReadInteger(double address) => throw new UnwindException();
        public void WriteInteger(double address, double value) { }
        public double ReadFloat(double address) => 0;
        public void WriteFloat(double address, double value) { }
        public string ReadString(double address) => "";
        public void WriteString(double address, string value) { }
        public double ReadPointer(double index) => 0;
        public double ReadStringPointer(double index) => 0;
        public double ReadLocalInteger(double index) => 0;
        public void WriteLocalInteger(double index, double value) { }
        public double ReadLocalFloat(double index) => 0;
        public void WriteLocalFloat(double index, double value) { }
        public string ReadLocalString(double index) => "";
        public void WriteLocalString(double index, string value) { }
        public ScriptFrameInfo? Frame => null;
        public IReadOnlyList<ScriptFrameInfo> Frames() => [];
        public double? ScriptId(string name) => null;
    }

    // ------------------------------------------------------------------------------------------- the check (§14.2)

    [Fact]
    public void TheCheckFindsTopLevelFunctionsWritesAndSyntaxErrorsWithoutRunning()
    {
        using var runtime = new LuaModRuntime();
        ModSourceCheck check = runtime.Check(new ModSource("mod.a", "hooks.lua", """
            local game = require("himegari-base")
            error("never runs")
            local function helper() end
            function on_award(e)
                if e.amount > 0 then game.g.stage_turn_limit = 30 end
                for i = 1, 2 do game.table("unit_current_levels")[i] = 5 end
            end
            M = {}
            function M.not_a_handler() end
            on_other = function() end
            if false then function hidden() end end
            """));

        Assert.True(check.Parsed);
        Assert.Equal(["on_award"], check.TopLevelFunctions.Order());
        Assert.Equal(["stage_turn_limit", "unit_current_levels"], check.WrittenNames);

        // What Lua-CSharp's own syntax tree refused (plan §4.2): comments, \z in a string, a bare return at the end.
        ModSourceCheck commented = runtime.Check(new ModSource("mod.a", "commented.lua", """
            -- A comment first, with an apostrophe: the guide's example.
            --[==[ a long
            comment ]==]
            local s = 'a\z
                       b' -- and one after code
            function on_start(ctx) end --[[ inline ]] function on_award(ctx) end
            function M:method() end
            local x = function() end
            y = x.g.not_written, 1
            z = 2
            a.g.first, b.table("second")[1], c = 1, 2, 3
            if x.g.compared == 1 then end
            return
            """));
        Assert.True(commented.Parsed, string.Join("; ", commented.Errors));
        Assert.Equal(["on_award", "on_start"], commented.TopLevelFunctions.Order());
        Assert.Equal(["first", "second"], commented.WrittenNames);

        ModSourceCheck broken = runtime.Check(new ModSource("mod.a", "bad.lua", "function broken(\n  return 1\nend"));
        ModSourceError error = Assert.Single(broken.Errors);
        Assert.Equal(2, error.Line);
        // The error carries its line; the message is the compiler's without the [string "CHUNK"]:LINE: it puts in front.
        Assert.DoesNotContain("bad.lua", error.Message);
        Assert.Contains("near 'return'", error.Message);
    }

    [Theory]
    // Lua-CSharp 0.5.8 refuses these as written ("malformed number").
    [InlineData("return t[0x100]", "return t[0x100 ]")]
    [InlineData("return 0x10+1, 0xff==255, 0X1f;", "return 0x10 +1, 0xff ==255, 0X1f ;")]
    [InlineData("return x..0x10]", "return x..0x10 ]")]
    [InlineData("return 0x10e+1", "return 0x10e +1")]
    // Left alone: strings, comments, names, decimal numerals, hex numerals with a fraction or an exponent, white space.
    [InlineData("return '0x10]', \"0x10]\", [[0x10]]", null)]
    [InlineData("-- 0x10]\n--[==[ 0x10] ]==] return a0x10, t[10], t[1.5], t[1e2], t[0x1p4], t[0x1.8], t[ 0x10 ]", null)]
    [InlineData("return '\\z\n  0x10]'", null)]
    // A letter after the numeral is one malformed numeral to Lua too: it stays malformed.
    [InlineData("return 0x10g", null)]
    public void HexNumeralsBeforeOperatorsGetASpace(string text, string? prepared)
    {
        Assert.Equal(prepared ?? text, LuaSource.Prepare(text));
    }

    [Fact]
    public void HexNumeralsReadAsLuaReadsThemAndErrorLinesAreTheModsOwn()
    {
        using var runtime = new LuaModRuntime();
        Scripting native = Native(new StackBuilder());
        Assert.Equal("256\t17\ttrue", Chunk(native, null, "local t = {[0x10]=256} return t[0x10], 0x10+1, 0xff==255"));

        // The space after 0x10 moves no line; the compiler's errors carry a line and no column, as Lua's do.
        ModSourceCheck check = runtime.Check(new ModSource("mod.a", "bad.lua", "x = 1\nlocal t = {} t[0x10] = = 2"));
        ModSourceError error = Assert.Single(check.Errors);
        Assert.Equal((2, 0), (error.Line, error.Column));
        Assert.StartsWith("line 2: ", error.ToString());
    }
}
