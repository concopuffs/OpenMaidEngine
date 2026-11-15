using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 5 of docs/superpowers/specs/2026-11-10-lua-scripting-design.md, script calls (§10, gate 9), over synthetic
/// scripts and hand-built stacks: <c>maid.vm.call</c> runs a script as a nested frame with the cause <c>mod</c>; a wait,
/// a save, a load, a script error and the budget refuse it with the instruction named, the cells keeping what the script
/// did and the statement block put back; a call is refused outside a handler the interpreter invoked; and a script that
/// ends the scene or the run abandons the handler past <c>pcall</c>. The restore's case is in
/// <see cref="LuaStateTests"/>; the link's check in <see cref="ModLuaLinkTests"/>; the list of waits in
/// <see cref="ModCallWaitsTests"/>.
/// </summary>
public sealed class LuaCallsTests : IDisposable
{
    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly int Mov = Table.ByLabel("mov")!.Value;
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        foreach (IDisposable item in _owned) item.Dispose();
    }

    private Scripting Native(LuaStackBuilder builder, bool trace = false, ModLimits? limits = null)
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        var (stack, plan) = builder.Build();
        return new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = runtime, Stack = stack, Plan = plan, Trace = trace, Limits = limits ?? ModLimits.Default,
        });
    }

    /// <summary>Scripts by name, and <paramref name="title"/> as resource 0, which a return to title enters.</summary>
    private static MapProvider Provider(Script? title, params Script[] scripts)
    {
        var byId = new Dictionary<long, Script>();
        if (title != null) byId[0] = title;
        return new MapProvider(byId, scripts.ToDictionary(script => script.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static VirtualMachine Run(Script root, Scripting native, IScriptProvider provider, IInputAndWaits? waits = null,
                                      VmOptions? options = null, ShellServices? shell = null, EngineTick? tick = null,
                                      SkipAndAuto? skip = null, Audio? audio = null)
    {
        var vm = new VirtualMachine(root, Table, waits ?? new HeadlessWaits(), options, provider, scripting: native,
                                    shell: shell, tick: tick, skipAndAuto: skip, audio: audio);
        vm.Run();
        return vm;
    }

    private static Operand G(int address) => new(T_GINT, address);
    private static Operand Imm(long value) => new(T_IMM, value);

    private static Script Assemble(string name, params (int, Operand[])[] code)
        => ScriptAssembler.Assemble(Table, name, code.ToList(), []);

    /// <summary>The mod's log lines, without the time, the level and the mod.</summary>
    private static string[] Lines(Scripting native, string mod = "mod.a")
        => native.RecentLog.Where(line => line.Contains($" {mod}: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf($" {mod}: ", StringComparison.Ordinal) + mod.Length + 3)..]).ToArray();

    private static void NoErrors(Scripting native)
        => Assert.True(!native.RecentLog.Any(line => line.Contains(" error ", StringComparison.Ordinal)),
                       string.Join("\n", native.RecentLog));

    private static ConsoleResult TryChunk(Scripting native, string? mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        Assert.True(request.Done);
        return request.Result!;
    }

    /// <summary>ROOT: its first instruction is the hooked one, then it exits.</summary>
    private static Script Root() => Assemble("ROOT.BIN", (Mov, [G(0x300), Imm(1)]), (0x2, []));

    /// <summary>A handler at ROOT's first instruction that calls SUB under pcall and logs the result.</summary>
    private const string Caller = """
        -- file: hooks.lua
        function caller(ctx)
            local ok, err = pcall(maid.vm.call, "SUB")
            maid.log.info(tostring(ok) .. " " .. tostring(err))
        end
        """;

    // ------------------------------------------------------------------------------------------- the call

    [Fact]
    public void AWaitFreeCallRunsAsANestedFrameWithTheCauseModAndHooksFireInsideIt()
    {
        Script root = Root();
        // SUB doubles G[0x310] into G[0x311], then marks G[0x312].
        Script sub = Assemble("SUB.BIN", (0x50, [G(0x311), G(0x310), G(0x310)]), (Mov, [G(0x312), Imm(1)]), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function caller(ctx)
                    maid.vm.g[0x310] = 21
                    maid.vm.call("SUB")
                    maid.vm.g[0x320] = maid.vm.g[0x311]
                    assert(maid.vm.frame.script == "ROOT.BIN", "the hooked frame runs again")
                end
                function sub_enter(ctx)
                    assert(ctx.cause == "mod" and ctx.mod == "mod.a", tostring(ctx.cause) .. " " .. tostring(ctx.mod))
                    local frames = maid.vm.frames()
                    assert(#frames == 2 and frames[1].script == "ROOT.BIN" and frames[2].script == "SUB.BIN")
                    maid.vm.g[0x321] = 1
                end
                function inner(ctx) maid.vm.g[0x322] = maid.vm.g[0x311] end
                """)
            .Calls("mod.a", "SUB")
            .Point(root, 0, "mod.a", "hooks.lua:caller")
            .ScriptHook("SUB.BIN", "enter", "mod.a", "hooks.lua:sub_enter")
            .Site(sub, 1, "mod.a", "hooks.lua:inner"), trace: true);

        VirtualMachine vm = Run(root, native, Provider(null, sub));

        NoErrors(native);
        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal((42, 1, 42), (vm.Globals[0x320], vm.Globals[0x321], vm.Globals[0x322]));
        Assert.Equal(1, vm.Globals[0x300]);   // the hooked instruction ran after the call
        string enter = Assert.Single(native.RecentTrace, line => line.Contains("sub_enter", StringComparison.Ordinal));
        Assert.Contains("\"cause\":\"mod\",\"caller\":\"mod.a\"", enter);
        Assert.Contains("\"within\":[\"mod.a hooks.lua:caller\"]", enter);
        string call = Assert.Single(native.RecentTrace, line => line.Contains("hooks.lua:caller\",\"hook\"", StringComparison.Ordinal));
        Assert.Contains("\"calls\":[{\"script\":\"SUB.BIN\",\"result\":\"ok\"}]", call);
    }

    [Fact]
    public void AScriptTheListDoesNotNameAMissingScriptAndNoNameAreRefused()
    {
        Script root = Root();
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function caller(ctx)
                    for _, name in ipairs({ "OTHER", "gone.bin" }) do
                        local ok, err = pcall(maid.vm.call, name)
                        maid.log.info(tostring(ok) .. " " .. err)
                    end
                    local ok, err = pcall(maid.vm.call)
                    maid.log.info(tostring(ok) .. " " .. err)
                end
                """)
            .Calls("mod.a", "SUB", "GONE")
            .Point(root, 0, "mod.a", "hooks.lua:caller"));

        VirtualMachine vm = Run(root, native, Provider(null, Assemble("SUB.BIN", (0x5, []))));

        Assert.Equal("exit", vm.HaltReason);
        string[] lines = Lines(native);
        Assert.Equal(3, lines.Length);
        Assert.Contains("false ", lines[0]);
        Assert.Contains("OTHER is not in mod.a's [lua] calls", lines[0]);
        Assert.Contains("calling GONE failed: no script GONE.BIN is installed", lines[1]);
        Assert.Contains("bad argument #1 to 'call' (a script name expected)", lines[2]);
    }

    [Fact]
    public void AModsOwnScriptIsCalledByItsNameAndRunsAsItsCatalogRecord()
    {
        Script root = Root();
        Script own = Assemble("$2$WAITER.BIN", (Mov, [G(0x330), Imm(7)]), (0x5, []));
        // Another mod's script of the same name is the game's, which the list does not name.
        Script game = Assemble("WAITER.BIN", (Mov, [G(0x331), Imm(1)]), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function caller(ctx)
                    maid.vm.call("waiter.bin")
                    local ok, err = pcall(maid.vm.call, "$2$WAITER")
                    maid.log.info(tostring(ok) .. " " .. tostring(err))
                end
                """)
            .OwnScript("mod.a", "WAITER", "$2$WAITER")
            .Point(root, 0, "mod.a", "hooks.lua:caller"), trace: true);

        VirtualMachine vm = Run(root, native, Provider(null, own, game));

        NoErrors(native);
        Assert.Equal(7, vm.Globals[0x330]);
        Assert.False(vm.Globals.ContainsKey(0x331));   // the game's WAITER did not run
        Assert.Equal(["true nil"], Lines(native));   // the catalog name is on the list too
        string call = Assert.Single(native.RecentTrace, line => line.Contains("hooks.lua:caller\",\"hook\"", StringComparison.Ordinal));
        Assert.Contains("\"calls\":[{\"script\":\"$2$WAITER.BIN\",\"result\":\"ok\"},{\"script\":\"$2$WAITER.BIN\",\"result\":\"ok\"}]", call);
    }

    // ------------------------------------------------------------------------------------------- refusals

    /// <summary>Spec §10, gate 9: each case's instruction refuses the call before it reaches the waits or the store.</summary>
    public static TheoryData<int, string> Refusals() => new()
    {
        { 0xc8, "waits (Sleep), and a called script cannot wait" },
        { 0x72, "waits (WaitForInput), and a called script cannot wait" },
        { 0x1b4, "waits (ShowDiagnosticMessage), and a called script cannot wait" },
        { 0x20f, "waits (PlayModalMovieToSurface), and a called script cannot wait" },
        { 0x19e, "saves, and a called script cannot save" },
        { 0x19f, "loads a save, and a called script cannot load" },
        { 0x1a1, "loads a save, and a called script cannot load" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void AWaitASaveOrALoadInACallFailsItWithTheInstructionNamedAndTheCellsKeepItsWrites(int opcode, string reason)
    {
        Script root = Root();
        int argc = Table.TryGetDefinition(opcode, out OpcodeDefinition definition) ? definition.Argc : 0;
        Operand[] args = Enumerable.Range(0, argc).Select(i => i == 0 && opcode >= 0x19e ? G(0x20) : Imm(1)).ToArray();
        // SUB writes G[0x330], reaches the refused instruction at 0x5, and would write G[0x331] after it.
        Script sub = Assemble("SUB.BIN", (Mov, [G(0x330), Imm(7)]), (opcode, args), (Mov, [G(0x331), Imm(1)]), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", Caller)
            .Calls("mod.a", "SUB")
            .Point(root, 0, "mod.a", "hooks.lua:caller"));
        var waits = new RecordingWaits();

        VirtualMachine vm = Run(root, native, Provider(null, sub), waits);

        Assert.Equal("exit", vm.HaltReason);
        string line = Assert.Single(Lines(native));
        Assert.StartsWith("false ", line);
        Assert.Contains($"calling SUB failed: SUB.BIN@0x5 (op 0x{opcode:x} {definition.CanonicalLabel}) {reason}", line);
        Assert.Equal((7, 0), (vm.Globals[0x330], vm.Globals.GetValueOrDefault(0x331)));
        Assert.Equal(1, vm.Globals[0x300]);   // the hooked frame went on
        Assert.Empty(waits.SleptDurations);
        Assert.Empty(waits.Diagnostics);
        Assert.Empty(waits.ModalMovies);
    }

    [Fact]
    public void AScriptErrorInACallFailsItWithTheErrorsText()
    {
        // ROOT arms a mouse callback (its code at 0xb); SUB's op 0xcd dispatches it, ROOT's: a Depth error.
        Script root = Assemble("ROOT.BIN", (0xcc, [Imm(0), Imm(0xb)]), (Mov, [G(0x300), Imm(1)]), (0x2, []),
                               (Mov, [G(0x120), Imm(1)]), (0x5, []));
        Script sub = Assemble("SUB.BIN", (0xcd, []), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", Caller)
            .Calls("mod.a", "SUB")
            .Point(root, 1, "mod.a", "hooks.lua:caller"));
        var waits = new RecordingWaits();

        VirtualMachine vm = Run(root, native, Provider(null, sub), waits);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Empty(waits.Diagnostics);   // no error box
        string line = Assert.Single(Lines(native));
        Assert.Contains("calling SUB failed: SUB.BIN@0x0 (op 0xcd dispatch-mouse-callback) raised a script error: ", line);
        Assert.Contains("SUB.BIN", line[line.IndexOf("script error", StringComparison.Ordinal)..]);
        Assert.Null(vm.ScriptError);
    }

    [Fact]
    public void ACallRefusedInsideAStatementBlockPutsTheBlockBackAndCallbackScriptsWaitForTheCall()
    {
        // ROOT opens a block around its hooked instruction; SUB opens two more and sleeps.
        Script root = Assemble("ROOT.BIN", (0x1f4, []), (Mov, [G(0x300), Imm(1)]), (0x1f5, []),
                               (Mov, [G(0x301), Imm(1)]), (0x2, []));
        Script sub = Assemble("SUB.BIN", (0x1f4, []), (0x1f4, []), (0xc8, [Imm(10)]), (0x5, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", Caller)
            .Calls("mod.a", "SUB")
            .Point(root, 1, "mod.a", "hooks.lua:caller"));
        var skip = SkipAndAuto.WithSystemClock();
        var audio = Audio.Silent(skip);
        var tick = new EngineTick(audio);

        VirtualMachine vm = Run(root, native, Provider(null, sub), skip: skip, audio: audio, tick: tick);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Contains("waits (Sleep)", Assert.Single(Lines(native)));
        Assert.Equal((false, 0), tick.StatementBlock);   // ROOT's close closed its own block; without the put-back (true, 1)

        // A callback script queued inside a call runs once the call has returned: CALLBACK_SETTING copies G[0x311],
        // which SUB sets to 1 and then 2.
        Script counting = Assemble("SUB.BIN", (Mov, [G(0x311), Imm(1)]), (Mov, [G(0x311), Imm(2)]), (0x5, []));
        Script callback = Assemble("CALLBACK_SETTING.BIN", (Mov, [G(0x340), G(0x311)]), (0x5, []));
        Script second = Root();
        Scripting again = Native(new LuaStackBuilder()
            .Mod("mod.a", Caller)
            .Calls("mod.a", "SUB")
            .Point(second, 0, "mod.a", "hooks.lua:caller"));
        var shell = ShellServices.Headless();
        var waits = new ScriptContextWaits("SUB.BIN", shell.RequestSettingCallback);

        VirtualMachine queued = Run(second, again, Provider(null, counting, callback), waits, shell: shell);

        Assert.Equal("exit", queued.HaltReason);
        Assert.Equal("true nil", Assert.Single(Lines(again)));
        Assert.Equal(2, queued.Globals[0x340]);
    }

    [Fact]
    public void ACallFromInitLuaASessionStartHandlerOrTheConsoleIsRefused()
    {
        Script root = Assemble("ROOT.BIN", (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: init.lua
                local ok, err = pcall(maid.vm.call, "SUB")
                maid.log.info("init " .. err)
                -- file: hooks.lua
                function on_start(e)
                    local ok, err = pcall(maid.vm.call, "SUB")
                    maid.log.info("start " .. err)
                end
                """)
            .Calls("mod.a", "SUB")
            .Engine(Scripting.SessionStartEvent, "mod.a", "hooks.lua:on_start"));
        Script sub = Assemble("SUB.BIN", (Mov, [G(0x330), Imm(7)]), (0x5, []));

        VirtualMachine vm = Run(root, native, Provider(null, sub));

        Assert.Equal("exit", vm.HaltReason);
        string[] lines = Lines(native);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("init ", lines[0]);
        Assert.StartsWith("start ", lines[1]);
        Assert.All(lines, line => Assert.Contains("maid.vm.call cannot run while the session is built", line));
        foreach (string? mod in new[] { "mod.a", null })
        {
            ConsoleResult result = TryChunk(native, mod, "return pcall(maid.vm.call, 'SUB')");
            Assert.Contains("maid.vm.call is not available in the console", result.Text);
        }
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x330));
    }

    // ------------------------------------------------------------------------------------------- unwinding

    [Theory]
    [InlineData("title")]
    [InlineData("exit")]
    [InlineData("stop")]
    [InlineData("halt")]
    public void AReturnToTitleAnExitAStopAndAHaltInsideACallAbandonTheHandlerPastPcall(string ending)
    {
        Script root = Root();
        Script sub = ending switch
        {
            "title" => Assemble("SUB.BIN", (0x9, [])),
            "exit" => Assemble("SUB.BIN", (0x1, [])),
            "halt" => Assemble("SUB.BIN", (0x8c, [Imm(0)])),   // a loop the step limit ends
            _ => Assemble("SUB.BIN", (Mov, [G(0x330), Imm(7)]), (0x5, [])),
        };
        // A return to title enters TITLE, which marks G[0x360] and exits.
        Script title = Assemble("TITLE.BIN", (Mov, [G(0x360), Imm(1)]), (0x2, []));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function caller(ctx)
                    local ok, err = pcall(maid.vm.call, "SUB")
                    maid.log.info("after the call")
                end
                function on_start(e) maid.log.info("start " .. e.reason) end
                """)
            .Calls("mod.a", "SUB")
            .Point(root, 0, "mod.a", "hooks.lua:caller")
            .Engine(Scripting.SessionStartEvent, "mod.a", "hooks.lua:on_start"), trace: true);
        // The stop is the front end's, asked for as SUB is entered.
        StopAndReload? stop = null;
        var waits = new ScriptContextWaits("SUB.BIN", () => { if (ending == "stop") stop!.RequestStop(); });
        stop = waits.StopAndReload;

        VirtualMachine vm = Run(root, native, Provider(title, sub), waits,
                                ending == "halt" ? new VmOptions(MaxSteps: 50) : null);

        Assert.Equal(ending switch
        {
            "title" => "exit",
            "halt" => "STEP-LIMIT",
            _ => "exit-request",
        }, vm.HaltReason);
        Assert.Equal(ending == "title" ? ["start boot", "start title"] : ["start boot"], Lines(native));
        Assert.Equal(ending == "title" ? 1 : 0, vm.Globals.GetValueOrDefault(0x360));
        Assert.Equal(0, vm.Globals.GetValueOrDefault(0x300));   // the hooked instruction never ran
        string call = Assert.Single(native.RecentTrace, line => line.Contains("hooks.lua:caller\"", StringComparison.Ordinal));
        Assert.Contains("\"result\":\"abandoned\"", call);
        Assert.Contains("\"calls\":[{\"script\":\"SUB.BIN\",\"result\":\"abandoned\"}]", call);
    }

    // ------------------------------------------------------------------------------------------- bounds

    [Fact]
    public void TheCallsBudgetEndsALoopingScriptAndTheNestingCapEndsARecursion()
    {
        Script root = Root();
        Script loop = Assemble("SUB.BIN", (0x8c, [Imm(0)]));
        Scripting native = Native(new LuaStackBuilder()
            .Mod("mod.a", Caller)
            .Calls("mod.a", "SUB")
            .Point(root, 0, "mod.a", "hooks.lua:caller"), limits: new ModLimits { CallInstructions = 100 });

        VirtualMachine vm = Run(root, native, Provider(null, loop));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Contains("calling SUB failed: the call ran past its budget of 100 script instructions at SUB.BIN@0x0 (op 0x8c jmp)",
                        Assert.Single(Lines(native)));

        // SUB's enter hook calls SUB again: each level is a handler, so the cap of spec §5.5 ends it, on the stack the
        // port's engine thread has (godot/Main.cs).
        Script recursive = Root();
        Script sub = Assemble("SUB.BIN", (0x50, [G(0x370), G(0x370), Imm(1)]), (0x5, []));
        Scripting deep = Native(new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function caller(ctx) maid.vm.call("SUB") end
                function again(ctx) maid.vm.call("SUB") end
                """)
            .Calls("mod.a", "SUB")
            .Point(recursive, 0, "mod.a", "hooks.lua:caller")
            .ScriptHook("SUB.BIN", "enter", "mod.a", "hooks.lua:again"));
        VirtualMachine? nested = null;
        var thread = new Thread(() => nested = Run(recursive, deep, Provider(null, sub)), 8 << 20);
        thread.Start();
        thread.Join();

        Assert.Equal("exit", nested!.HaltReason);
        // 32 handlers run: the caller and 31 enter hooks; the 32nd enter hook is refused, and every frame returns.
        Assert.Equal(32, nested.Globals[0x370]);
        string error = Assert.Single(deep.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));
        Assert.Contains("nested too deep", error);
    }

    /// <summary>Waits that return at once and run <paramref name="action"/> as <paramref name="script"/> is entered.</summary>
    private sealed class ScriptContextWaits(string script, Action action) : IInputAndWaits
    {
        public StopAndReload StopAndReload { get; } = new();
        StopAndReload? IInputAndWaits.StopAndReload => StopAndReload;

        public void EnterScriptContext(string scriptName)
        {
            if (scriptName == script) action();
        }

        public void Sleep(long duration) { }
        public void FrameYield() { }
        public void WaitForInput() { }
    }
}
