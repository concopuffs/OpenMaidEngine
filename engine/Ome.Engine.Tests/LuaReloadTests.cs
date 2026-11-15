using System.Collections.Concurrent;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 6 of docs/superpowers/specs/2026-11-10-lua-scripting-design.md: <c>--lua-reload</c> (§13). The scripting
/// native takes a reload at the next instruction boundary outside a wait, keeping <c>maid.state</c>; a build that fails
/// keeps the running session; the front end's check (<see cref="ModLuaReload"/>) and its watcher
/// (<see cref="LuaReloadWatcher"/>) over a mod folder.
/// </summary>
public sealed class LuaReloadTests : IDisposable
{
    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly int Mov = Table.ByLabel("mov")!.Value;
    private readonly List<IDisposable> _owned = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-lua-reload-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (IDisposable item in _owned) item.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LazyModRuntime Runtime()
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        return runtime;
    }

    private Scripting Native(LuaStackBuilder builder)
    {
        var (stack, plan) = builder.Build();
        return new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = Runtime(), Stack = stack, Plan = plan, Errors = LuaErrorPolicy.Continue, Limits = ModLimits.Default,
        });
    }

    private static Operand G(int address) => new(T_GINT, address);
    private static Operand Imm(long value) => new(T_IMM, value);

    /// <summary>ROOT: 0 G[0x100] = 1; 1 sleep 10 (where the tests hand a reload over); 2 G[0x101] = 2; 3 exit. The
    /// tests hook instructions 0 and 2.</summary>
    private static Script Sleeper() => ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
    {
        (Mov, [G(0x100), Imm(1)]),
        (0xc8, [Imm(10)]),
        (Mov, [G(0x101), Imm(2)]),
        (0x2, []),
    }, []);

    /// <summary>A host that runs <paramref name="atSleep"/> inside the op 0xc8 sleep: the engine thread inside a wait, as
    /// a front end's request runs.</summary>
    private sealed class RequestAtSleep(Action atSleep) : RecordingWaits
    {
        public override void Sleep(long duration)
        {
            atSleep();
            base.Sleep(duration);
        }
    }

    private static VirtualMachine Run(Script script, Scripting native, IInputAndWaits? waits = null)
    {
        var vm = new VirtualMachine(script, Table, waits ?? new HeadlessWaits(), scripting: native);
        vm.Run();
        return vm;
    }

    private static string Chunk(Scripting native, string mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        Assert.True(request.Result!.Succeeded, request.Result.Text);
        return request.Result.Text;
    }

    private static string Version(int add, string init = "maid.state.builds = (maid.state.builds or 0) + 1") => $$"""
        -- file: init.lua
        {{init}}
        -- file: hooks.lua
        function on_point(ctx)
            maid.state.hits = (maid.state.hits or 0) + 1
            maid.vm.g[0x110] = maid.vm.g[0x110] + {{add}}
        end
        function on_start(e) maid.state.reasons = (maid.state.reasons or "") .. e.reason .. " " end
        """;

    private static LuaStackBuilder Hooked(Script root, string files) => new LuaStackBuilder()
        .Mod("mod.a", files)
        .Point(root, 0, "mod.a", "hooks.lua:on_point")
        .Point(root, 2, "mod.a", "hooks.lua:on_point")
        .Engine(Scripting.SessionStartEvent, "mod.a", "hooks.lua:on_start");

    private static void Reload(Scripting native, string files) => native.RequestReload("mod.a", LuaStackBuilder.Sources("mod.a", files));

    // ------------------------------------------------------------------------------------------- the native's reload

    [Fact]
    public void AReloadAtTheNextBoundaryRebuildsTheSessionKeepingStateAndSwapsTheBodies()
    {
        Script root = Sleeper();
        Scripting native = Native(Hooked(root, Version(1)));

        VirtualMachine vm = Run(root, native, new RequestAtSleep(() =>
        {
            Reload(native, Version(100));
            Assert.True(native.ReloadPending);
        }));

        Assert.Equal("exit", vm.HaltReason);
        Assert.False(native.ReloadPending);
        Assert.Equal(101, vm.Globals[0x110]);   // the old body before the sleep, the new one after
        Assert.Equal("hits=2 builds=2 reasons=boot reload  session=reload", Chunk(native, "mod.a",
            "return 'hits=' .. maid.state.hits .. ' builds=' .. maid.state.builds .. ' reasons=' .. maid.state.reasons"
            + " .. ' session=' .. maid.session.reason"));
        Assert.Equal(SessionReason.Reload, native.Reason);
        Assert.Contains(native.RecentLog, line => line.Contains("(engine): reloaded mod.a: the session was rebuilt, keeping maid.state"));
        Assert.Equal("Lua: reloaded mod.a", native.LatestNotice);
    }

    [Fact]
    public void AReloadWhoseBuildFailsKeepsTheRunningSession()
    {
        Script root = Sleeper();
        Scripting native = Native(Hooked(root, Version(1)));

        VirtualMachine vm = Run(root, native, new RequestAtSleep(() => Reload(native, Version(100, "error('broken')"))));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(2, vm.Globals[0x110]);   // the running session's body, both times
        Assert.Equal("hits=2 reasons=boot ", Chunk(native, "mod.a",
            "return 'hits=' .. maid.state.hits .. ' reasons=' .. maid.state.reasons"));
        Assert.Equal(SessionReason.Boot, native.Reason);
        Assert.Contains(native.RecentLog, line => line.Contains("mod.a: reload failed, the running session is kept:")
                                                  && line.Contains("broken"));
        Assert.StartsWith("Lua: reload failed, the running session is kept: mod.a:", native.LatestNotice);
    }

    [Fact]
    public void AStateThatDoesNotSerializeRefusesTheReload()
    {
        Script root = Sleeper();
        Scripting native = Native(Hooked(root, Version(1) + "\nfunction on_start(e) maid.state.keep = function() end end\n"));

        VirtualMachine vm = Run(root, native, new RequestAtSleep(() => Reload(native, Version(100))));

        Assert.Equal(2, vm.Globals[0x110]);
        Assert.Equal(SessionReason.Boot, native.Reason);
        Assert.Contains(native.RecentLog, line => line.Contains(
            "mod.a: not reloaded, the running session is kept: its maid.state cannot be carried into the new session"));
    }

    [Fact]
    public void AReloadBringsBackAModThatWasOffAndGivesASwitchedOffHandlerAnotherChance()
    {
        // ROOT: 0-2 three moves whose flaky handler fails, 3 a sleep (the reload), 4 a move both handlers see, 5 exit.
        Script root = ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
        {
            (Mov, [G(0x100), Imm(1)]), (Mov, [G(0x100), Imm(2)]), (Mov, [G(0x100), Imm(3)]),
            (0xc8, [Imm(10)]),
            (Mov, [G(0x101), Imm(1)]),
            (0x2, []),
        }, []);
        const string Off = "-- file: init.lua\nerror('not ready')\n-- file: hooks.lua\nfunction mark(ctx) maid.vm.g[0x120] = 1 end\n";
        const string Fixed = "-- file: init.lua\n-- file: hooks.lua\nfunction mark(ctx) maid.vm.g[0x120] = 1 end\n";
        var builder = new LuaStackBuilder()
            .Mod("mod.a", Off)
            .Mod("mod.b", """
                -- file: hooks.lua
                function flaky(ctx)
                    maid.state.n = (maid.state.n or 0) + 1
                    if maid.state.n <= 3 then error("not yet") end
                    maid.vm.g[0x121] = maid.state.n
                end
                """)
            .Point(root, 4, "mod.a", "hooks.lua:mark");
        for (int i = 0; i < 3; i++) builder.Point(root, i, "mod.b", "hooks.lua:flaky");
        builder.Point(root, 4, "mod.b", "hooks.lua:flaky");
        Scripting native = Native(builder);

        VirtualMachine vm = Run(root, native, new RequestAtSleep(() =>
        {
            Assert.Contains(native.RecentLog, line => line.Contains("mod.b: hooks.lua:flaky failed"));
            Assert.Equal("Lua: mod.b flaky failed 3 times in a row and is off for this session", native.LatestNotice);
            native.RequestReload("mod.a", LuaStackBuilder.Sources("mod.a", Fixed));
        }));

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[0x120]);   // mod.a is on in the new session
        Assert.Equal(4, vm.Globals[0x121]);   // mod.b's handler is on again, with its state
        Assert.Contains(native.RecentLog, line => line.Contains("mod.a: off for this session") && line.Contains("not ready"));
    }

    [Fact]
    public void AModThatIsStillOffDoesNotStopTheReload()
    {
        Script root = Sleeper();
        var builder = new LuaStackBuilder()
            .Mod("mod.a", Version(1))
            .Mod("mod.off", "-- file: init.lua\nerror('still broken')\n")
            .Point(root, 0, "mod.a", "hooks.lua:on_point")
            .Point(root, 2, "mod.a", "hooks.lua:on_point");
        Scripting native = Native(builder);

        VirtualMachine vm = Run(root, native, new RequestAtSleep(() => Reload(native, Version(100))));

        Assert.Equal(101, vm.Globals[0x110]);
        Assert.Equal(SessionReason.Reload, native.Reason);
        Assert.Contains(native.RecentLog, line => line.Contains("mod.off: still off for this session") && line.Contains("still broken"));
    }

    [Fact]
    public void WithNoSessionRunningTheNextBuildTakesTheFiles()
    {
        Script root = Sleeper();
        Scripting native = Native(Hooked(root, Version(1)));
        Reload(native, Version(100));
        Assert.False(native.ReloadPending);

        VirtualMachine vm = Run(root, native);

        Assert.Equal(200, vm.Globals[0x110]);
        Assert.Equal(SessionReason.Boot, native.Reason);
    }

    [Fact]
    public void ARefusalAndAnUnknownModKeepTheSessionAndSaySo()
    {
        Script root = Sleeper();
        Scripting native = Native(Hooked(root, Version(1)));
        Run(root, native);

        native.RefuseReload("mod.a", "lua/hooks.lua: line 2: unexpected symbol near 'end'");
        Assert.Equal("Lua: mod.a was not reloaded: lua/hooks.lua: line 2: unexpected symbol near 'end'", native.LatestNotice);
        native.RequestReload("mod.x", []);
        Assert.Contains(native.RecentLog, line => line.Contains(
            "mod.x: not reloaded, the running session is kept: it is not a mod of the stack that ships Lua"));
        Assert.False(native.ReloadPending);
        Assert.Equal(SessionReason.Boot, native.Reason);
    }

    /// <summary>An interpreter that has no frames and reports whether it is inside a wait's callback.</summary>
    private sealed class WaitingInterpreter : IInterpreterAccess
    {
        public bool Inside;
        public bool InsideWait => Inside;
        public IReadOnlyList<(string Script, int Offset)> Frames() => [];
        public bool HasFrame => false;
        public long LocalInteger(int index) => throw new NotSupportedException();
        public void SetLocalInteger(int index, long value) => throw new NotSupportedException();
        public long LocalFloat(int index) => throw new NotSupportedException();
        public void SetLocalFloat(int index, long bits) => throw new NotSupportedException();
        public string LocalString(int index) => throw new NotSupportedException();
        public void SetLocalString(int index, string value) => throw new NotSupportedException();
        public CellValue ReadOperand(Operand operand) => throw new NotSupportedException();
        public void WriteOperand(Operand operand, CellValue value) => throw new NotSupportedException();
        public uint? ScriptId(string name) => null;
        public void Call(string name, long instructions) => throw new NotSupportedException();
        public bool IsNumberedSave(int opcode) => false;
    }

    [Fact]
    public void AReloadIsNotTakenInsideAWaitsCallback()
    {
        Scripting native = Native(Hooked(Sleeper(), Version(1)));
        var interpreter = new WaitingInterpreter { Inside = true };
        native.AttachInterpreter(interpreter);
        Assert.Null(native.StartSession(SessionReason.Boot));

        Reload(native, Version(100));
        Assert.True(native.ReloadPending);
        Assert.False(native.BoundaryPending);
        Assert.Null(native.ServiceBoundary());
        Assert.Equal(SessionReason.Boot, native.Reason);

        interpreter.Inside = false;
        Assert.True(native.BoundaryPending);
        Assert.Null(native.ServiceBoundary());
        Assert.Equal(SessionReason.Reload, native.Reason);
        Assert.False(native.ReloadPending);
    }

    // ------------------------------------------------------------------------------------------- the front end's check

    private string Folder(string id, Dictionary<string, string> files)
    {
        string folder = Path.Combine(_root, id + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 3\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n\n[game]\nprofile = \"himegari\"\n"
            + "target = \"extended\"\n\n[lua]\napi = 1\ncapabilities = [\"vm\"]\n");
        foreach (var (path, text) in files)
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        return folder;
    }

    private static readonly ModRuntimeMod Mod = new("mod.r", "1.0.0", false, true, ["base.r"], []);
    private static readonly ModRuntimeHandler[] Handlers =
        [new("mod.r", "hooks.lua", "on_point"), new("other", "hooks.lua", "elsewhere")];

    [Fact]
    public void TheCheckReadsTheFilesAndRunsNone()
    {
        string folder = Folder("mod.r", new()
        {
            ["lua/init.lua"] = "error('never at a check')\n",
            ["lua/hooks.lua"] = "function on_point(ctx) end\n",
            ["lua/util/names.lua"] = "return {}\n",
        });

        ModLuaReloadCheck check = ModLuaReload.Read(folder, Mod, Handlers, Runtime().Get());

        Assert.True(check.Passed, string.Join("; ", check.Problems));
        Assert.Equal(["hooks.lua", "init.lua", "util/names.lua"], check.Files.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.All(check.Files, file => Assert.Equal("mod.r", file.ModId));
    }

    [Fact]
    public void TheCheckRefusesWhatTheLinkWould()
    {
        IModRuntime runtime = Runtime().Get();
        ModLuaReloadCheck broken = ModLuaReload.Read(Folder("mod.r", new()
        {
            ["lua/hooks.lua"] = "function on_point(ctx)\n",
            ["lua/base.r.lua"] = "return 1\n",
        }), Mod, Handlers, runtime);
        Assert.Contains(broken.Problems, problem => problem.StartsWith("lua/hooks.lua: line ", StringComparison.Ordinal));
        Assert.Contains(broken.Problems, problem => problem.StartsWith("lua/base.r.lua is module 'base.r', which is also the id", StringComparison.Ordinal));

        ModLuaReloadCheck renamed = ModLuaReload.Read(Folder("mod.r", new() { ["lua/hooks.lua"] = "function on_other(ctx) end\n" }),
                                                      Mod, Handlers, runtime);
        Assert.Equal(["lua/hooks.lua declares no top-level function on_point, which a hook names; a handler is a global "
                      + "function of its file (function on_point(…))"], renamed.Problems);

        ModLuaReloadCheck gone = ModLuaReload.Read(Folder("mod.r", new() { ["lua/init.lua"] = "\n" }), Mod, Handlers, runtime);
        Assert.Equal(["lua/hooks.lua is gone, and a hook names its on_point; changing the hooks needs a restart"], gone.Problems);

        ModLuaReloadCheck other = ModLuaReload.Read(Folder("mod.s", new() { ["lua/hooks.lua"] = "function on_point() end\n" }),
                                                    Mod, Handlers, runtime);
        Assert.EndsWith("is now mod mod.s, not mod.r; a changed manifest needs a restart", Assert.Single(other.Problems));
    }

    [Fact]
    public void TheWatcherDeliversAModsFilesOnceTheyHaveSettled()
    {
        string folder = Folder("mod.r", new() { ["lua/hooks.lua"] = "function on_point(ctx) end\n" });
        var stack = new ModRuntimeStack([Mod with { Files = [new ModSource("mod.r", "hooks.lua", "function on_point(ctx) end\n")] }],
                                        Handlers);
        var delivered = new BlockingCollection<(string Mod, ModLuaReloadCheck Check)>();
        using var watcher = new LuaReloadWatcher(new Dictionary<string, string> { ["mod.r"] = folder, ["packed"] = _root },
                                                 stack, Runtime().Get(), (mod, check) => delivered.Add((mod, check)),
                                                 TimeSpan.FromMilliseconds(50));
        Assert.Equal(["mod.r"], watcher.Watched.Keys);

        File.WriteAllText(Path.Combine(folder, "lua", "hooks.lua"), "function on_point(ctx) maid.log.info('v2') end\n");

        Assert.True(delivered.TryTake(out var first, TimeSpan.FromSeconds(10)), "no delivery within 10 s");
        Assert.Equal("mod.r", first.Mod);
        Assert.True(first.Check.Passed, string.Join("; ", first.Check.Problems));
        Assert.Contains("v2", Assert.Single(first.Check.Files).Text);
    }
}
