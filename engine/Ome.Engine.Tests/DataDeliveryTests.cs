using System.Text.Json;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Linking;
using Ome.Mods.Packages;
using Ome.Script.Abi;
using Ome.Script.Patch;
using Ome.Scripting.Lua;

/// <summary>
/// The delivery of docs/superpowers/specs/2026-11-17-data-patches-design.md §5.2, §5.3, §5.5 to §5.7, linked from real
/// packages and run by the VM over a synthetic script: the contribution as the owner's handler gets it, the order, the
/// attribution in the trace, the watch records and the log, failures counted by file, the budget with its allowance,
/// a mod that is off, and the halt. Gates 1 (at run time), 3 (but the base mods' <c>data.ready</c>, which
/// <c>DataPatchWorkspaceTests</c> runs), 4 and 9: the owner's kind is a list of strings, and no stack here has a record
/// table or a base mod.
/// </summary>
public sealed class DataDeliveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ome-data-delivery-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModPackage> _opened = new();
    private readonly List<IDisposable> _owned = new();
    private readonly LazyModRuntime _runtime = LuaModRuntime.Lazy();

    private const int T_IMM = 0, T_GINT = 3;
    private static readonly OpcodeTable Table = OpcodeTableJson.Load(Paths.OpcodesJson);
    private static readonly OpcodeContracts Contracts = OpcodeContracts.Load(Paths.OpcodesJson);
    private const string AddExp = "ADDEXP.BIN";
    private const string Point = "himegari.experience.after_calculation";

    /// <summary>ADDEXP: G[0x100] = 1; G[0x101] = 2, the point, at 0x5; exit.</summary>
    private static readonly List<(int, Operand[])> AddExpCode =
    [
        (0x55, [new Operand(T_GINT, 0x100), new Operand(T_IMM, 1)]),
        (0x55, [new Operand(T_GINT, 0x101), new Operand(T_IMM, 2)]),
        (0x2, []),
    ];

    private static readonly byte[] AddExpBytes = ScriptAssembler.AssembleBytes(AddExpCode, [], Table.AbiId);

    /// <summary>SUB, which the owner may call: G[0x400] = 42; return.</summary>
    private static readonly List<(int, Operand[])> SubCode = [(0x55, [new Operand(T_GINT, 0x400), new Operand(T_IMM, 42)]), (0x5, [])];

    private const string Sub = "SUB.BIN";

    public void Dispose()
    {
        // The natives' sessions end before the runtime that built them.
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        foreach (ModPackage mod in _opened) mod.Dispose();
        _runtime.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ------------------------------------------------------------------------------------------------- packages

    private ModLinkBase Base() => new()
    {
        Profile = "himegari",
        CatalogFingerprint = "sha256:" + new string('1', 64),
        ReadScript = name => name.Equals(AddExp, StringComparison.OrdinalIgnoreCase) ? AddExpBytes
                             : name.Equals(Sub, StringComparison.OrdinalIgnoreCase) ? ScriptAssembler.AssembleBytes(SubCode, [], Table.AbiId)
                             : null,
        Table = Table,
        Contracts = Contracts,
        Intrinsics = new IntrinsicCatalog(Table, Contracts),
        PatchPoints = new OmsPatchPoints([new OmsPatchPoint(Point, AddExp, null,
            [new OmsPatchPointSite(OmsPatchPlan.FingerprintOf(AddExpBytes), 0x5)])]),
        Lua = _runtime,
    };

    /// <summary>The owner's events: at the point, raised from Lua, and at a point this install does not have.</summary>
    private const string OwnerEvents = """
        [[event]]
        name = "test.owner.award"
        point = "himegari.experience.after_calculation"

        [[event]]
        name = "test.owner.computed"
        raised_by = "lua"

        [[event]]
        name = "test.owner.unbound"
        point = "himegari.not.in.this.install"
        """;

    /// <summary>
    /// The owner's handler of <c>test.lists</c>: a document lists names and may set G[0x100] (<c>value</c>) and G[0x102]
    /// (<c>mark</c>); <c>fail</c> raises an error, <c>spin</c> never ends, and <c>probe</c> tries to write into the view
    /// and compares it with the one the file's last delivery got. It records what it saw in <c>seen</c>. With
    /// <c>before</c> it writes a cell of each kind (G[0x100], <c>before</c> cells from G[0x201], GS[0x10], GF[0x20] and
    /// the frame's L[0]), with <c>call</c> it calls SUB, and then it stops as <c>stop</c> says: an <c>error</c>, a write
    /// the engine's access refuses (<c>access</c>), or the <c>budget</c>. <c>copy_local</c> copies L[0] + 1000 to G[0x300].
    /// </summary>
    private const string Lists = """
        seen = {}
        local views = {}
        function apply(event, contribution)
            local data = contribution.data
            if data.fail then error("refused " .. contribution.file) end
            if data.problem then
                error(contribution.mod .. " " .. contribution.file .. ": 1 problem, so nothing of it is applied: x.y: no", 0)
            end
            if data.spin then while true do end end
            if data.before then
                maid.vm.g[0x100] = 99
                for i = 1, data.before do maid.vm.g[0x200 + i] = i end
                maid.vm.gs[0x10] = "written"
                maid.vm.gf[0x20] = 2.5
                maid.vm.l[0] = 9
                if data.call then maid.vm.call("SUB") end
                if data.stop == "error" then error("stopped after its writes") end
                if data.stop == "access" then maid.vm.g[0x100] = 1.5 end
                if data.stop == "budget" then while true do end end
            end
            if data.copy_local then maid.vm.g[0x300] = maid.vm.l[0] + 1000 end
            local names = {}
            if data.names then for _, name in ipairs(data.names) do names[#names + 1] = name end end
            local unraised = {}
            for _, name in ipairs(contribution.unraised) do unraised[#unraised + 1] = name end
            seen[#seen + 1] = contribution.mod .. " " .. contribution.file .. " [" .. table.concat(names, ",")
                              .. "] unraised " .. table.concat(unraised, ",")
            if data.value then maid.vm.g[0x100] = data.value end
            if data.mark then maid.vm.g[0x102] = data.mark end
            if data.probe then
                local wrote, written = pcall(function() data.names[1] = "z" end)
                local renamed, refused = pcall(function() contribution.mod = "x" end)
                local again = views[contribution.file] == nil and "first" or tostring(views[contribution.file] == contribution)
                views[contribution.file] = contribution
                probe = type(contribution) .. " " .. type(data) .. " " .. #data.names .. " " .. again .. " | "
                        .. tostring(wrote) .. " " .. written .. " | " .. tostring(renamed) .. " " .. refused
            end
        end
        """;

    private const string LuaTable = "[lua]\napi = 1\ncapabilities = [\"vm\"]\n\n";

    private const string Accepts = """
        [[accepts]]
        kind = "test.lists"
        description = "Lists of names."

        [[accepts.at]]
        event = "test.owner.award"
        handler = "lists.lua:apply"

        [[accepts.at]]
        event = "test.owner.computed"
        handler = "lists.lua:apply"

        [[accepts.at]]
        event = "test.owner.unbound"
        handler = "lists.lua:apply"

        """;

    private ModPackage Mod(string id, string tables, Dictionary<string, string>? files = null)
    {
        string folder = Path.Combine(_root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.toml"),
            $"omp = 4\nid = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\n\n[game]\nprofile = \"himegari\"\ntarget = \"extended\"\n\n"
            + tables + "\n");
        foreach (var (path, text) in files ?? new Dictionary<string, string>())
        {
            string file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        ModPackage mod = ModPackage.Open(folder);
        _opened.Add(mod);
        return mod;
    }

    private ModPackage Owner(string lists = Lists, string lua = LuaTable)
        => Mod("test.owner", lua + Accepts, new() { ["events/owner.toml"] = OwnerEvents, ["lua/lists.lua"] = lists });

    private static string Requires(params string[] ids)
        => string.Concat((ids.Length == 0 ? ["test.owner"] : ids).Select(id => $"[[requires]]\nid = \"{id}\"\nversion = \">=1\"\n\n"));

    private static string Data(string file, int? priority = null)
        => $"[[data]]\nkind = \"test.lists\"\nfile = \"{file}\"\n" + (priority is { } p ? $"priority = {p}\n" : "") + "\n";

    /// <summary>A mod with one data file and no Lua.</summary>
    private ModPackage Contributor(string id, string text, int? priority = null, string? requires = null)
        => Mod(id, (requires ?? Requires()) + Data($"{Short(id)}.toml", priority), new() { [$"data/{Short(id)}.toml"] = text });

    private static string Short(string id) => id[(id.LastIndexOf('.') + 1)..];

    /// <summary>A mod with one data file and a hook on the owner's event at the point, which records G[0x102] as it finds
    /// it, at the same priority as its file.</summary>
    private ModPackage LuaContributor(string id, string text)
        => Mod(id, LuaTable + Requires() + Data($"{Short(id)}.toml")
                   + $"[[hooks]]\nevent = \"test.owner.award\"\nhandler = \"{Short(id)}.lua:on_award\"\n",
            new()
            {
                [$"data/{Short(id)}.toml"] = text,
                [$"lua/{Short(id)}.lua"] = "function on_award(event) found = maid.vm.g[0x102] end\n",
            });

    private ModLinkResult Link(params ModPackage[] mods)
    {
        ModLinkResult result = ModLinker.Link(Base(), mods, mods.Select(mod => mod.Id).ToList());
        Assert.True(result.Succeeded, result.Report.Render());
        return result;
    }

    private Scripting Native(ModLinkResult result, LuaErrorPolicy errors = LuaErrorPolicy.Continue, ModLimits? limits = null)
    {
        var native = new Scripting(new ScriptMemory(), new ScriptingSetup
        {
            Runtime = _runtime, Stack = result.Lua.Stack, Plan = result.Lua.Plan, Errors = errors,
            Limits = limits ?? ModLimits.Default, Trace = true,
        });
        _owned.Add(native);
        return native;
    }

    private static VirtualMachine Run(Scripting native)
    {
        var sub = new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase)
        {
            [Sub] = ScriptAssembler.Assemble(Table, Sub, SubCode, []),
        };
        var vm = new VirtualMachine(ScriptAssembler.Assemble(Table, AddExp, AddExpCode, []), Table, new HeadlessWaits(),
                                    provider: new MapProvider(new Dictionary<long, Script>(), sub), scripting: native);
        vm.Run();
        return vm;
    }

    private static string Chunk(Scripting native, string mod, string code)
    {
        ConsoleRequest request = native.SubmitConsole(mod, code);
        native.ServiceRequests();
        Assert.True(request.Done);
        Assert.True(request.Result!.Succeeded, request.Result.Text);
        return request.Result.Text;
    }

    private static string Seen(Scripting native) => Chunk(native, "test.owner", "return table.concat(seen, '; ')");

    private static void Emit(Scripting native) => Chunk(native, "test.owner", "maid.events.emit('test.owner.computed', {})");

    private static List<JsonElement> Trace(Scripting native)
        => native.RecentTrace.Select(line => JsonDocument.Parse(line).RootElement).ToList();

    /// <summary>A trace line's call as the report names it: <c>MOD FILE (OWNER HANDLER)</c> or <c>MOD HANDLER</c>.</summary>
    private static string Named(JsonElement line)
        => line.TryGetProperty("file", out JsonElement file)
            ? $"{line.GetProperty("mod").GetString()} {file.GetString()} ({line.GetProperty("owner").GetString()} {line.GetProperty("handler").GetString()})"
            : $"{line.GetProperty("mod").GetString()} {line.GetProperty("handler").GetString()}";

    private static void NoErrors(Scripting native)
        => Assert.DoesNotContain(native.RecentLog, line => line.Contains(" error ", StringComparison.Ordinal));

    // ------------------------------------------------------------------------------------------- gate 1, at run time

    [Fact]
    public void AnOwnerWithoutAContributorRunsAsItDidAndItsHandlerFileIsNeverLoaded()
    {
        // The kind's file raises an error at its top level, which only loading it would show; a Lua mod's hook on the
        // kind's event is the event's only handler, with the handler budget.
        ModPackage owner = Owner(lists: "error('never loaded')\nfunction apply(event, contribution) end\n");
        ModPackage hook = Mod("test.hook", LuaTable + Requires()
                                           + "[[hooks]]\nevent = \"test.owner.award\"\nhandler = \"hook.lua:on_award\"\n",
            new() { ["lua/hook.lua"] = "function on_award(event) maid.vm.g[0x103] = 7 end\n" });
        ModLinkResult result = Link(owner, hook);
        Assert.Empty(result.Lua.Stack.ContributionList);
        Scripting native = Native(result);

        VirtualMachine vm = Run(native);

        Assert.Equal("exit", vm.HaltReason);
        NoErrors(native);
        Assert.Equal(0, native.NoticeCount);
        Assert.Equal(7, vm.Globals[0x103]);
        JsonElement line = Assert.Single(Trace(native));
        Assert.Equal("test.hook hook.lua:on_award", Named(line));
        Assert.Equal(ModLimits.Default.HandlerInstructions, line.GetProperty("budget").GetInt64());
        Assert.False(line.TryGetProperty("owner", out _));
    }

    // ------------------------------------------------------------------------------------- the contribution, gate 9

    [Fact]
    public void TheOwnerGetsTheContributionAsAReadOnlyViewMadeOnceForTheSession()
    {
        // Gate 9: the kind's documents are lists of strings, and the stack has no record table and no base mod.
        ModLinkResult result = Link(Owner(), Contributor("test.a", "names = [\"x\", \"y\"]\nprobe = true\n"));
        Assert.DoesNotContain(result.Lua.Stack.Mods, mod => mod.Layout != null);
        Scripting native = Native(result);

        Run(native);
        NoErrors(native);
        Assert.Equal("test.a data/a.toml [x,y] unraised test.owner.unbound", Seen(native));
        string first = Chunk(native, "test.owner", "return probe");
        Assert.StartsWith("userdata userdata 2 first | false ", first);
        Assert.Contains("a data file's document is read-only", first);
        Assert.Equal(2, first.Split("a data file's document is read-only").Length - 1);

        // The second delivery, at the event raised from Lua, gets the same view.
        Emit(native);
        Assert.StartsWith("userdata userdata 2 true | ", Chunk(native, "test.owner", "return probe"));
        Assert.Equal("test.a data/a.toml [x,y] unraised test.owner.unbound; test.a data/a.toml [x,y] unraised test.owner.unbound",
                     Seen(native));
    }

    // -------------------------------------------------------------------------------------------------- gate 3

    [Fact]
    public void TwoDataModsAndALuaModRunInTheReportsOrderAndTheLoadOrderDecidesWhichValueHolds()
    {
        (string Order, long Value, long Found) RunWith(bool swapped)
        {
            ModPackage owner = Owner();
            ModPackage a = Contributor("test.a", "value = 10\n");
            ModPackage b = Contributor("test.b", "value = 20\n");
            ModPackage c = LuaContributor("test.c", "mark = 7\n");
            ModLinkResult result = swapped ? Link(owner, b, a, c) : Link(owner, a, b, c);
            ModReportLine order = Assert.Single(result.Report.Lines,
                line => line.Class == ModReportClass.OrderedInteraction && line.Message.StartsWith("0x5 ", StringComparison.Ordinal));
            Scripting native = Native(result);
            VirtualMachine vm = Run(native);
            NoErrors(native);

            // The trace's calls at the point come in the order the report gives.
            List<string> calls = Trace(native).Select(Named).ToList();
            int at = -1;
            foreach (string call in calls)
            {
                int next = order.Message.IndexOf(call, StringComparison.Ordinal);
                Assert.True(next > at, $"{call} is out of the report's order: {order.Message}");
                at = next;
            }
            long found = long.Parse(Chunk(native, "test.c", "return tostring(found)"));
            foreach (ModPackage mod in new[] { owner, a, b, c })
            {
                _opened.Remove(mod);
                mod.Dispose();
            }
            Directory.Delete(_root, recursive: true);
            return (string.Join(" | ", calls), vm.Globals[0x100], found);
        }

        var (order, value, found) = RunWith(swapped: false);
        Assert.Equal("test.a data/a.toml (test.owner lists.lua:apply) | test.b data/b.toml (test.owner lists.lua:apply) | "
                     + "test.c data/c.toml (test.owner lists.lua:apply) | test.c c.lua:on_award", order);
        Assert.Equal(20, value);   // the later file's value holds
        Assert.Equal(7, found);    // test.c's own file was applied before its handler at the same priority

        (order, value, found) = RunWith(swapped: true);
        Assert.StartsWith("test.b data/b.toml (test.owner lists.lua:apply) | test.a data/a.toml", order);
        Assert.Equal(10, value);
        Assert.Equal(7, found);
    }

    [Fact]
    public void APriorityPutsAFileBeforeTheModsLoadedEarlier()
    {
        ModLinkResult result = Link(Owner(), Contributor("test.a", "value = 10\n"), Contributor("test.b", "value = 20\n", priority: -1));
        Scripting native = Native(result);
        VirtualMachine vm = Run(native);
        Assert.Equal(["test.b data/b.toml (test.owner lists.lua:apply)", "test.a data/a.toml (test.owner lists.lua:apply)"],
                     Trace(native).Select(Named));
        Assert.Equal(10, vm.Globals[0x100]);
    }

    // -------------------------------------------------------------------------------------------------- gate 4

    [Fact]
    public void TheTraceAndAWatchNameTheContributorAndItsFile()
    {
        ModPackage watcher = Mod("test.w", LuaTable + "[[hooks]]\nwatch = \"G[0x100]\"\nhandler = \"w.lua:on_write\"\n",
            new() { ["lua/w.lua"] = "function on_write(ctx) end\n" });
        ModLinkResult result = Link(Owner(), Contributor("test.a", "value = 10\nnames = [\"x\"]\n"), watcher);
        Scripting native = Native(result);
        Run(native);
        NoErrors(native);

        List<JsonElement> trace = Trace(native);
        JsonElement delivery = Assert.Single(trace, line => line.TryGetProperty("file", out _));
        Assert.Equal(("test.a", "data/a.toml", "test.owner", "lists.lua:apply", "test.owner.award", "event", "ok"),
                     (delivery.GetProperty("mod").GetString(), delivery.GetProperty("file").GetString(),
                      delivery.GetProperty("owner").GetString(), delivery.GetProperty("handler").GetString(),
                      delivery.GetProperty("hook").GetString(), delivery.GetProperty("kind").GetString(),
                      delivery.GetProperty("result").GetString()));
        // The budget: the handler's and the allowance for each of the document's three values, the array counting
        // one (plan §12.6). A write by the owner's access is through the owner, for the contributor.
        Assert.Equal(ModLimits.Default.HandlerInstructions + 3 * ModLimits.Default.DataInstructionsPerValue,
                     delivery.GetProperty("budget").GetInt64());
        Assert.True(delivery.GetProperty("instructions").GetInt64() >= 0);
        JsonElement write = Assert.Single(delivery.GetProperty("writes").EnumerateArray());
        Assert.Equal(("G[0x100]", "test.owner", 10), (write.GetProperty("cell").GetString(),
                                                      write.GetProperty("through").GetString(), write.GetProperty("after").GetInt32()));

        // The watch on the cell the file set names the contributor as its writer.
        JsonElement watch = Assert.Single(trace, line => line.GetProperty("kind").GetString() == "watch"
                                                         && line.GetProperty("writer").GetString() == "mod");
        Assert.Equal(("test.w", "mod", "test.a", 10), (watch.GetProperty("mod").GetString(), watch.GetProperty("writer").GetString(),
                                                       watch.GetProperty("by").GetString(), watch.GetProperty("new").GetInt32()));
    }

    [Fact]
    public void ThreeFailuresOfOneFileSwitchOffThatFileAndLeaveAnotherModsFileDelivered()
    {
        ModLinkResult result = Link(Owner(), Contributor("test.a", "names = [\"a\"]\n"), Contributor("test.bad", "fail = true\n"));
        Scripting native = Native(result);

        Run(native);                    // the point's delivery: the first failure
        Emit(native);                   // the second, at the other event
        Assert.Equal("Lua: test.bad data/bad.toml failed: [string \"test.owner/lua/lists.lua\"]:5: refused data/bad.toml",
                     native.LatestNotice);
        Emit(native);                   // the third switches the file off
        Assert.Equal("Lua: test.bad data/bad.toml failed 3 times in a row and is not applied again this session",
                     native.LatestNotice);
        Emit(native);                   // test.a's file is still delivered; test.bad's is not

        List<string> failures = native.RecentLog.Where(line => line.Contains(" error ", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, failures.Count);
        Assert.Contains(" error test.bad: data/bad.toml (test.owner lists.lua:apply) failed at test.owner.award at ADDEXP.BIN@0x5: ",
                        failures[0]);
        Assert.All(failures.Skip(1), line => Assert.Contains(
            " error test.bad: data/bad.toml (test.owner lists.lua:apply) failed at test.owner.computed: ", line));
        Assert.Equal(4, Seen(native).Split("; ").Count(entry => entry.StartsWith("test.a data/a.toml", StringComparison.Ordinal)));
        Assert.Equal(["ok", "error", "ok", "error", "ok", "error", "ok"],
                     Trace(native).Select(line => line.GetProperty("result").GetString()));
        Assert.Equal("test.bad", Trace(native)[1].GetProperty("mod").GetString());
    }

    [Fact]
    public void ANoticeAndAHaltReasonNameTheContributorOnceWhereTheOwnersMessageOpensWithIt()
    {
        // The owner reports as ome-records does, "<mod> <file>: 1 problem, …"; the log keeps the message as it is.
        ModLinkResult result = Link(Owner(), Contributor("test.bad", "problem = true\n"));
        Scripting native = Native(result);
        Run(native);
        Assert.Equal("Lua: test.bad data/bad.toml failed: 1 problem, so nothing of it is applied: x.y: no", native.LatestNotice);
        Assert.Contains(native.RecentLog, line => line.Contains(
            " error test.bad: data/bad.toml (test.owner lists.lua:apply) failed at test.owner.award at ADDEXP.BIN@0x5: "
            + "test.bad data/bad.toml: 1 problem, so nothing of it is applied: x.y: no", StringComparison.Ordinal));

        VirtualMachine halted = Run(Native(result, LuaErrorPolicy.Halt));
        Assert.Equal("lua-error: test.bad data/bad.toml (test.owner lists.lua:apply) at test.owner.award at ADDEXP.BIN@0x5: "
                     + "1 problem, so nothing of it is applied: x.y: no", halted.HaltReason);
    }

    [Fact]
    public void ADeliveryHasTheHandlerBudgetAndAnAllowanceForEachValue()
    {
        var limits = ModLimits.Default with { HandlerInstructions = 50_000, DataInstructionsPerValue = 10_000 };
        // Five values: spin, n, and n's three elements.
        ModLinkResult result = Link(Owner(), Contributor("test.spin", "spin = true\nn = [1, 2, 3]\n"));
        Scripting native = Native(result, limits: limits);

        Run(native);

        JsonElement line = Assert.Single(Trace(native));
        Assert.Equal(("budget", 100_000L), (line.GetProperty("result").GetString(), line.GetProperty("budget").GetInt64()));
        Assert.Contains(native.RecentLog, entry => entry.Contains(
            "error test.spin: data/spin.toml (test.owner lists.lua:apply) ran out of its instruction budget at test.owner.award "
            + "at ADDEXP.BIN@0x5: the call ran past its budget of 100,000 instructions", StringComparison.Ordinal));
        Assert.True(line.GetProperty("instructions").GetInt64() >= 100_000);
    }

    // ---------------------------------------------------------------------------------------- a mod that is off

    [Fact]
    public void AnOwnerThatIsOffDeliversNothingAndItsNoticeNamesTheFilesNotApplied()
    {
        ModLinkResult result = Link(Owner(lists: Lists + "\nerror('owner broken')\n"), Contributor("test.a", "value = 10\n"),
                                    LuaContributor("test.c", "mark = 7\n"));
        Scripting native = Native(result);

        VirtualMachine vm = Run(native);

        Assert.Equal("exit", vm.HaltReason);
        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Empty(native.RecentTrace);
        Assert.Contains(native.RecentLog, line => line.Contains(" error test.owner: off for this session: ", StringComparison.Ordinal)
                                                  && line.Contains("owner broken\nnot applied: test.a data/a.toml, test.c data/c.toml",
                                                                   StringComparison.Ordinal));
        // test.c requires the owner, so it is off too, and its notice names its own file.
        Assert.Equal("Lua: test.c is off for this session: it requires test.owner, which is off for this session; "
                     + "not applied: test.c data/c.toml", native.LatestNotice);
    }

    [Fact]
    public void AModWithNoLuaIsOffWhenAModItRequiresIsOff()
    {
        ModPackage helper = Mod("test.helper", LuaTable, new() { ["lua/init.lua"] = "error('helper broken')\n" });
        ModLinkResult result = Link(Owner(), helper, Contributor("test.a", "names = [\"a\"]\n"),
                                    Contributor("test.d", "value = 30\n", requires: Requires("test.owner", "test.helper")));
        Assert.Equal(["test.helper", "test.owner"], result.Lua.Stack.ContributionList[1].Requires.Order());
        Scripting native = Native(result);

        VirtualMachine vm = Run(native);

        Assert.Equal("Lua: test.helper is off for this session: [string \"test.helper/lua/init.lua\"]:1: helper broken; "
                     + "not applied: test.d data/d.toml", native.LatestNotice);
        Assert.Equal(1, vm.Globals[0x100]);
        Assert.Equal(["test.a data/a.toml (test.owner lists.lua:apply)"], Trace(native).Select(Named));
    }

    // ---------------------------------------------------------------------------------------------- the halt

    [Fact]
    public void UnderHaltADeliverysErrorEndsTheRunNamingTheContributorAndItsFile()
    {
        ModLinkResult result = Link(Owner(), Contributor("test.bad", "fail = true\n"), Contributor("test.z", "value = 5\n"));
        Scripting native = Native(result, LuaErrorPolicy.Halt);

        VirtualMachine vm = Run(native);

        Assert.Equal("lua-error: test.bad data/bad.toml (test.owner lists.lua:apply) at test.owner.award at ADDEXP.BIN@0x5: "
                     + "[string \"test.owner/lua/lists.lua\"]:5: refused data/bad.toml", vm.HaltReason);
        Assert.Single(native.RecentTrace);   // the later file is not delivered
        Assert.NotEqual(5, vm.Globals[0x100]);
    }

    // ------------------------------------------------------------------------- a failed delivery, plan §12.6

    /// <summary>The cells of the trace line's writes, in order.</summary>
    private static string[] Cells(JsonElement line)
        => line.GetProperty("writes").EnumerateArray().Select(write => write.GetProperty("cell").GetString()!).ToArray();

    [Theory]
    [InlineData("error", "error")]
    [InlineData("access", "error")]
    [InlineData("budget", "budget")]
    public void ADeliveryThatFailsAfterItsWritesLeavesEveryCellAsItWasAndTheNextFileIsStillDelivered(string stop, string result)
    {
        ModLinkResult linked = Link(Owner(), Contributor("test.bad", $"before = 3\nstop = \"{stop}\"\n"),
                                    Contributor("test.z", "mark = 5\ncopy_local = true\n"));
        Scripting native = Native(linked);

        VirtualMachine vm = Run(native);

        Assert.Equal("exit", vm.HaltReason);
        // ADDEXP's own G[0x100] = 1 holds, and the cells the file wrote that held nothing hold nothing again.
        Assert.Equal(1, vm.Globals[0x100]);
        Assert.DoesNotContain(vm.Globals.Keys, address => address is > 0x200 and <= 0x203);
        Assert.False(vm.GlobalStrings.ContainsKey(0x10));
        Assert.False(vm.GlobalFloats.ContainsKey(0x20));
        // The next file is delivered after it, in the same frame, and finds L[0] as the script left it.
        Assert.Equal(5, vm.Globals[0x102]);
        Assert.Equal(1000, vm.Globals[0x300]);

        // The trace line keeps the writes, to show how far the delivery got, and says how many cells went back.
        List<JsonElement> trace = Trace(native);
        JsonElement bad = Assert.Single(trace, line => line.GetProperty("mod").GetString() == "test.bad");
        Assert.Equal(result, bad.GetProperty("result").GetString());
        Assert.Equal(["G[0x100]", "G[0x201]", "G[0x202]", "G[0x203]", "GS[0x10]", "GF[0x20]", "L[0x0]"], Cells(bad));
        Assert.Equal(7, bad.GetProperty("put_back").GetInt32());
        JsonElement next = Assert.Single(trace, line => line.GetProperty("mod").GetString() == "test.z");
        Assert.Equal("ok", next.GetProperty("result").GetString());
        Assert.False(next.TryGetProperty("put_back", out _));
        string logged = Assert.Single(native.RecentLog, line => line.Contains(" error test.bad: ", StringComparison.Ordinal));
        Assert.Contains("\nthe 7 cells it wrote are put back", logged);
    }

    [Fact]
    public void AWatchIsToldOfAFailedDeliverysWriteAndThenOfTheEnginePuttingItBack()
    {
        ModPackage watcher = Mod("test.w", LuaTable + "[[hooks]]\nwatch = \"G[0x100]\"\nhandler = \"w.lua:on_write\"\n",
            new() { ["lua/w.lua"] = "function on_write(ctx) end\n" });
        Scripting native = Native(Link(Owner(), Contributor("test.bad", "before = 1\nstop = \"error\"\n"), watcher));

        VirtualMachine vm = Run(native);

        Assert.Equal(1, vm.Globals[0x100]);
        var watches = Trace(native).Where(line => line.GetProperty("kind").GetString() == "watch")
            .Select(line => (line.GetProperty("writer").GetString(), line.TryGetProperty("by", out JsonElement by) ? by.GetString() : null,
                             line.GetProperty("old").GetInt32(), line.GetProperty("new").GetInt32()))
            .ToList();
        Assert.Equal([("script", null, 0, 1), ("mod", "test.bad", 1, 99), ("engine", null, 99, 1)], watches);
    }

    [Fact]
    public void AScriptTheDeliveryCalledHasItsGlobalWritesPutBackWithTheDeliverysOwn()
    {
        ModLinkResult linked = Link(Owner(lua: "[lua]\napi = 1\ncapabilities = [\"vm\"]\ncalls = [\"SUB\"]\n\n"),
                                    Contributor("test.bad", "before = 1\ncall = true\nstop = \"error\"\n"));
        Scripting native = Native(linked);

        VirtualMachine vm = Run(native);

        Assert.Equal("exit", vm.HaltReason);
        Assert.False(vm.Globals.ContainsKey(0x400));
        Assert.Equal(1, vm.Globals[0x100]);
        JsonElement bad = Assert.Single(Trace(native), line => line.GetProperty("mod").GetString() == "test.bad");
        Assert.Equal("ok", bad.GetProperty("calls")[0].GetProperty("result").GetString());
        // SUB's G[0x400] is put back with the delivery's five cells; its frame, gone, has nothing to put back.
        Assert.Equal(6, bad.GetProperty("put_back").GetInt32());
    }

    [Fact]
    public void ADeliveryThatSucceedsKeepsItsWritesAndAHandlerThatIsNoDeliveryKeepsWhatItWroteBeforeItFailed()
    {
        ModPackage hook = Mod("test.hook", LuaTable + Requires()
                                           + "[[hooks]]\nevent = \"test.owner.award\"\nhandler = \"hook.lua:on_award\"\n",
            new() { ["lua/hook.lua"] = "function on_award(event) maid.vm.g[0x103] = 7 error('after its write') end\n" });
        Scripting native = Native(Link(Owner(), Contributor("test.a", "value = 10\n"), hook));

        VirtualMachine vm = Run(native);

        Assert.Equal((10, 7), (vm.Globals[0x100], vm.Globals[0x103]));
        Assert.All(Trace(native), line => Assert.False(line.TryGetProperty("put_back", out _)));
    }
}
