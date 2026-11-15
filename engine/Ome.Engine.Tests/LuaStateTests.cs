using System.Text.Json.Nodes;
using Lua;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Scripting.Lua;

/// <summary>
/// Milestone 2 of docs/superpowers/specs/2026-11-10-lua-scripting-design.md: <c>maid.state</c> beside numbered saves,
/// the session a load rebuilds, <c>maid.session.restoring</c>, <c>ome.session.resumed</c> and <c>ome.save.writing</c>
/// (spec §5.7, §7.7, §11). Gates 7 and 8 and gate 10's load case, over real saves in a temporary directory.
/// </summary>
public sealed class LuaStateTests : IDisposable
{
    private const int T_IMM = 0, T_GINT = 3, T_LINT = 9;
    private const uint RootId = 0x77;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly int Mov = Table.ByLabel("mov")!.Value;
    private static readonly NativeSaveIdentity Identity =
        new(NativeSaveMagic.S4SD, 0x4a343234, "lua-state-test", 3, 10, new NativeSaveBankDimensions(402459, 1, 789, 1, 1, 1));
    private const string StampSuffix = ".mods.toml";

    private readonly List<IDisposable> _owned = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-lua-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (IDisposable item in _owned) item.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ------------------------------------------------------------------------------------------------- helpers

    private static Operand G(int address) => new(T_GINT, address);
    private static Operand L(int index) => new(T_LINT, index);
    private static Operand Imm(long value) => new(T_IMM, value);

    private static Script WithPackedId(Script source, uint packedId)
        => new()
        {
            Name = source.Name,
            PackedId = packedId,
            Header = source.Header,
            Instructions = source.Instructions,
            IndexByOffset = source.IndexByOffset,
            Strings = source.Strings,
            BodyDwords = source.BodyDwords,
            ReadMessageOffsets = source.ReadMessageOffsets,
            ScriptCallOffsets = source.ScriptCallOffsets,
            LocalCallOffsets = source.LocalCallOffsets,
        };

    /// <summary>ROOT: 0 the prologue, 1 the restore's op 0xae, 2 the range a load runs again, 3 op 0x1ad, 4 save slot 1
    /// (its status in G[0x20]), 5 exit. A load re-enters it, runs the prologue, and resumes at 2.</summary>
    private static Script Root() => WithPackedId(ScriptAssembler.Assemble(Table, "ROOT.BIN", new List<(int, Operand[])>
    {
        (Mov, [G(0x100), Imm(5)]),
        (0xae, []),
        (Mov, [G(0x101), Imm(1)]),
        (0x1ad, []),
        (0x19e, [G(0x20), Imm(1)]),
        (0x2, []),
    }, []), RootId);

    /// <summary>LOADER: a full load of slot 1, then exit (which the restored ROOT reaches instead).</summary>
    private static Script Loader() => WithPackedId(ScriptAssembler.Assemble(Table, "LOADER.BIN", new List<(int, Operand[])>
    {
        (0x1a1, [L(0), Imm(1)]),
        (0x2, []),
    }, []), 0x99);

    private static Script CallbackLoad(bool returnToTitle = false) => ScriptAssembler.Assemble(Table, "CALLBACK_LOAD.BIN",
        returnToTitle
            ? new List<(int, Operand[])> { (Mov, [G(0x102), Imm(1)]), (0x9, []) }
            : new List<(int, Operand[])> { (Mov, [G(0x102), Imm(1)]), (0x2, []) }, []);

    private string Saves(string name = "saves")
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>The store the port composes: the save, the stack stamp beside it, and the state file.</summary>
    private static INativeDatStore Store(string directory, string? stamp = null)
        => new SidecarNativeDatStore(
            new SidecarNativeDatStore(new DirectoryNativeDatStore(directory, Identity), directory, StampSuffix, stamp),
            directory, Scripting.StateFileSuffix, currentSidecar: null);

    private static string StateFile(string directory, int slot = 1) => Path.Combine(directory, $"SAVE{slot:D2}.modstate.json");

    private LazyModRuntime Runtime()
    {
        LazyModRuntime runtime = LuaModRuntime.Lazy();
        _owned.Add(runtime);
        return runtime;
    }

    private Scripting Native(LuaStackBuilder? builder, LuaErrorPolicy errors = LuaErrorPolicy.Continue, bool stamped = false)
    {
        var (stack, plan) = (builder ?? new LuaStackBuilder()).Build();
        return new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = Runtime(), Stack = stack, Plan = plan, Errors = errors,
            StampedMods = stamped ? sidecars => sidecars.TryGetValue(StampSuffix, out string? text) ? text.Split(',') : null : null,
        });
    }

    private static VirtualMachine Run(Script root, Scripting native, INativeDatStore store, IScriptProvider? provider = null)
    {
        var vm = new VirtualMachine(root, Table, new HeadlessWaits(), provider: provider,
                                    saves: new SavesAndSettings(store: store), scripting: native);
        vm.Run();
        return vm;
    }

    private static MapProvider Provider(Script? callback = null, Script? title = null)
    {
        var byId = new Dictionary<long, Script> { [RootId] = Root() };
        if (title != null) byId[0] = title;
        var byName = new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase);
        if (callback != null) byName["CALLBACK_LOAD.BIN"] = callback;
        return new MapProvider(byId, byName);
    }

    /// <summary>The log lines of <paramref name="mod"/>, without the time and the level.</summary>
    private static string[] Lines(Scripting native, string mod = "mod.a")
        => native.RecentLog.Where(line => line.Contains($" {mod}: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf($" {mod}: ", StringComparison.Ordinal) + mod.Length + 3)..]).ToArray();

    private static string[] EngineLines(Scripting native)
        => native.RecentLog.Where(line => line.Contains(" (engine): ", StringComparison.Ordinal)).ToArray();

    private static string? Block(string directory, string mod, int slot = 1)
        => JsonNode.Parse(File.ReadAllText(StateFile(directory, slot)))!["mods"]![mod]?["state"]?.ToJsonString();

    /// <summary>The counting mod: it logs the session's life and counts the prologue outside a restore, the repeated
    /// range always, and writes two keys at <c>ome.save.writing</c>.</summary>
    private const string CountingMod = """
        -- file: hooks.lua
        local function log(text) maid.log.info(text) end
        function on_start(e)
            log("start " .. e.reason .. " restoring=" .. tostring(maid.session.restoring) .. " count=" .. tostring(maid.state.count)
                .. " local=" .. tostring(kept) .. " version=" .. tostring(maid.mod.state_version))
            if e.reason == "load" then log("keys " .. tostring(maid.state[1]) .. " | " .. tostring(maid.state["1"])) end
            kept = true
        end
        function on_callback(ctx) log("callback restoring=" .. tostring(maid.session.restoring)) end
        function on_prologue(ctx)
            log("prologue restoring=" .. tostring(maid.session.restoring))
            if not maid.session.restoring then maid.state.count = (maid.state.count or 0) + 1 end
        end
        function on_resumed(e) log("resumed slot=" .. e.slot .. " restoring=" .. tostring(maid.session.restoring)) end
        function on_repeat(ctx)
            log("repeat restoring=" .. tostring(maid.session.restoring))
            maid.state.repeats = (maid.state.repeats or 0) + 1
        end
        function on_writing(e)
            log("writing slot=" .. e.slot)
            maid.state[1] = "integer key"
            maid.state["1"] = "string key"
        end
        """;

    private static LuaStackBuilder Counting(string id = "mod.a")
    {
        Script root = Root();
        return new LuaStackBuilder()
            .Mod(id, CountingMod)
            .Engine(Scripting.SessionStartEvent, id, "hooks.lua:on_start")
            .Engine(Scripting.SessionResumedEvent, id, "hooks.lua:on_resumed")
            .Engine(Scripting.SaveWritingEvent, id, "hooks.lua:on_writing")
            .Point(CallbackLoad(), 0, id, "hooks.lua:on_callback")
            .Point(root, 0, id, "hooks.lua:on_prologue")
            .Point(root, 2, id, "hooks.lua:on_repeat");
    }

    // ------------------------------------------------------------------------------------------- gates 7 and 8

    [Fact]
    public void AFullLoadRebuildsTheSessionWithTheSavedStateAndMarksTheRestore()
    {
        string saves = Saves();
        Scripting first = Native(Counting());
        VirtualMachine played = Run(Root(), first, Store(saves));
        Assert.Equal("exit", played.HaltReason);
        Assert.Equal(0, played.Globals[0x20]);   // the save was written
        Assert.Equal(["start boot restoring=false count=nil local=nil version=nil", "prologue restoring=false",
                      "repeat restoring=false", "writing slot=1"], Lines(first));
        // The keys 1 and "1" are two keys; the keys are in one order, so the same state gives the same file.
        Assert.Equal("""{"i:1":"integer key","s:1":"string key","s:count":1,"s:repeats":1}""", Block(saves, "mod.a"));

        Scripting second = Native(Counting());
        VirtualMachine loaded = Run(Loader(), second, Store(saves), Provider(CallbackLoad()));

        Assert.Equal("exit", loaded.HaltReason);
        Assert.Equal(1, loaded.Globals[0x102]);   // CALLBACK_LOAD ran
        Assert.Equal(
        [
            "start boot restoring=false count=nil local=nil version=nil",
            // The load's session: built before CALLBACK_LOAD, with the saved state, a Lua global of the boot session
            // gone, restoring set through CALLBACK_LOAD and the prologue.
            "start load restoring=true count=1 local=nil version=1.0.0",
            "keys integer key | string key",
            "callback restoring=true",
            "prologue restoring=true",
            // Op 0xae's jump: restoring clears and the event fires before the first resumed instruction, whose hooks
            // then fire a second time (spec §11, "Part of the last script runs again").
            "resumed slot=1 restoring=false",
            "repeat restoring=false",
            "writing slot=1",
        ], Lines(second));
        // The handler that honours the flag kept its total; the one in the repeated range counted the range twice.
        Assert.Equal("""{"i:1":"integer key","s:1":"string key","s:count":1,"s:repeats":2}""", Block(saves, "mod.a"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARemovedModGetsItsStateBackAfterASaveMadeWithoutIt(bool anotherModStays)
    {
        string saves = Saves();
        Run(Root(), Native(Counting()), Store(saves));
        string? saved = Block(saves, "mod.a");

        // mod.a removed: the last Lua mod (no runtime), or beside another; the restored ROOT saves slot 1 again.
        LuaStackBuilder? without = anotherModStays ? Counting("mod.b") : null;
        Scripting native = Native(without);
        VirtualMachine run = Run(Loader(), native, Store(saves), Provider());
        Assert.Equal("exit", run.HaltReason);
        Assert.Equal(0, run.Globals[0x20]);
        Assert.Equal(saved, Block(saves, "mod.a"));
        Assert.Equal(anotherModStays, Block(saves, "mod.b") != null);

        Scripting back = Native(Counting());
        Run(Loader(), back, Store(saves), Provider());
        Assert.Contains("start load restoring=true count=1 local=nil version=1.0.0", Lines(back));
    }

    [Fact]
    public void TheSaveIsTheSameWithAndWithoutAModThatOnlyUsesState()
    {
        string with = Saves("with"), without = Saves("without");
        LuaStackBuilder stateOnly = new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function on_writing(e) maid.state.saves = (maid.state.saves or 0) + 1 end
                """)
            .Engine(Scripting.SaveWritingEvent, "mod.a", "hooks.lua:on_writing");
        Run(Root(), Native(stateOnly), Store(with));
        Run(Root(), Native(null), Store(without));

        NativeNumberedSaveFile modded = new DirectoryNativeDatStore(with, Identity).LoadNumberedFile(1)!;
        NativeNumberedSaveFile plain = new DirectoryNativeDatStore(without, Identity).LoadNumberedFile(1)!;
        // The time in the header differs between any two saves; the save itself does not.
        Assert.Equal(plain.Document.Payload, modded.Document.Payload);
        Assert.Equal(plain.HistoryTail, modded.HistoryTail);
        Assert.Equal("""{"s:saves":1}""", Block(with, "mod.a"));
        Assert.False(File.Exists(StateFile(without)));   // no Lua and no state to carry: no state file
    }

    [Fact]
    public void AStateFileThatDoesNotMatchItsSaveIsReportedAndGivesEmptyState()
    {
        string saves = Saves();
        Run(Root(), Native(Counting()), Store(saves));
        // The file a crash between the two writes would leave: another save's state beside this save.
        JsonNode file = JsonNode.Parse(File.ReadAllText(StateFile(saves)))!;
        file["save"]!["play_seconds"] = 4321;
        File.WriteAllText(StateFile(saves), file.ToJsonString());

        Scripting native = Native(Counting());
        Run(Loader(), native, Store(saves), Provider());

        Assert.Contains(EngineLines(native), line => line.Contains("SAVE01.modstate.json does not belong to its save")
                                                     && line.Contains("4321 s"));
        Assert.Contains("start load restoring=true count=nil local=nil version=nil", Lines(native));
        Assert.Contains("SAVE01.modstate.json does not belong", native.LatestNotice ?? "");
    }

    [Theory]
    [InlineData("mod.a", true)]
    [InlineData("mod.patch", false)]
    public void AMissingStateFileIsReportedWhenTheStampNamesAModThatShipsLua(string stamp, bool reported)
    {
        string saves = Saves();
        Run(Root(), Native(Counting(), stamped: true), Store(saves, stamp));
        File.Delete(StateFile(saves));

        Scripting native = Native(Counting(), stamped: true);
        Run(Loader(), native, Store(saves, stamp), Provider());

        Assert.Equal(reported, EngineLines(native).Any(line =>
            line.Contains("save slot 1 was made with mod.a, which ships Lua, but SAVE01.modstate.json is missing: its state starts empty")));
        Assert.Contains("start load restoring=true count=nil local=nil version=nil", Lines(native));
    }

    [Theory]
    [InlineData(LuaErrorPolicy.Continue)]
    [InlineData(LuaErrorPolicy.Halt)]
    public void AStateThatCannotBeSavedKeepsTheLastGoodBlockAndTheSaveIsStillWritten(LuaErrorPolicy errors)
    {
        string saves = Saves();
        Script twice = ScriptAssembler.Assemble(Table, "TWICE.BIN", new List<(int, Operand[])>
        {
            (0x1ad, []),
            (0x19e, [G(0x20), Imm(1)]),
            (Mov, [G(0x103), Imm(1)]),
            (0x19e, [G(0x21), Imm(2)]),
            (0x2, []),
        }, []);
        LuaStackBuilder builder = new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: init.lua
                maid.state.count = 1
                -- file: hooks.lua
                function on_point(ctx)
                    maid.state.count = 99
                    maid.state.items = { 1, 2, { name = "x", f = function() end } }
                end
                """)
            .Point(twice, 2, "mod.a", "hooks.lua:on_point");
        Scripting native = Native(builder, errors);

        VirtualMachine vm = Run(twice, native, Store(saves));

        Assert.Equal(0, vm.Globals[0x20]);
        Assert.Equal(0, vm.Globals[0x21]);   // the game's save was written
        Assert.True(File.Exists(Path.Combine(saves, "SAVE02.DAT")));
        Assert.Equal("""{"s:count":1}""", Block(saves, "mod.a", slot: 1));
        Assert.Equal(Block(saves, "mod.a", slot: 1), Block(saves, "mod.a", slot: 2));   // its last good state
        string error = Assert.Single(Lines(native));
        Assert.Contains("maid.state was not saved in slot 2, its last good state is kept: maid.state.items[3].f is a function", error);
        if (errors == LuaErrorPolicy.Halt)
            Assert.Equal("lua-state-error: mod.a: maid.state.items[3].f is a function; a save holds booleans, numbers, strings and tables",
                         vm.HaltReason);
        else Assert.Equal("exit", vm.HaltReason);
    }

    [Fact]
    public void AModWhoseInitFailsAtALoadIsOffAndItsStateBlockIsCarriedUnchanged()
    {
        string saves = Saves();
        string mod = """
            -- file: init.lua
            if maid.state.count then error("cannot start from a save") end
            -- file: hooks.lua
            function on_prologue(ctx)
                maid.log.info("prologue")
                maid.state.count = (maid.state.count or 0) + 1
            end
            """;
        LuaStackBuilder Build() => new LuaStackBuilder().Mod("mod.a", mod).Point(Root(), 0, "mod.a", "hooks.lua:on_prologue");
        Run(Root(), Native(Build()), Store(saves));
        string? saved = Block(saves, "mod.a");
        Assert.Equal("""{"s:count":1}""", saved);

        Scripting native = Native(Build());
        VirtualMachine vm = Run(Loader(), native, Store(saves), Provider());

        Assert.Equal("exit", vm.HaltReason);
        // The boot session built (no state); the load's failed, so the prologue hook did not fire after the load.
        string[] lines = Lines(native);
        Assert.Contains(lines, line => line.StartsWith("off for this session:") && line.Contains("cannot start from a save"));
        Assert.DoesNotContain("prologue", lines);
        Assert.Equal(saved, Block(saves, "mod.a"));
    }

    [Fact]
    public void ADataOnlyLoadRebuildsTheSessionAtTheNextBoundary()
    {
        string saves = Saves();
        Run(Root(), Native(Counting()), Store(saves));
        Script dataOnly = ScriptAssembler.Assemble(Table, "DATAONLY.BIN", new List<(int, Operand[])>
        {
            (0x19f, [G(0x20), Imm(1)]),
            (Mov, [G(0x102), Imm(3)]),
            (0x2, []),
        }, []);
        LuaStackBuilder builder = new LuaStackBuilder().Mod("mod.a", CountingMod + "\n" + """
            function on_before_load(ctx) maid.log.info("before load count=" .. tostring(maid.state.count)) end
            function on_after_load(ctx)
                maid.log.info("next count=" .. tostring(maid.state.count) .. " restoring=" .. tostring(maid.session.restoring)
                    .. " reason=" .. maid.session.reason)
            end
            """)
            .Engine(Scripting.SessionStartEvent, "mod.a", "hooks.lua:on_start")
            .Engine(Scripting.SessionResumedEvent, "mod.a", "hooks.lua:on_resumed")
            .Point(dataOnly, 0, "mod.a", "hooks.lua:on_before_load")
            .Point(dataOnly, 1, "mod.a", "hooks.lua:on_after_load");
        Scripting native = Native(builder);

        VirtualMachine vm = Run(dataOnly, native, Store(saves));

        Assert.Equal(0, vm.Globals[0x20]);
        Assert.Equal(3, vm.Globals[0x102]);
        Assert.Equal(
        [
            "start boot restoring=false count=nil local=nil version=nil",
            "before load count=nil",
            // Built at the boundary after op 0x19f, before the next instruction's hooks; no frame is restored, so the
            // flag stays clear and ome.session.resumed does not fire.
            "start load restoring=false count=1 local=nil version=1.0.0",
            "keys integer key | string key",
            "next count=1 restoring=false reason=load",
        ], Lines(native));
    }

    [Fact]
    public void AnAfterHookDoesNotFireWhenALoadRebuiltTheSessionDuringItsInstruction()
    {
        // Spec §7.1: OUTER calls DATAONLY, whose op 0x19f ends the session; the next boundary builds the load's. The
        // call's after hook belonged to the old session, so it does not fire; one inside the new session does.
        string saves = Saves();
        Run(Root(), Native(Counting()), Store(saves));
        Script dataOnly = ScriptAssembler.Assemble(Table, "DATAONLY.BIN", new List<(int, Operand[])>
        {
            (0x19f, [G(0x20), Imm(1)]),
            (Mov, [G(0x102), Imm(3)]),
            (0x5, []),
        }, []);
        Script outer = ScriptAssembler.Assemble(Table, "OUTER.BIN", new List<(int, Operand[])>
        {
            (0x3, [Imm(9)]),
            (Mov, [G(0x105), Imm(1)]),
            (0x2, []),
        }, []);
        LuaStackBuilder builder = new LuaStackBuilder().Mod("mod.a", CountingMod + "\n" + """
            function after_call(ctx) maid.log.info("after call") end
            function after_mov(ctx) maid.log.info("after mov reason=" .. maid.session.reason) end
            """)
            .Engine(Scripting.SessionStartEvent, "mod.a", "hooks.lua:on_start")
            .Site(outer, 0, "mod.a", "hooks.lua:after_call", "after")
            .Site(dataOnly, 1, "mod.a", "hooks.lua:after_mov", "after");
        Scripting native = Native(builder);

        VirtualMachine vm = Run(outer, native, Store(saves), new MapProvider(new() { [9] = dataOnly }));

        Assert.Equal((3, 1), (vm.Globals[0x102], vm.Globals[0x105]));
        Assert.Equal(
        [
            "start boot restoring=false count=nil local=nil version=nil",
            "start load restoring=false count=1 local=nil version=1.0.0",
            "keys integer key | string key",
            "after mov reason=load",
        ], Lines(native));
    }

    [Fact]
    public void EnterFollowsTheSessionStartAndARestoredFrameEntersWithTheCauseRestore()
    {
        // Spec §7.2: the load's session is built before CALLBACK_LOAD, which enters with the cause restore; the saved ROOT
        // is entered again at its entry with the cause restore; a return to title enters the root with the cause title.
        string saves = Saves();
        Run(Root(), Native(Counting()), Store(saves));
        LuaStackBuilder Entering() => Counting()
            .Mod("mod.e", "-- file: e.lua\nfunction on_enter(ctx) maid.log.info(\"enter \" .. ctx.script .. \" \" .. ctx.cause) end")
            .ScriptHook("LOADER.BIN", "enter", "mod.e", "e.lua:on_enter")
            .ScriptHook("CALLBACK_LOAD.BIN", "enter", "mod.e", "e.lua:on_enter")
            .ScriptHook("ROOT.BIN", "enter", "mod.e", "e.lua:on_enter")
            .ScriptHook("TITLE.BIN", "enter", "mod.e", "e.lua:on_enter");
        string[] Order(Scripting native)
            => native.RecentLog.Where(line => line.Contains(" mod.e: ", StringComparison.Ordinal)
                                              || line.Contains(" mod.a: start ", StringComparison.Ordinal)
                                              || line.Contains(" mod.a: resumed ", StringComparison.Ordinal))
                .Select(line => line[(line.IndexOf(": ", 20, StringComparison.Ordinal) + 2)..].Split(" restoring")[0]).ToArray();

        Scripting loaded = Native(Entering());
        Run(Loader(), loaded, Store(saves), Provider(CallbackLoad()));
        Assert.Equal(["start boot", "enter LOADER.BIN root", "start load", "enter CALLBACK_LOAD.BIN restore",
                      "enter ROOT.BIN restore", "resumed slot=1"], Order(loaded));

        Script title = ScriptAssembler.Assemble(Table, "TITLE.BIN", new List<(int, Operand[])> { (0x2, []) }, []);
        Scripting abandoned = Native(Entering());
        Run(Loader(), abandoned, Store(saves), Provider(CallbackLoad(returnToTitle: true), title));
        Assert.Equal(["start boot", "enter LOADER.BIN root", "start load", "enter CALLBACK_LOAD.BIN restore", "start title",
                      "enter TITLE.BIN title"], Order(abandoned));
    }

    /// <summary>A mod watching ROOT's prologue cell, its repeated cell and CALLBACK_LOAD's cell (spec §7.3): it logs each
    /// record and keeps two totals in its state, one that honours the restore flag and one that does not.</summary>
    private static LuaStackBuilder Watching()
        => new LuaStackBuilder()
            .Mod("mod.w", """
                -- file: watch.lua
                function on_write(ctx)
                    maid.log.info(string.format("0x%x %d->%d restoring=%s", ctx.address, ctx.old, ctx.new,
                                                tostring(maid.session.restoring)))
                    if ctx.address == 0x100 and not maid.session.restoring then maid.state.prologue = (maid.state.prologue or 0) + 1 end
                    if ctx.address == 0x101 then maid.state.repeats = (maid.state.repeats or 0) + 1 end
                end
                """)
            .Watch("G[0x100..]", 0x100, "mod.w", "watch.lua:on_write", count: 3);

    [Fact]
    public void AWatchSeesARestoresWritesAndNoneForTheLoadsBanksOrATitlesZeroing()
    {
        // Spec §7.3, gate 6 and gate 8: the load's banks and op 0x9's zeroing are no writes; the restored prologue's write
        // is, with the flag set; the write in the repeated range is delivered again after ome.session.resumed.
        string saves = Saves();
        Scripting first = Native(Watching());
        Run(Root(), first, Store(saves));
        Assert.Equal(["0x100 0->5 restoring=false", "0x101 0->1 restoring=false"], Lines(first, "mod.w"));
        Assert.Equal("""{"s:prologue":1,"s:repeats":1}""", Block(saves, "mod.w"));

        Scripting loaded = Native(Watching());
        Run(Loader(), loaded, Store(saves), Provider(CallbackLoad()));
        Assert.Equal(["0x102 0->1 restoring=true", "0x100 5->5 restoring=true", "0x101 1->1 restoring=false"],
                     Lines(loaded, "mod.w"));
        // The total that honours the flag is the same after save, load and resume; the other counted its write twice.
        Assert.Equal("""{"s:prologue":1,"s:repeats":2}""", Block(saves, "mod.w"));

        // A return to title zeroes the banks, which no watch sees: TITLE's write finds the cell at 0. (The second run's save
        // holds CALLBACK_LOAD's 1, which this load's banks bring back unseen.)
        Script title = ScriptAssembler.Assemble(Table, "TITLE.BIN", new List<(int, Operand[])>
        {
            (Mov, [G(0x100), Imm(2)]),
            (0x2, []),
        }, []);
        Scripting abandoned = Native(Watching());
        Run(Loader(), abandoned, Store(saves), Provider(CallbackLoad(returnToTitle: true), title));
        Assert.Equal(["0x102 1->1 restoring=true", "0x100 0->2 restoring=false"], Lines(abandoned, "mod.w"));
    }

    [Fact]
    public void ADataOnlyLoadDropsTheRecordsItsInstructionMadeAndTheNextSessionGetsItsOwn()
    {
        // Spec §7.3, §5.7: the before hook's write to G[0x110] and op 0x19f's status write to G[0x20] go with the session
        // the load ends; the next instruction's write is delivered in the load's session.
        string saves = Saves();
        Run(Root(), Native(Counting()), Store(saves));
        Script dataOnly = ScriptAssembler.Assemble(Table, "DATAONLY.BIN", new List<(int, Operand[])>
        {
            (0x19f, [G(0x20), Imm(1)]),
            (Mov, [G(0x102), Imm(3)]),
            (0x2, []),
        }, []);
        LuaStackBuilder builder = new LuaStackBuilder().Mod("mod.a", """
            -- file: hooks.lua
            function before_load(ctx) maid.vm.g[0x110] = 1 end
            function on_write(ctx)
                maid.log.info(string.format("0x%x %d->%d %s", ctx.address, ctx.old, ctx.new, maid.session.reason))
            end
            """)
            .Point(dataOnly, 0, "mod.a", "hooks.lua:before_load")
            .Watch("G[0x20]", 0x20, "mod.a", "hooks.lua:on_write")
            .Watch("G[0x102]", 0x102, "mod.a", "hooks.lua:on_write")
            .Watch("G[0x110]", 0x110, "mod.a", "hooks.lua:on_write");
        Scripting native = Native(builder);

        VirtualMachine vm = Run(dataOnly, native, Store(saves));

        Assert.Equal((0, 3, 0), (vm.Globals[0x20], vm.Globals[0x102], vm.Globals.GetValueOrDefault(0x110)));   // the banks are the save's
        Assert.Equal(["0x102 0->3 load"], Lines(native));
    }

    [Fact]
    public void AnAbandonedLoadEndsWithTheFlagSetAndTheTitleSessionStartsClear()
    {
        string saves = Saves();
        Run(Root(), Native(Counting()), Store(saves));
        Script title = ScriptAssembler.Assemble(Table, "TITLE.BIN", new List<(int, Operand[])>
        {
            (Mov, [G(0x104), Imm(1)]),
            (0x2, []),
        }, []);
        Scripting native = Native(Counting());

        VirtualMachine vm = Run(Loader(), native, Store(saves), Provider(CallbackLoad(returnToTitle: true), title));

        Assert.Equal(1, vm.Globals[0x104]);
        Assert.Equal(
        [
            "start boot restoring=false count=nil local=nil version=nil",
            "start load restoring=true count=1 local=nil version=1.0.0",
            "keys integer key | string key",
            "callback restoring=true",
            // CALLBACK_LOAD returned to title: no frame was restored, and the title session starts clear and empty.
            "start title restoring=false count=nil local=nil version=nil",
        ], Lines(native));
    }

    [Fact]
    public void SaveWritingTakesItsPlaceInTheSaveInstructionsListByPriority()
    {
        string saves = Saves();
        Script root = Root();
        LuaStackBuilder builder = new LuaStackBuilder()
            .Mod("mod.a", """
                -- file: hooks.lua
                function early(e) maid.log.info("writing " .. e.slot) end
                function late(ctx) maid.log.info("point " .. ctx.args[2].value) end
                function first(ctx) maid.log.info("point first") end
                """)
            .Point(root, 4, "mod.a", "hooks.lua:late", priority: 5)
            .Point(root, 4, "mod.a", "hooks.lua:first", priority: -5)
            .Engine(Scripting.SaveWritingEvent, "mod.a", "hooks.lua:early");

        Scripting native = Native(builder);
        Run(root, native, Store(saves));

        Assert.Equal(["point first", "writing 1", "point 1"], Lines(native));
    }

    // ----------------------------------------------------------------------------------------- the state's form

    [Fact]
    public void TheStateFormKeepsKeysTypesAndOrderAndReadsBackWhatItWrote()
    {
        var limits = ModLimits.Default;
        var nested = new LuaTable { [1] = "a", [2] = "b", [3] = "c" };
        var holes = new LuaTable { [1] = true, [3] = false };
        var state = new LuaTable
        {
            [1] = "integer one", ["1"] = "string one", ["zeta"] = 0.1 + 0.2, ["alpha"] = -0.0, ["big"] = 1e20,
            ["list"] = nested, ["holes"] = holes, ["text"] = "姫狩り \"q\" \\ \n\0", [-7] = 12345678901234,
            ["empty"] = new LuaTable(),
        };

        string json = LuaStateCodec.Encode(state, limits);

        Assert.Equal("""{"i:-7":12345678901234,"i:1":"integer one","s:1":"string one","s:alpha":-0,"s:big":1E+20,"s:empty":{},"s:holes":{"i:1":true,"i:3":false},"s:list":["a","b","c"],"s:text":"姫狩り \"q\" \\ \n\u0000","s:zeta":0.30000000000000004}""",
                     json);
        LuaTable back = LuaStateCodec.Decode(json, limits);
        Assert.Equal(json, LuaStateCodec.Encode(back, limits));
        Assert.Equal("integer one", back[1].Read<string>());
        Assert.Equal("string one", back["1"].Read<string>());
        Assert.True(double.IsNegative(back["alpha"].Read<double>()));
        Assert.Equal(0.1 + 0.2, back["zeta"].Read<double>());
    }

    [Fact]
    public void TheStateFormRefusesWhatASaveCannotHoldAndNamesWhere()
    {
        var limits = ModLimits.Default with { StateDepth = 4, StateCharacters = 64 };
        var cycle = new LuaTable();
        cycle["self"] = cycle;
        var deep = new LuaTable { ["a"] = new LuaTable { ["b"] = new LuaTable { ["c"] = new LuaTable { ["d"] = new LuaTable() } } } };
        (LuaValue State, string Message)[] cases =
        [
            (new LuaTable { ["f"] = new LuaFunction("f", (context, _) => new ValueTask<int>(0)) },
             "maid.state.f is a function; a save holds booleans, numbers, strings and tables"),
            (new LuaTable { ["x"] = double.PositiveInfinity }, "maid.state.x is inf, which a save cannot hold"),
            (new LuaTable { ["x"] = new LuaTable { [1] = double.NaN } }, "maid.state.x[1] is nan, which a save cannot hold"),
            (new LuaTable { [1.5] = 1 }, "maid.state has a key that is the number 1.5; a saved key is a string or an integer"),
            (new LuaTable { [true] = 1 }, "maid.state has a key that is the boolean true; a saved key is a string or an integer"),
            (cycle, "maid.state.self is a table that contains itself (a cycle)"),
            (deep, "maid.state.a.b.c.d nests tables more than 4 deep"),
            (new LuaTable { ["s"] = new string('x', 100) }, "maid.state is larger than 64 characters as saved (at maid.state.s)"),
            ("text", "maid.state is a string; it must be a table"),
        ];
        foreach (var (value, message) in cases)
            Assert.Equal(message, Assert.Throws<LuaStateException>(() => LuaStateCodec.Encode(value, limits)).Message);
        Assert.Equal("{}", LuaStateCodec.Encode(LuaValue.Nil, limits));
        Assert.Contains("is marked neither s: nor i:",
                        Assert.Throws<LuaStateException>(() => LuaStateCodec.Decode("""{"count":1}""", limits)).Message);
    }
}
