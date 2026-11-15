using System.Diagnostics;
using System.Text.Json;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Ome.Scripting.Lua.Spike;

/// <summary>Spec §5.1's criteria against MoonSharp 2.0.0.</summary>
public static class MoonSharpCases
{
    const string Lib = "moonsharp";

    static MoonSharpCases()
    {
        UserData.RegisterType<View>();
        UserData.RegisterType<EventView>();
        UserData.RegisterType<Accessors>();
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    const CoreModules Modules = CoreModules.Basic | CoreModules.GlobalConsts | CoreModules.TableIterators | CoreModules.Metatables
                                | CoreModules.String | CoreModules.Table | CoreModules.ErrorHandling | CoreModules.Math
                                | CoreModules.Coroutine | CoreModules.Bit32;

    static Script NewScript() => new(Modules);

    static DynValue Fn(Func<ScriptExecutionContext, CallbackArguments, DynValue> body, string name = "cs")
        => DynValue.NewCallback(body, name);

    static DynValue Run(Script script, string code, Table? environment = null)
        => script.DoString(code, environment ?? script.Globals, "chunk");

    static Table ModEnvironment(Script script, Func<string, DynValue>? require = null, List<string>? log = null)
    {
        var environment = new Table(script);
        foreach (string name in Spike.Basics)
            if (script.Globals.Get(name) is { } value && !value.IsNil()) environment.Set(name, value);
        foreach (string name in Spike.LibraryTables)
        {
            DynValue value = script.Globals.Get(name);
            if (value.Type != DataType.Table) continue;
            var copy = new Table(script);
            foreach (TablePair pair in value.Table.Pairs) copy.Set(pair.Key, pair.Value);
            if (name == "string") copy.Set("dump", DynValue.Nil);
            if (name == "math") copy.Set("randomseed", DynValue.Nil);
            environment.Set(name, DynValue.NewTable(copy));
        }
        environment.Set("_G", DynValue.NewTable(environment));
        if (require != null) environment.Set("require", Fn((_, args) => require(args[0].String), "require"));
        if (log != null)
            environment.Set("print", Fn((_, args) =>
            {
                log.Add(string.Join("\t", Enumerable.Range(0, args.Count).Select(i => args[i].ToPrintString())));
                return DynValue.Nil;
            }, "print"));
        return environment;
    }

    static string LockStringMetatable(Script script)
        => Run(script, "local mt = getmetatable('') if mt == nil then return 'none' end mt.__metatable = false return 'locked'").ToPrintString();

    static CaseResult Result(string name, int criterion, string verdict, string detail, Dictionary<string, double>? metrics = null)
        => new(Lib, name, criterion, verdict, detail, metrics);

    // ---- read-only views (§5.2) ------------------------------------------------------------------------------------

    sealed class View(Table target, ViewFactory factory) : IUserDataType
    {
        public Table Target { get; } = target;

        public DynValue Index(Script script, DynValue index, bool isDirectIndexing) => factory.Wrap(Target.Get(index));

        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
            => throw new ScriptRuntimeException("read-only table");

        public DynValue? MetaIndex(Script script, string metaname) => metaname switch
        {
            "__len" => Fn((_, _) => DynValue.NewNumber(Target.Length)),
            "__pairs" => Fn((_, args) => DynValue.NewTuple(factory.Next, args[0], DynValue.Nil)),
            "__ipairs" => Fn((_, args) => DynValue.NewTuple(factory.IpairsStep, args[0], DynValue.NewNumber(0))),
            "__metatable" => DynValue.NewString("locked"),
            _ => null,
        };
    }

    sealed class ViewFactory
    {
        readonly Dictionary<Table, DynValue> _views = new(ReferenceEqualityComparer.Instance);
        public DynValue Next { get; }
        public DynValue IpairsStep { get; }

        public ViewFactory()
        {
            Next = Fn((_, args) =>
            {
                var view = (View)args[0].UserData.Object;
                TablePair? pair = view.Target.NextKey(args[1]);
                return pair is { } p && !p.Key.IsNil() ? DynValue.NewTuple(p.Key, Wrap(p.Value)) : DynValue.Nil;
            });
            IpairsStep = Fn((_, args) =>
            {
                var view = (View)args[0].UserData.Object;
                double index = args[1].Number + 1;
                DynValue value = view.Target.Get(index);
                return value.IsNil() ? DynValue.Nil : DynValue.NewTuple(DynValue.NewNumber(index), Wrap(value));
            });
        }

        public DynValue Wrap(DynValue value)
        {
            if (value.Type != DataType.Table) return value;
            if (!_views.TryGetValue(value.Table, out DynValue? view))
                _views[value.Table] = view = UserData.Create(new View(value.Table, this));
            return view;
        }
    }

    sealed class EventView : IUserDataType
    {
        public double Amount;
        public DynValue Index(Script script, DynValue index, bool isDirectIndexing)
            => index.String == "amount" ? DynValue.NewNumber(Amount) : DynValue.Nil;
        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            if (index.String == "amount") Amount = value.Number;
            return true;
        }
        public DynValue? MetaIndex(Script script, string metaname) => null;
    }

    sealed class Accessors(IReadOnlyDictionary<string, int> layout, IReadOnlySet<string> writes, Dictionary<int, double> cells) : IUserDataType
    {
        public DynValue Index(Script script, DynValue index, bool isDirectIndexing)
            => layout.TryGetValue(index.String, out int address)
                ? DynValue.NewNumber(cells.GetValueOrDefault(address))
                : throw new ScriptRuntimeException($"no global named {index.String}");
        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            if (!writes.Contains(index.String)) throw new ScriptRuntimeException($"{index.String} is not in the mod's write list");
            cells[layout[index.String]] = value.Number;
            return true;
        }
        public DynValue? MetaIndex(Script script, string metaname) => metaname == "__metatable" ? DynValue.NewString("locked") : null;
    }

    // ---- criterion 1 ----------------------------------------------------------------------------------------------

    [Case(1, "c01-reentry")]
    public static CaseResult Reentry()
    {
        Script script = NewScript();
        var marks = new List<long>();
        script.Globals.Set("cs_reenter", Fn((context, args) =>
        {
            marks.Add(Spike.StackMark());
            return script.Call(script.Globals.Get(args[0].String), args[1]);
        }));
        Run(script, """
            function inner(x) return x * 2 end
            function middle(x) return cs_reenter("inner", x) + 1 end
            function outer(x) return cs_reenter("middle", x) + 10 end
            """);
        double value = script.Call(script.Globals.Get("outer"), 5).Number;
        double perLevel = marks.Count >= 2 ? Math.Abs(marks[0] - marks[1]) : -1;
        return Result("c01-reentry", 1, value == 21 ? "pass" : "fail",
            $"outer(5) = {value} through two C# re-entries; C# stack per re-entry level {perLevel} bytes",
            new() { ["stack_bytes_per_reentry"] = perLevel });
    }

    // ---- criterion 2 ----------------------------------------------------------------------------------------------

    static readonly (string Name, string Code)[] UnwindScenarios =
    [
        ("plain", "function h() boom() after = true end"),
        ("pcall", "function h() pcall(boom) after = true end"),
        ("xpcall", "function h() xpcall(boom, function(e) handled = true return e end) after = true end"),
        ("coroutine.wrap+pcall", "function h() coroutine.wrap(function() pcall(boom) end)() after = true end"),
        ("coroutine.resume", "function h() local co = coroutine.create(boom) coroutine.resume(co) after = true end"),
        ("__index", "function h() local t = setmetatable({}, {__index = function() boom() end}) pcall(function() return t.x end) after = true end"),
        ("table.sort", "function h() pcall(table.sort, {3, 2, 1}, function(a, b) boom() return a < b end) after = true end"),
        ("__tostring", "function h() pcall(tostring, setmetatable({}, {__tostring = function() boom() end})) after = true end"),
        ("gsub", "function h() pcall(string.gsub, 'abc', '%w', function() boom() end) after = true end"),
        ("re-entry+pcall", "function inner() pcall(boom) inner_after = true end function h() pcall(cs_call, 'inner') after = true end"),
    ];

    [Case(2, "c02-unwind")]
    public static CaseResult Unwind()
    {
        var outcomes = new List<string>();
        bool allReached = true, allUnwrapped = true;
        foreach (var (name, code) in UnwindScenarios)
        {
            Script script = NewScript();
            script.Globals.Set("boom", Fn((_, _) => throw new SpikeUnwindException("unwind")));
            script.Globals.Set("cs_call", Fn((_, args) => script.Call(script.Globals.Get(args[0].String))));
            Run(script, code);
            string outcome;
            try
            {
                script.Call(script.Globals.Get("h"));
                outcome = "caught by Lua";
                allReached = false;
            }
            catch (Exception error)
            {
                Exception? inner = error;
                while (inner != null && inner is not SpikeUnwindException) inner = inner.InnerException;
                bool luaRanOn = script.Globals.Get("after").CastToBool() || script.Globals.Get("handled").CastToBool()
                                || script.Globals.Get("inner_after").CastToBool();
                if (inner == null) { outcome = $"became {error.GetType().Name}"; allReached = false; }
                else if (luaRanOn) { outcome = "reached C#, but Lua ran on after the pcall"; allReached = false; }
                else if (error is SpikeUnwindException) outcome = "ok";
                else { outcome = $"ok, wrapped in {error.GetType().Name}"; allUnwrapped = false; }
            }
            outcomes.Add($"{name}: {outcome}");
        }
        bool freshWorks = Run(NewScript(), "return 1 + 1").Number == 2;
        string verdict = allReached && freshWorks ? (allUnwrapped ? "pass" : "partial") : "fail";
        return Result("c02-unwind", 2, verdict, string.Join("; ", outcomes) + $"; a new script works: {freshWorks}");
    }

    // ---- criterion 3 ----------------------------------------------------------------------------------------------

    static readonly (string Name, string Code)[] BudgetScenarios =
    [
        ("loop", "function h() while true do end end"),
        ("pcall loop", "function h() while true do pcall(function() while true do end end) end end"),
        ("pcall short", "function h() while true do pcall(function() for i = 1, 10 do end end) end end"),
        ("xpcall", "function h() while true do xpcall(function() while true do end end, function(e) return e end) end end"),
        ("coroutine", "function h() while true do coroutine.wrap(function() while true do end end)() end end"),
        ("coroutine pcall", "function h() while true do pcall(coroutine.wrap(function() while true do end end)) end end"),
        ("table.sort comparator", "function h() table.sort({3, 2, 1}, function(a, b) while true do end end) end"),
    ];

    /// <summary>MoonSharp's budget: run the handler as a coroutine whose AutoYieldCounter forces a yield after N
    /// instructions; a forced suspension is the budget stop, and the coroutine is abandoned.</summary>
    static DynValue CallBudgeted(Script script, DynValue function, long budget)
    {
        DynValue coroutine = script.CreateCoroutine(function);
        coroutine.Coroutine.AutoYieldCounter = budget;
        DynValue result = coroutine.Coroutine.Resume();
        if (coroutine.Coroutine.State == CoroutineState.ForceSuspended) throw new SpikeBudgetException();
        return result;
    }

    [Case(3, "c03-budget")]
    public static CaseResult Budget()
    {
        var outcomes = new List<string>();
        bool all = true;
        foreach (var (name, code) in BudgetScenarios)
        {
            Script script = NewScript();
            Run(script, code + " function ok() return 42 end");
            var watch = Stopwatch.StartNew();
            bool ended = Spike.RunWithin(5000, () => CallBudgeted(script, script.Globals.Get("h"), 2_000_000), out Exception? error);
            string outcome;
            if (!ended) { outcome = "not stopped within 5 s"; all = false; }
            else if (error is SpikeBudgetException)
            {
                bool works = CallBudgeted(script, script.Globals.Get("ok"), 2_000_000).Number == 42;
                outcome = $"stopped in {watch.ElapsedMilliseconds} ms" + (works ? "" : ", and the script no longer works");
                all &= works;
            }
            else { outcome = $"ended with {error?.GetType().Name ?? "no error"}: {error?.Message}"; all = false; }
            outcomes.Add($"{name}: {outcome}");
            if (!ended) break;
        }
        return Result("c03-budget", 3, all ? "pass" : "fail",
            "coroutine with AutoYieldCounter, a forced suspension abandoned: " + string.Join("; ", outcomes));
    }

    // ---- criterion 4 ----------------------------------------------------------------------------------------------

    [Case(4, "c04-sandbox")]
    public static CaseResult Sandbox()
    {
        Script script = NewScript();
        var modules = new Dictionary<string, string> { ["util.names"] = "loads = (loads or 0) + 1 return { greet = function() return 'hi' end }" };
        var cache = new Dictionary<string, DynValue>();
        var log = new List<string>();
        Table? environment = null;
        environment = ModEnvironment(script, name =>
        {
            if (cache.TryGetValue(name, out DynValue? cached)) return cached;
            if (!modules.TryGetValue(name, out string? source)) throw new ScriptRuntimeException($"module '{name}' not found");
            return cache[name] = Run(script, source, environment);
        }, log);
        string check = Run(script, """
            local missing = {}
            for _, name in ipairs({"io", "os", "debug", "package", "load", "loadstring", "dofile", "loadfile", "collectgarbage"}) do
                if _G[name] ~= nil then missing[#missing + 1] = name end
            end
            if string.dump ~= nil then missing[#missing + 1] = "string.dump" end
            if math.randomseed ~= nil then missing[#missing + 1] = "math.randomseed" end
            local names = require("util.names")
            local same = require("util.names") == names
            local ok = pcall(require, "missing")
            print("hello", 1)
            return table.concat(missing, ",") .. "|" .. names.greet() .. "|" .. tostring(same) .. "|" .. tostring(ok) .. "|" .. tostring(loads)
                .. "|" .. type(bit32) .. "|" .. type(coroutine)
            """, environment).String;
        string[] parts = check.Split('|');
        bool ok = parts[0] == "" && parts[1] == "hi" && parts[2] == "true" && parts[3] == "false" && parts[4] == "1"
                  && log.SequenceEqual(["hello\t1"]) && parts[5] == "table" && parts[6] == "table";
        return Result("c04-sandbox", 4, ok ? "pass" : "fail",
            $"still reachable: [{parts[0]}]; require: {parts[1]}, same table twice {parts[2]}, missing module refused {parts[3] == "false"}, loaded {parts[4]}x; "
            + $"print captured {JsonSerializer.Serialize(log)}; bit32 {parts[5]}, coroutine {parts[6]}");
    }

    // ---- criterion 5 ----------------------------------------------------------------------------------------------

    [Case(5, "c05-isolation")]
    public static CaseResult Isolation()
    {
        Script script = NewScript();
        string locked = LockStringMetatable(script);
        Table baseEnvironment = ModEnvironment(script);
        Table exports = Run(script, "exports = { f = function() return 'orig' end, t = { x = 1 }, n = 3, list = { 10, 20, 30 } } return exports",
            baseEnvironment).Table;
        Table ModWithView()
        {
            var factory = new ViewFactory();
            return ModEnvironment(script, name => name == "base" ? factory.Wrap(DynValue.NewTable(exports)) : throw new ScriptRuntimeException("no module"));
        }
        Table a = ModWithView(), b = ModWithView();
        Run(script, """
            secret = 1
            string.format = nil
            table.insert = nil
            math.floor = nil
            pcall(function() getmetatable("").__index.upper = function() return "pwn" end end)
            pcall(setmetatable, "", {})
            local base = require("base")
            attempts = {}
            attempts[#attempts + 1] = pcall(function() base.f = function() return "pwn" end end)
            attempts[#attempts + 1] = pcall(rawset, base, "f", 0)
            attempts[#attempts + 1] = pcall(function() base.t.x = 99 end)
            attempts[#attempts + 1] = pcall(rawset, base.t, "x", 99)
            attempts[#attempts + 1] = pcall(setmetatable, base, nil)
            attempts[#attempts + 1] = pcall(function() getmetatable(base).__index = nil end)
            meta = tostring(getmetatable(base))
            own_upper = ("x"):upper()
            """, a);
        string fromB = Run(script, """
            local base = require("base")
            local keys = {}
            for k, v in pairs(base.t) do keys[#keys + 1] = k .. "=" .. tostring(v) end
            local sum = 0
            for _, v in ipairs(base.list) do sum = sum + v end
            return tostring(secret) .. "|" .. string.format("%d", 5) .. "|" .. tostring(table.insert ~= nil) .. "|" .. tostring(math.floor(2.5))
                .. "|" .. ("x"):upper() .. "|" .. base.f() .. "|" .. tostring(base.t.x) .. "|" .. tostring(base.n) .. "|" .. tostring(#base.list)
                .. "|" .. table.concat(keys, ",") .. "|" .. tostring(sum)
            """, b).String;
        string fromBase = Run(script, "return exports.f() .. '|' .. tostring(exports.t.x)", baseEnvironment).String;
        Table attempts = a.Get("attempts").Table;
        var succeeded = new List<int>();
        for (int i = 1; i <= attempts.Length; i++) if (attempts.Get(i).CastToBool()) succeeded.Add(i);
        string[] p = fromB.Split('|');
        bool isolated = p[0] == "nil" && p[1] == "5" && p[2] == "true" && p[3] == "2" && p[4] == "X" && p[5] == "orig" && p[6] == "1";
        bool readable = p[7] == "3" && p[8] == "3" && p[9] == "x=1" && p[10] == "60";
        bool ok = isolated && fromBase == "orig|1" && succeeded.Count == 0 && locked == "locked";
        return Result("c05-isolation", 5, ok ? (readable ? "pass" : "partial") : "fail",
            $"string metatable {locked}; mod B sees: secret {p[0]}, string.format {p[1]}, table.insert {p[2]}, math.floor {p[3]}, (\"x\"):upper() {p[4]}, base.f() {p[5]}, base.t.x {p[6]}; "
            + $"mod A's writes into the view that succeeded: [{string.Join(",", succeeded)}] of 6; getmetatable(view) {a.Get("meta").ToPrintString()}; "
            + $"the base mod sees {fromBase}; through a view: n {p[7]}, #list {p[8]}, pairs {p[9]}, ipairs sum {p[10]}; mod A's own (\"x\"):upper() {a.Get("own_upper").ToPrintString()}");
    }

    // ---- criteria 6 and 7 -----------------------------------------------------------------------------------------

    [Case(6, "c06-dispatch")]
    public static CaseResult Dispatch()
    {
        Script script = NewScript();
        Table environment = ModEnvironment(script);
        Run(script, "function noop() end function add(a, b) return a + b end function on_award(e) e.amount = e.amount * 2 end", environment);
        DynValue noop = environment.Get("noop"), add = environment.Get("add"), onAward = environment.Get("on_award");
        var e = new EventView { Amount = 1 };
        DynValue eventValue = UserData.Create(e);
        DynValue one = DynValue.NewNumber(1), two = DynValue.NewNumber(2);
        const int count = 300_000;
        var noops = Spike.Measure(count, () => script.Call(noop));
        var adds = Spike.Measure(count, () => script.Call(add, one, two));
        var awards = Spike.Measure(count, () => { e.Amount = 1; script.Call(onAward, eventValue); });
        bool correct = e.Amount == 2;
        return Result("c06-dispatch", 6, correct ? "pass" : "fail",
            $"noop {noops.Nanoseconds:F0} ns/{noops.Bytes:F0} B; add(1, 2) {adds.Nanoseconds:F0} ns/{adds.Bytes:F0} B; "
            + $"on_award(e) doubling a userdata field {awards.Nanoseconds:F0} ns/{awards.Bytes:F0} B",
            new()
            {
                ["noop_ns"] = noops.Nanoseconds, ["noop_bytes"] = noops.Bytes, ["add_ns"] = adds.Nanoseconds,
                ["award_ns"] = awards.Nanoseconds, ["award_bytes"] = awards.Bytes,
            });
    }

    [Case(7, "c07-sync")]
    public static CaseResult Synchronous()
    {
        Script script = NewScript();
        int engineThread = Environment.CurrentManagedThreadId;
        int otherThreads = 0;
        script.Globals.Set("cs_noop", Fn((_, _) =>
        {
            if (Environment.CurrentManagedThreadId != engineThread) otherThreads++;
            return DynValue.Nil;
        }));
        Run(script, "function h(e) cs_noop(e) cs_noop(e) end function loop(n) for i = 1, n do cs_noop() end end");
        for (int i = 0; i < 100_000; i++) script.Call(script.Globals.Get("h"), i);
        const int n = 1_000_000;
        var warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 500) script.Call(script.Globals.Get("loop"), 10_000);
        double perCallback = double.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            var watch = Stopwatch.StartNew();
            script.Call(script.Globals.Get("loop"), n);
            perCallback = Math.Min(perCallback, watch.Elapsed.TotalMilliseconds * 1e6 / n);
        }
        return Result("c07-sync", 7, otherThreads == 0 ? "pass" : "fail",
            $"the API is synchronous; 100,000 calls, each calling C# twice: {otherThreads} callbacks ran on another thread; a Lua-to-C# call costs {perCallback:F0} ns",
            new() { ["lua_to_cs_ns"] = perCallback });
    }

    // ---- criterion 8 ----------------------------------------------------------------------------------------------

    [Case(8, "c08-session-build")]
    public static CaseResult SessionBuild()
    {
        var parse = Stopwatch.StartNew();
        Dictionary<string, int> layout = SpikeLayout.Load("kamidori");
        parse.Stop();
        const string init = """
            local M = {}
            function on_award(e) e.amount = e.amount * 2 end
            function on_start() end
            local function helper(x) return x + 1 end
            M.helper = helper
            M.data = { 1, 2, 3, name = "mod" }
            return M
            """;
        double Build()
        {
            var watch = Stopwatch.StartNew();
            Script script = NewScript();
            LockStringMetatable(script);
            Table baseEnvironment = ModEnvironment(script);
            DynValue exports = Run(script, init, baseEnvironment);
            var cells = new Dictionary<int, double>();
            for (int mod = 0; mod < 5; mod++)
            {
                var factory = new ViewFactory();
                var accessors = new Accessors(layout, new HashSet<string> { "stage_turn_limit" }, cells);
                Table environment = ModEnvironment(script, _ => factory.Wrap(exports));
                environment.Set("game_g", UserData.Create(accessors));
                Run(script, init, environment);
            }
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds;
        }
        double cold = Build();
        var warm = Enumerable.Range(0, 40).Select(_ => Build()).Order().ToArray();
        double median = warm[warm.Length / 2];
        return Result("c08-session-build", 8, median < 25 ? "pass" : "partial",
            $"a session with a base mod and 5 dependents (own environments and library copies, locked string metatable, views, accessors over Kamidori's {layout.Count} named globals, each running an init.lua): "
            + $"first {cold:F1} ms, then median {median:F2} ms, max {warm[^1]:F2} ms; reading the layout once took {parse.ElapsedMilliseconds} ms",
            new() { ["cold_ms"] = cold, ["warm_median_ms"] = median, ["layout_names"] = layout.Count, ["layout_parse_ms"] = parse.ElapsedMilliseconds });
    }

    // ---- criterion 9 ----------------------------------------------------------------------------------------------

    [Case(9, "c09-licence")]
    public static CaseResult Licence() => SpikeLayout.Licence(Lib, "moonsharp", "2.0.0");

    // ---- criterion 10 ---------------------------------------------------------------------------------------------

    static CaseResult Recursion(string name, string code, Script? given = null)
    {
        Script script = given ?? NewScript();
        Run(script, "depth = 0");
        string outcome, verdict;
        try
        {
            DynValue result = Run(script, code);
            outcome = "returned " + result.ToPrintString();
            verdict = "pass";
        }
        catch (Exception error)
        {
            outcome = $"{error.GetType().Name}: {Shorten(error.Message)}";
            verdict = error is InterpreterException ? "pass" : "partial";
        }
        double depth = script.Globals.Get("depth").Number;
        bool works = Run(script, "return 2 + 2").Number == 4;
        return Result(name, 10, works ? verdict : "fail", $"{outcome}; depth reached {depth}; the script works after: {works}",
            new() { ["depth"] = depth });
    }

    static string Shorten(string text) => text.Length > 120 ? text[..120] + "…" : text;

    [Case(10, "c10-recursion-plain")]
    public static CaseResult RecursionPlain() => Recursion("c10-recursion-plain", "local function f() depth = depth + 1 return 1 + f() end return f()");

    [Case(10, "c10-recursion-pcall")]
    public static CaseResult RecursionPcall() => Recursion("c10-recursion-pcall", "local function f() depth = depth + 1 pcall(f) end f() return 'done'");

    [Case(10, "c10-recursion-index")]
    public static CaseResult RecursionIndex() => Recursion("c10-recursion-index",
        "local t = setmetatable({}, {__index = function(t, k) depth = depth + 1 return t[k] end}) return t.x");

    [Case(10, "c10-recursion-sort")]
    public static CaseResult RecursionSort() => Recursion("c10-recursion-sort",
        "local function cmp(a, b) depth = depth + 1 table.sort({3, 2, 1}, cmp) return a < b end table.sort({3, 2, 1}, cmp) return 'done'");

    [Case(10, "c10-recursion-coroutine")]
    public static CaseResult RecursionCoroutine() => Recursion("c10-recursion-coroutine",
        "local function f() depth = depth + 1 coroutine.wrap(f)() end f() return 'done'");

    [Case(10, "c10-recursion-gsub")]
    public static CaseResult RecursionGsub() => Recursion("c10-recursion-gsub",
        "local function f() depth = depth + 1 string.gsub('a', 'a', f) end f() return 'done'");

    [Case(10, "c10-recursion-tostring")]
    public static CaseResult RecursionTostring() => Recursion("c10-recursion-tostring",
        "local t t = setmetatable({}, {__tostring = function() depth = depth + 1 return tostring(t) end}) return tostring(t)");

    [Case(10, "c10-recursion-reentry")]
    public static CaseResult RecursionReentry()
    {
        Script script = NewScript();
        var marks = new List<long>();
        script.Globals.Set("cs_call", Fn((_, args) =>
        {
            if (marks.Count < 3) marks.Add(Spike.StackMark());
            return script.Call(args[0]);
        }));
        CaseResult result = Recursion("c10-recursion-reentry", "local function f() depth = depth + 1 cs_call(f) end f() return 'done'", script);
        double perLevel = marks.Count >= 3 ? Math.Abs(marks[1] - marks[2]) : -1;
        return result with
        {
            Detail = result.Detail + $"; C# stack per Lua-C#-Lua level {perLevel} bytes",
            Metrics = new() { ["depth"] = result.Metrics!["depth"], ["stack_bytes_per_level"] = perLevel },
        };
    }

    // ---- criterion 11 ---------------------------------------------------------------------------------------------

    [Case(11, "c11-pairs-order")]
    public static CaseResult PairsOrder()
    {
        string order = Run(NewScript(), Spike.PairsOrderSource).String;
        return Result("c11-pairs-order", 11, "info", Spike.Digest(order.Split(',')),
            new() { ["string_hash"] = "probe-string".GetHashCode() });
    }

    // ---- extras ---------------------------------------------------------------------------------------------------

    [Case(0, "x-backtrack")]
    public static CaseResult Backtrack()
    {
        Script script = NewScript();
        Run(script, "function h() " + Spike.BacktrackSource + " end");
        var watch = Stopwatch.StartNew();
        bool ended = Spike.RunWithin(10_000, () => CallBudgeted(script, script.Globals.Get("h"), 2_000_000), out Exception? error);
        return Result("x-backtrack", 0, "info",
            ended ? $"ended after {watch.ElapsedMilliseconds} ms with {error?.GetType().Name ?? "a result"}"
                  : "a backtracking string.find ran past 10 s: the coroutine budget does not reach inside it");
    }

    [Case(0, "x-parse-check")]
    public static CaseResult ParseCheck()
    {
        Script script = NewScript();
        var watch = Stopwatch.StartNew();
        script.LoadString("function on_award(e) e.amount = e.amount * 2 end", null, "mod.lua");
        watch.Stop();
        string syntax;
        try { script.LoadString("function broken(\n  return 1\nend", null, "bad.lua"); syntax = "not reported"; }
        catch (SyntaxErrorException error) { syntax = $"reported: {error.DecoratedMessage}"; }
        return Result("x-parse-check", 0, "partial",
            $"compiling without running takes {watch.Elapsed.TotalMilliseconds:F2} ms and a syntax error is {syntax}; MoonSharp has no public syntax tree, so finding top-level functions and written-out writes needs a parser of our own");
    }
}
