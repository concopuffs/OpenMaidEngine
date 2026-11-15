using System.Diagnostics;
using System.Text.Json;
using Lua;
using Lua.CodeAnalysis.Syntax;
using Lua.CodeAnalysis.Syntax.Nodes;
using Lua.Runtime;
using Lua.Standard;

namespace Ome.Scripting.Lua.Spike;

/// <summary>Spec §5.1's criteria against Lua-CSharp (LuaCSharp on NuGet).</summary>
public static class LuaCSharpCases
{
    const string Lib = "luacsharp";

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>ValueTasks that did not complete synchronously (criterion 7).</summary>
    static int s_asyncCompletions;

    static T Sync<T>(ValueTask<T> task)
    {
        if (task.IsCompleted) return task.Result;
        s_asyncCompletions++;
        return task.AsTask().GetAwaiter().GetResult();
    }

    static void Sync(ValueTask task)
    {
        if (task.IsCompleted) { task.GetAwaiter().GetResult(); return; }
        s_asyncCompletions++;
        task.AsTask().GetAwaiter().GetResult();
    }

    static LuaState NewState()
    {
        var state = LuaState.Create();
        state.OpenBasicLibrary();
        state.OpenStringLibrary();
        state.OpenTableLibrary();
        state.OpenMathLibrary();
        state.OpenBitwiseLibrary();
        state.OpenCoroutineLibrary();
        return state;
    }

    static LuaFunction Fn(string name, Func<LuaFunctionExecutionContext, int> body)
        => new(name, (context, _) => new ValueTask<int>(body(context)));

    static LuaValue[] Run(LuaState state, string code, LuaTable? environment = null, CancellationToken token = default)
    {
        LuaClosure closure = state.Load(code.AsSpan(), "chunk", environment ?? state.Environment);
        return Sync(state.ExecuteAsync(closure, token));
    }

    static LuaValue[] Call(LuaState state, LuaValue function, params LuaValue[] arguments)
        => Sync(state.CallAsync(function, arguments, CancellationToken.None));

    /// <summary>A callback that sees the token of the call it runs under, so a nested call can pass it on.</summary>
    static LuaFunction FnT(string name, Func<LuaFunctionExecutionContext, CancellationToken, int> body)
        => new(name, (context, token) => new ValueTask<int>(body(context, token)));

    static LuaValue[] CallWith(LuaState state, LuaValue function, CancellationToken token, params LuaValue[] arguments)
        => Sync(state.CallAsync(function, arguments, token));

    static IEnumerable<Exception> Chain(Exception error)
    {
        for (Exception? e = error; e != null; e = e.InnerException) yield return e;
    }

    /// <summary>A mod's environment as §5.2 and §5.3 describe it: the kept basics, its own copies of the library
    /// tables, and a require that resolves only what <paramref name="modules"/> gives.</summary>
    static LuaTable ModEnvironment(LuaState state, Func<string, LuaValue>? require = null, List<string>? log = null)
    {
        var environment = new LuaTable();
        foreach (string name in Spike.Basics)
            if (state.Environment.TryGetValue(name, out LuaValue value)) environment[name] = value;
        foreach (string name in Spike.LibraryTables)
        {
            if (!state.Environment.TryGetValue(name, out LuaValue value) || value.Type != LuaValueType.Table) continue;
            var copy = new LuaTable();
            foreach (var pair in value.Read<LuaTable>()) copy[pair.Key] = pair.Value;
            if (name == "string") copy["dump"] = LuaValue.Nil;
            if (name == "math") copy["randomseed"] = LuaValue.Nil;
            environment[name] = copy;
        }
        environment["_G"] = environment;
        if (require != null)
            environment["require"] = Fn("require", context => context.Return(require(context.GetArgument<string>(0))));
        if (log != null)
            environment["print"] = Fn("print", context =>
            {
                log.Add(string.Join("\t", context.Arguments.ToArray().Select(v => v.ToString())));
                return context.Return();
            });
        return environment;
    }

    /// <summary>Lock the string metatable (§5.2): getmetatable("") gives a mod nothing it can change.</summary>
    static string LockStringMetatable(LuaState state)
        => Run(state, "local mt = getmetatable('') if mt == nil then return 'none' end mt.__metatable = false return 'locked'")[0].ToString();

    static CaseResult Result(string name, int criterion, string verdict, string detail, Dictionary<string, double>? metrics = null)
        => new(Lib, name, criterion, verdict, detail, metrics);

    // ---- read-only views (§5.2) ------------------------------------------------------------------------------------

    /// <summary>A requiring mod's view of a table another mod exports: a userdata, so rawset cannot reach it.</summary>
    sealed class View(LuaTable target, ViewFactory factory) : ILuaUserData
    {
        public LuaTable Target { get; } = target;
        public LuaTable Metatable { get => factory.Metatable; set { } }
        public Span<LuaValue> UserValues => Span<LuaValue>.Empty;
    }

    sealed class ViewFactory
    {
        readonly Dictionary<LuaTable, View> _views = new(ReferenceEqualityComparer.Instance);
        public LuaTable Metatable { get; } = new();

        public ViewFactory()
        {
            LuaFunction next = Fn("next", context =>
            {
                var view = (View)context.GetArgument(0).Read<ILuaUserData>();
                return view.Target.TryGetNext(context.GetArgument(1), out var pair)
                    ? context.Return(pair.Key, Wrap(pair.Value))
                    : context.Return(LuaValue.Nil);
            });
            Metatable["__index"] = Fn("__index", context =>
            {
                var view = (View)context.GetArgument(0).Read<ILuaUserData>();
                view.Target.TryGetValue(context.GetArgument(1), out LuaValue value);
                return context.Return(Wrap(value));
            });
            Metatable["__newindex"] = Fn("__newindex", context => throw new LuaRuntimeException(context.State, "read-only table", 1));
            Metatable["__len"] = Fn("__len", context => context.Return(((View)context.GetArgument(0).Read<ILuaUserData>()).Target.ArrayLength));
            Metatable["__pairs"] = Fn("__pairs", context => context.Return(next, context.GetArgument(0), LuaValue.Nil));
            Metatable["__ipairs"] = Fn("__ipairs", context =>
            {
                LuaFunction step = Fn("ipairs_step", inner =>
                {
                    var view = (View)inner.GetArgument(0).Read<ILuaUserData>();
                    double index = inner.GetArgument<double>(1) + 1;
                    view.Target.TryGetValue(index, out LuaValue value);
                    return value.Type == LuaValueType.Nil ? inner.Return(LuaValue.Nil) : inner.Return(index, Wrap(value));
                });
                return context.Return(step, context.GetArgument(0), 0);
            });
            Metatable["__metatable"] = "locked";
        }

        public LuaValue Wrap(LuaValue value)
        {
            if (value.Type != LuaValueType.Table) return value;
            LuaTable table = value.Read<LuaTable>();
            if (!_views.TryGetValue(table, out View? view)) _views[table] = view = new View(table, this);
            return LuaValue.FromUserData(view);
        }
    }

    /// <summary>An event's typed field, as a userdata over a C# value (§9.2), for the dispatch cost.</summary>
    sealed class EventView : ILuaUserData
    {
        public static readonly LuaTable Meta = new();
        public double Amount;
        public LuaTable Metatable { get => Meta; set { } }
        public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

        static EventView()
        {
            Meta["__index"] = Fn("__index", context =>
            {
                var e = (EventView)context.GetArgument(0).Read<ILuaUserData>();
                return context.GetArgument<string>(1) == "amount" ? context.Return(e.Amount) : context.Return(LuaValue.Nil);
            });
            Meta["__newindex"] = Fn("__newindex", context =>
            {
                var e = (EventView)context.GetArgument(0).Read<ILuaUserData>();
                if (context.GetArgument<string>(1) == "amount") e.Amount = context.GetArgument<double>(2);
                return context.Return();
            });
        }
    }

    /// <summary>A mod's accessors over a base mod's layout (§9.3): reads any name, writes the names it lists.</summary>
    sealed class Accessors(IReadOnlyDictionary<string, int> layout, IReadOnlySet<string> writes, Dictionary<int, double> cells) : ILuaUserData
    {
        readonly IReadOnlyDictionary<string, int> _layout = layout;
        readonly IReadOnlySet<string> _writes = writes;
        readonly Dictionary<int, double> _cells = cells;
        public static readonly LuaTable Meta = new();
        public LuaTable Metatable { get => Meta; set { } }
        public Span<LuaValue> UserValues => Span<LuaValue>.Empty;

        static Accessors()
        {
            Meta["__index"] = Fn("__index", context =>
            {
                var a = (Accessors)context.GetArgument(0).Read<ILuaUserData>();
                string name = context.GetArgument<string>(1);
                if (!a._layout.TryGetValue(name, out int address)) throw new LuaRuntimeException(context.State, $"no global named {name}", 1);
                return context.Return(a._cells.GetValueOrDefault(address));
            });
            Meta["__newindex"] = Fn("__newindex", context =>
            {
                var a = (Accessors)context.GetArgument(0).Read<ILuaUserData>();
                string name = context.GetArgument<string>(1);
                if (!a._writes.Contains(name)) throw new LuaRuntimeException(context.State, $"{name} is not in the mod's write list", 1);
                a._cells[a._layout[name]] = context.GetArgument<double>(2);
                return context.Return();
            });
            Meta["__metatable"] = "locked";
        }
    }

    // ---- criterion 1 ----------------------------------------------------------------------------------------------

    [Case(1, "c01-reentry")]
    public static CaseResult Reentry()
    {
        LuaState state = NewState();
        var marks = new List<long>();
        state.Environment["cs_reenter"] = Fn("cs_reenter", context =>
        {
            marks.Add(Spike.StackMark());
            LuaValue name = context.GetArgument(0);
            LuaValue x = context.GetArgument(1);
            LuaValue[] result = Call(context.State, context.State.Environment[name], x);
            return context.Return(result[0]);
        });
        Run(state, """
            function inner(x) return x * 2 end
            function middle(x) return cs_reenter("inner", x) + 1 end
            function outer(x) return cs_reenter("middle", x) + 10 end
            """);
        s_asyncCompletions = 0;
        double value = Call(state, state.Environment["outer"], 5)[0].Read<double>();
        bool ok = value == 21 && s_asyncCompletions == 0;
        double perLevel = marks.Count >= 2 ? Math.Abs(marks[0] - marks[1]) : -1;
        return Result("c01-reentry", 1, ok ? "pass" : "fail",
            $"outer(5) = {value} through two C# re-entries; async completions {s_asyncCompletions}; C# stack per re-entry level {perLevel} bytes",
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
            LuaState state = NewState();
            state.Environment["boom"] = Fn("boom", _ => throw new SpikeUnwindException("unwind"));
            state.Environment["cs_call"] = Fn("cs_call", context =>
            {
                Call(context.State, context.State.Environment[context.GetArgument(0)]);
                return context.Return();
            });
            Run(state, code);
            string outcome;
            try
            {
                Call(state, state.Environment["h"]);
                outcome = "caught by Lua";
                allReached = false;
            }
            catch (Exception error)
            {
                Exception? inner = error;
                while (inner != null && inner is not SpikeUnwindException) inner = inner.InnerException;
                bool luaRanOn = state.Environment["after"].ToBoolean() || state.Environment["handled"].ToBoolean()
                                || state.Environment["inner_after"].ToBoolean();
                if (inner == null) { outcome = $"became {error.GetType().Name}"; allReached = false; }
                else if (luaRanOn) { outcome = "reached C#, but Lua ran on after the pcall"; allReached = false; }
                else if (error is SpikeUnwindException) outcome = "ok";
                else { outcome = $"ok, wrapped in {error.GetType().Name}"; allUnwrapped = false; }
            }
            outcomes.Add($"{name}: {outcome}");
            state.Dispose();
        }
        LuaState fresh = NewState();
        bool freshWorks = Run(fresh, "return 1 + 1")[0].Read<double>() == 2;
        string verdict = allReached && freshWorks ? (allUnwrapped ? "pass" : "partial") : "fail";
        return Result("c02-unwind", 2, verdict, string.Join("; ", outcomes) + $"; disposed, and a new state works: {freshWorks}");
    }

    /// <summary>Criterion 2 with the unwind carried as a cancellation, two ways: the callback throws a subclass of
    /// OperationCanceledException (throw), or it cancels the call's token and returns (cancel).</summary>
    [Case(2, "c02-unwind-cancel")]
    public static CaseResult UnwindCancel()
    {
        var lines = new List<string>();
        var passed = new List<string>();
        foreach (string variant in new[] { "throw", "cancel" })
        {
            var outcomes = new List<string>();
            bool all = true;
            int stateWorks = 0;
            foreach (var (name, code) in UnwindScenarios)
            {
                LuaState state = NewState();
                using var source = new CancellationTokenSource();
                state.Environment["boom"] = FnT("boom", (context, _) =>
                {
                    if (variant == "throw") throw new SpikeUnwindCanceledException("unwind");
                    source.Cancel();
                    return context.Return();
                });
                state.Environment["cs_call"] = FnT("cs_call", (context, token) =>
                {
                    CallWith(context.State, context.State.Environment[context.GetArgument(0)], token);
                    return context.Return();
                });
                Run(state, code + " function ok() return 42 end");
                string outcome;
                try
                {
                    CallWith(state, state.Environment["h"], source.Token);
                    outcome = "caught by Lua";
                    all = false;
                }
                catch (Exception error)
                {
                    bool ranOn = state.Environment["after"].ToBoolean() || state.Environment["handled"].ToBoolean()
                                 || state.Environment["inner_after"].ToBoolean();
                    bool carried = Chain(error).Any(e => e is SpikeUnwindCanceledException)
                                   || (variant == "cancel" && error is OperationCanceledException);
                    if (!carried) { outcome = $"became {error.GetType().Name}: {Shorten(error.Message)}"; all = false; }
                    else if (ranOn) { outcome = "reached C#, but Lua ran on"; all = false; }
                    else outcome = error is SpikeUnwindCanceledException ? "ok" : $"ok as {error.GetType().Name}";
                }
                try { if (Call(state, state.Environment["ok"])[0].Read<double>() == 42) stateWorks++; }
                catch (Exception) { }
                outcomes.Add($"{name}: {outcome}");
                state.Dispose();
            }
            if (all) passed.Add(variant);
            lines.Add($"{variant} [{string.Join("; ", outcomes)}; the same state worked after in {stateWorks} of {UnwindScenarios.Length}]");
        }
        return Result("c02-unwind-cancel", 2, passed.Count > 0 ? "pass" : "fail",
            $"passing: [{string.Join(",", passed)}]; " + string.Join(" ", lines));
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
        ("coroutine.resume", "function h() while true do coroutine.resume(coroutine.create(function() while true do end end)) end end"),
        ("table.sort", "function h() while true do pcall(table.sort, {3, 2, 1}, function(a, b) while true do end end) end end"),
        ("__index", "function h() local t = setmetatable({}, {__index = function() while true do end end}) while true do pcall(function() return t.x end) end end"),
        ("gsub", "function h() while true do pcall(string.gsub, 'abc', '%w', function() while true do end end) end end"),
        ("re-entry", "function inner() while true do end end function h() while true do pcall(cs_call, 'inner') end end"),
    ];

    /// <summary>Criterion 3 with an instruction-count hook. How the hook ends the call: throw (a non-Lua exception),
    /// oce (a subclass of OperationCanceledException) or cancel (it cancels the call's token and returns). A pass needs
    /// every scenario stopped, and the state then still working: plain calls, pcall catching an ordinary error, and the
    /// same handler stopped again by a second budget.</summary>
    static CaseResult BudgetWith(string caseName, string mode, string description)
    {
        const long budget = 2_000_000;
        var outcomes = new List<string>();
        bool all = true;
        foreach (var (name, code) in BudgetScenarios)
        {
            LuaState state = NewState();
            long used = 0;
            CancellationTokenSource current = new();
            state.SetHook(Fn("hook", context =>
            {
                used += 1000;
                if (used > budget)
                {
                    if (mode == "throw") throw new SpikeBudgetException();
                    if (mode == "oce") throw new SpikeBudgetCanceledException();
                    current.Cancel();
                }
                return context.Return();
            }), "", 1000);
            state.Environment["cs_call"] = FnT("cs_call", (context, token) =>
            {
                CallWith(context.State, context.State.Environment[context.GetArgument(0)], token);
                return context.Return();
            });
            Run(state, code + " function ok() return 42 end function check() local ok, e = pcall(error, 'x') return (not ok) and e == 'x' end");
            bool IsBudget(Exception? error) => error != null && (mode == "cancel"
                ? error is OperationCanceledException
                : Chain(error).Any(e => e is SpikeBudgetException or SpikeBudgetCanceledException));
            string Attempt(out bool stopped, out bool hung)
            {
                used = 0;
                current = new CancellationTokenSource();
                var watch = Stopwatch.StartNew();
                hung = !Spike.RunWithin(5000, () => CallWith(state, state.Environment["h"], current.Token), out Exception? error);
                stopped = !hung && IsBudget(error);
                if (hung) return "not stopped within 5 s";
                if (stopped) return $"stopped in {watch.ElapsedMilliseconds} ms" + (error is OperationCanceledException ? $" ({error.GetType().Name})" : "");
                return $"ended with {error?.GetType().Name ?? "no error"}: {Shorten(error?.Message ?? "")}";
            }
            string outcome = Attempt(out bool stopped, out bool hung);
            if (stopped)
            {
                used = 0;
                bool works = false;
                try { works = Call(state, state.Environment["ok"])[0].Read<double>() == 42 && Call(state, state.Environment["check"])[0].ToBoolean(); }
                catch (Exception) { }
                string again = Attempt(out bool stoppedAgain, out hung);
                if (!works) outcome += ", and then the state no longer works";
                if (!stoppedAgain) outcome += $", but a second budget: {again}";
                all &= works && stoppedAgain;
            }
            else all = false;
            outcomes.Add($"{name}: {outcome}");
            if (hung) break;   // a thread is still running the loop
        }
        return Result(caseName, 3, all ? "pass" : "fail", description + ": " + string.Join("; ", outcomes));
    }

    [Case(3, "c03-budget")]
    public static CaseResult Budget() => BudgetWith("c03-budget", "throw",
        "instruction-count hook every 1000 instructions, throwing a non-Lua exception");

    [Case(3, "c03-budget-oce")]
    public static CaseResult BudgetCanceled() => BudgetWith("c03-budget-oce", "oce",
        "instruction-count hook every 1000 instructions, throwing a subclass of OperationCanceledException");

    [Case(3, "c03-budget-hookcancel")]
    public static CaseResult BudgetHookCancel() => BudgetWith("c03-budget-hookcancel", "cancel",
        "instruction-count hook every 1000 instructions, cancelling the call's token and returning");

    /// <summary>What the instruction hook costs (§7.5): a counted loop with no hook, and with the count hook.</summary>
    [Case(6, "c06-hook-cost")]
    public static CaseResult HookCost()
    {
        long instructions = 0;
        double Time(int? every, string code = "function spin(n) local x = 0 for i = 1, n do x = x + i end return x end")
        {
            LuaState state = NewState();
            long calls = 0;
            if (every is int count) state.SetHook(Fn("hook", context => { calls++; return context.Return(); }), "", count);
            Run(state, code);
            var warm = Stopwatch.StartNew();
            while (warm.ElapsedMilliseconds < 500) Call(state, state.Environment["spin"], 100_000);
            double best = double.MaxValue;
            for (int round = 0; round < 3; round++)
            {
                calls = 0;
                var watch = Stopwatch.StartNew();
                Call(state, state.Environment["spin"], 10_000_000);
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
                if (every == 1000) instructions = calls * 1000;
            }
            return best;
        }
        double none = Time(null), thousand = Time(1000);
        double perInstruction = thousand * 1e6 / instructions;
        double hundred = Time(100);
        // A mixed body: table reads and writes, a string concatenation, a call.
        const string mixed = """
            local function add(a, b) return a + b end
            function spin(n) local t = { 0 } local s = "" for i = 1, n do t[1] = add(t[1], i) if i % 1000 == 0 then s = "x" .. i end end return t[1] end
            """;
        double mixedTime = Time(1000, mixed);
        double mixedPer = mixedTime * 1e6 / instructions;
        return Result("c06-hook-cost", 6, "info",
            $"10,000,000 loop iterations: no hook {none:F0} ms, count hook every 1000 instructions {thousand:F0} ms, every 100 {hundred:F0} ms; "
            + $"{instructions:N0} instructions counted, {perInstruction:F1} ns each; a mixed loop body (table, call, concatenation) {mixedPer:F1} ns an instruction",
            new()
            {
                ["no_hook_ms"] = none, ["hook_1000_ms"] = thousand, ["hook_100_ms"] = hundred,
                ["ns_per_instruction"] = perInstruction, ["ns_per_instruction_mixed"] = mixedPer,
            });
    }

    [Case(3, "c03-budget-cancel")]
    public static CaseResult BudgetCancel()
    {
        // The other mechanism the library offers: a CancellationToken on the call.
        LuaState state = NewState();
        Run(state, "function h() while true do pcall(function() while true do end end) end end function ok() return 42 end");
        using var source = new CancellationTokenSource(200);
        Exception? error = null;
        bool ended = Spike.RunWithin(5000, () => Sync(state.CallAsync(state.Environment["h"], [], source.Token)), out error);
        if (!ended) return Result("c03-budget-cancel", 3, "fail", "a cancelled token did not stop a pcall loop within 5 s");
        bool works = Call(state, state.Environment["ok"])[0].Read<double>() == 42;
        return Result("c03-budget-cancel", 3, error is OperationCanceledException && works ? "pass" : "fail",
            $"a token cancelled after 200 ms ended a pcall loop with {error?.GetType().Name ?? "no error"}; the state works after: {works}");
    }

    // ---- criterion 4 ----------------------------------------------------------------------------------------------

    [Case(4, "c04-sandbox")]
    public static CaseResult Sandbox()
    {
        LuaState state = NewState();
        var modules = new Dictionary<string, string> { ["util.names"] = "loads = (loads or 0) + 1 return { greet = function() return 'hi' end }" };
        var cache = new Dictionary<string, LuaValue>();
        var log = new List<string>();
        LuaTable? environment = null;
        environment = ModEnvironment(state, name =>
        {
            if (cache.TryGetValue(name, out LuaValue cached)) return cached;
            if (!modules.TryGetValue(name, out string? source)) throw new LuaRuntimeException(state, $"module '{name}' not found", 1);
            return cache[name] = Run(state, source, environment)[0];
        }, log);
        string check = Run(state, """
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
            """, environment)[0].Read<string>();
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
        LuaState state = NewState();
        string locked = LockStringMetatable(state);
        LuaTable baseEnvironment = ModEnvironment(state);
        LuaTable exports = Run(state, "exports = { f = function() return 'orig' end, t = { x = 1 }, n = 3, list = { 10, 20, 30 } } return exports",
            baseEnvironment)[0].Read<LuaTable>();
        LuaTable ModWithView()
        {
            var factory = new ViewFactory();
            return ModEnvironment(state, name => name == "base" ? factory.Wrap(exports) : throw new LuaRuntimeException(state, "no module", 1));
        }
        LuaTable a = ModWithView(), b = ModWithView();
        Run(state, """
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
        string fromB = Run(state, """
            local base = require("base")
            local keys = {}
            for k, v in pairs(base.t) do keys[#keys + 1] = k .. "=" .. tostring(v) end
            local sum = 0
            for _, v in ipairs(base.list) do sum = sum + v end
            return tostring(secret) .. "|" .. string.format("%d", 5) .. "|" .. tostring(table.insert ~= nil) .. "|" .. tostring(math.floor(2.5))
                .. "|" .. ("x"):upper() .. "|" .. base.f() .. "|" .. tostring(base.t.x) .. "|" .. tostring(base.n) .. "|" .. tostring(#base.list)
                .. "|" .. table.concat(keys, ",") .. "|" .. tostring(sum)
            """, b)[0].Read<string>();
        string fromBase = Run(state, "return exports.f() .. '|' .. tostring(exports.t.x)", baseEnvironment)[0].Read<string>();
        LuaTable attempts = a["attempts"].Read<LuaTable>();
        var succeeded = new List<int>();
        for (int i = 1; i <= attempts.ArrayLength; i++) if (attempts[i].ToBoolean()) succeeded.Add(i);
        string[] p = fromB.Split('|');
        bool isolated = p[0] == "nil" && p[1] == "5" && p[2] == "true" && p[3] == "2" && p[4] == "X" && p[5] == "orig" && p[6] == "1";
        bool readable = p[7] == "3" && p[8] == "3" && p[9] == "x=1" && p[10] == "60";
        bool ok = isolated && fromBase == "orig|1" && succeeded.Count == 0 && locked == "locked";
        return Result("c05-isolation", 5, ok ? (readable ? "pass" : "partial") : "fail",
            $"string metatable {locked}; mod B sees: secret {p[0]}, string.format {p[1]}, table.insert {p[2]}, math.floor {p[3]}, (\"x\"):upper() {p[4]}, base.f() {p[5]}, base.t.x {p[6]}; "
            + $"mod A's writes into the view that succeeded: [{string.Join(",", succeeded)}] of 6; getmetatable(view) {a["meta"]}; "
            + $"the base mod sees {fromBase}; through a view: n {p[7]}, #list {p[8]}, pairs {p[9]}, ipairs sum {p[10]}; mod A's own (\"x\"):upper() {a["own_upper"]}");
    }

    // ---- criteria 6 and 7 -----------------------------------------------------------------------------------------

    [Case(6, "c06-dispatch")]
    public static CaseResult Dispatch()
    {
        LuaState state = NewState();
        LuaTable environment = ModEnvironment(state);
        Run(state, "function noop() end function add(a, b) return a + b end function on_award(e) e.amount = e.amount * 2 end", environment);
        LuaValue noop = environment["noop"], add = environment["add"], onAward = environment["on_award"];
        var e = new EventView { Amount = 1 };
        LuaValue eventValue = LuaValue.FromUserData(e);
        const int count = 300_000;
        var arrays = Spike.Measure(count, () => Call(state, noop));
        var stack = Spike.Measure(count, () =>
        {
            int basePos = state.Stack.Count;
            state.Push(noop);
            int results = Sync(state.CallAsync(basePos, basePos, CancellationToken.None));
            state.Stack.PopUntil(basePos);
        });
        var adds = Spike.Measure(count, () =>
        {
            int basePos = state.Stack.Count;
            state.Push(add); state.Push(1); state.Push(2);
            Sync(state.CallAsync(basePos, basePos, CancellationToken.None));
            state.Stack.PopUntil(basePos);
        });
        var awards = Spike.Measure(count, () =>
        {
            e.Amount = 1;
            int basePos = state.Stack.Count;
            state.Push(onAward); state.Push(eventValue);
            Sync(state.CallAsync(basePos, basePos, CancellationToken.None));
            state.Stack.PopUntil(basePos);
        });
        bool correct = e.Amount == 2;
        // As the runtime will call (plan §3.1): a live token from the session's source, and the count hook installed.
        using var source = new CancellationTokenSource();
        CancellationToken live = source.Token;
        var tokened = Spike.Measure(count, () =>
        {
            int basePos = state.Stack.Count;
            state.Push(noop);
            Sync(state.CallAsync(basePos, basePos, live));
            state.Stack.PopUntil(basePos);
        });
        long used = 0;
        state.SetHook(Fn("hook", context => { used += 1000; return context.Return(); }), "", 1000);
        var hooked = Spike.Measure(count, () =>
        {
            e.Amount = 1;
            int basePos = state.Stack.Count;
            state.Push(onAward); state.Push(eventValue);
            Sync(state.CallAsync(basePos, basePos, live));
            state.Stack.PopUntil(basePos);
        });
        return Result("c06-dispatch", 6, correct ? "pass" : "fail",
            $"noop {arrays.Nanoseconds:F0} ns/{arrays.Bytes:F0} B with argument arrays, {stack.Nanoseconds:F0} ns/{stack.Bytes:F0} B on the stack, {tokened.Nanoseconds:F0} ns/{tokened.Bytes:F0} B with a live token; "
            + $"add(1, 2) {adds.Nanoseconds:F0} ns/{adds.Bytes:F0} B; on_award(e) doubling a userdata field {awards.Nanoseconds:F0} ns/{awards.Bytes:F0} B, "
            + $"{hooked.Nanoseconds:F0} ns/{hooked.Bytes:F0} B with a live token and the count hook",
            new()
            {
                ["noop_ns"] = stack.Nanoseconds, ["noop_bytes"] = stack.Bytes, ["noop_array_ns"] = arrays.Nanoseconds,
                ["noop_token_ns"] = tokened.Nanoseconds, ["noop_token_bytes"] = tokened.Bytes,
                ["add_ns"] = adds.Nanoseconds, ["award_ns"] = awards.Nanoseconds, ["award_bytes"] = awards.Bytes,
                ["award_token_hook_ns"] = hooked.Nanoseconds, ["award_token_hook_bytes"] = hooked.Bytes,
            });
    }

    [Case(7, "c07-sync")]
    public static CaseResult Synchronous()
    {
        LuaState state = NewState();
        int engineThread = Environment.CurrentManagedThreadId;
        int otherThreads = 0;
        state.Environment["cs_noop"] = Fn("cs_noop", context =>
        {
            if (Environment.CurrentManagedThreadId != engineThread) otherThreads++;
            return context.Return();
        });
        Run(state, "function h(e) cs_noop(e) cs_noop(e) end function loop(n) for i = 1, n do cs_noop() end end");
        s_asyncCompletions = 0;
        for (int i = 0; i < 100_000; i++) Call(state, state.Environment["h"], i);
        int asyncCalls = s_asyncCompletions;
        const int n = 1_000_000;
        var warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 500) Call(state, state.Environment["loop"], 10_000);
        double perCallback = double.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            var watch = Stopwatch.StartNew();
            Call(state, state.Environment["loop"], n);
            perCallback = Math.Min(perCallback, watch.Elapsed.TotalMilliseconds * 1e6 / n);
        }
        bool ok = asyncCalls == 0 && otherThreads == 0;
        return Result("c07-sync", 7, ok ? "pass" : "fail",
            $"100,000 calls, each calling C# twice: {asyncCalls} did not complete synchronously, {otherThreads} callbacks ran on another thread; "
            + $"a Lua-to-C# call costs {perCallback:F0} ns. The API is ValueTask-based: a call whose callbacks are all synchronous completes in place",
            new() { ["async_completions"] = asyncCalls, ["lua_to_cs_ns"] = perCallback });
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
            LuaState state = NewState();
            LockStringMetatable(state);
            LuaTable baseEnvironment = ModEnvironment(state);
            LuaTable exports = Run(state, init, baseEnvironment)[0].Read<LuaTable>();
            var cells = new Dictionary<int, double>();
            for (int mod = 0; mod < 5; mod++)
            {
                var factory = new ViewFactory();
                var accessors = new Accessors(layout, new HashSet<string> { "stage_turn_limit" }, cells);
                LuaTable environment = ModEnvironment(state, name => factory.Wrap(exports));
                environment["game_g"] = LuaValue.FromUserData(accessors);
                Run(state, init, environment);
            }
            watch.Stop();
            state.Dispose();
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
    public static CaseResult Licence() => SpikeLayout.Licence(Lib, "luacsharp", "0.5.8");

    // ---- criterion 10 ---------------------------------------------------------------------------------------------

    static CaseResult Recursion(string name, string code, LuaState? given = null)
    {
        LuaState state = given ?? NewState();
        Run(state, "depth = 0");
        string outcome;
        string verdict;
        try
        {
            LuaValue[] results = Run(state, code);
            outcome = "returned " + string.Join(",", results.Select(r => r.ToString()));
            verdict = "pass";
        }
        catch (Exception error)
        {
            outcome = $"{error.GetType().Name}: {Shorten(error.Message)}";
            verdict = error is LuaRuntimeException or LuaStackOverflowException ? "pass" : "partial";
        }
        double depth = state.Environment["depth"].Read<double>();
        bool works = Run(state, "return 2 + 2")[0].Read<double>() == 4;
        return Result(name, 10, works ? verdict : "fail", $"{outcome}; depth reached {depth}; the state works after: {works}",
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
        // Lua -> C# -> Lua without the engine's nesting cap (§5.5): the path the cap guards. Measure what one level
        // costs on the C# stack.
        LuaState state = NewState();
        var marks = new List<long>();
        state.Environment["cs_call"] = Fn("cs_call", context =>
        {
            if (marks.Count < 3) marks.Add(Spike.StackMark());
            Call(context.State, context.GetArgument(0));
            return context.Return();
        });
        CaseResult result = Recursion("c10-recursion-reentry", "local function f() depth = depth + 1 cs_call(f) end f() return 'done'", state);
        double perLevel = marks.Count >= 3 ? Math.Abs(marks[1] - marks[2]) : -1;
        return result with
        {
            Detail = result.Detail + $"; C# stack per Lua-C#-Lua level {perLevel} bytes; thread stack {Spike.StackBytes / 1024} KiB",
            Metrics = new() { ["depth"] = result.Metrics!["depth"], ["stack_bytes_per_level"] = perLevel },
        };
    }

    // ---- criterion 11 ---------------------------------------------------------------------------------------------

    [Case(11, "c11-pairs-order")]
    public static CaseResult PairsOrder()
    {
        LuaState state = NewState();
        string order = Run(state, Spike.PairsOrderSource)[0].Read<string>();
        return Result("c11-pairs-order", 11, "info", Spike.Digest(order.Split(',')),
            new() { ["string_hash"] = "probe-string".GetHashCode() });
    }

    // ---- extras ---------------------------------------------------------------------------------------------------

    [Case(0, "x-backtrack")]
    public static CaseResult Backtrack()
    {
        LuaState state = NewState();
        long used = 0;
        state.SetHook(Fn("hook", context =>
        {
            used += 1000;
            if (used > 2_000_000) throw new SpikeBudgetException();
            return context.Return();
        }), "", 1000);
        using var source = new CancellationTokenSource(1000);
        var watch = Stopwatch.StartNew();
        bool ended = Spike.RunWithin(10_000, () => Run(state, Spike.BacktrackSource, token: source.Token), out Exception? error);
        return Result("x-backtrack", 0, ended ? "info" : "info",
            ended ? $"ended after {watch.ElapsedMilliseconds} ms with {error?.GetType().Name ?? "a result"}"
                  : "a backtracking string.find ran past 10 s: neither the instruction hook nor a token cancelled after 1 s stopped it");
    }

    /// <summary>How strings count: LuaCSharp's strings are .NET strings (UTF-16), reference Lua's are bytes.</summary>
    [Case(0, "x-strings")]
    public static CaseResult Strings()
    {
        LuaState state = NewState();
        state.Environment["vm_text"] = "姫狩り";
        string r = Run(state, """
            local s = "日本語"
            return table.concat({ #s, string.byte(s, 1), s:sub(1, 1), #"\230\151\165", #string.char(255), ("abc"):upper(),
                #vm_text, vm_text:sub(1, 1), tostring(vm_text == "姫狩り") }, "|")
            """)[0].Read<string>();
        string[] p = r.Split('|');
        return Result("x-strings", 0, "info",
            $"#\"日本語\" {p[0]} (reference Lua: 9); string.byte(s, 1) {p[1]} (reference: 230); s:sub(1, 1) \"{p[2]}\"; #\"\\230\\151\\165\" {p[3]} (reference: 3, one character there); "
            + $"#string.char(255) {p[4]}; upper {p[5]}; a VM string passed in: # {p[6]}, sub(1, 1) \"{p[7]}\", equal to the literal {p[8]}");
    }

    [Case(0, "x-parse-check")]
    public static CaseResult ParseCheck()
    {
        const string source = """
            local game = require("himegari-base")
            local function helper(x) return x end
            function on_award(event)
                local function inner() end
                event.amount = event.amount * 2
                game.g.stage_turn_limit = 30
                game.table("unit_current_levels")[event.unit] = 5
            end
            M = {}
            function M.not_a_handler() end
            on_other = function() end
            if false then function hidden() end end
            """;
        var watch = Stopwatch.StartNew();
        LuaSyntaxTree tree = LuaSyntaxTree.Parse(source, "mod.lua");
        var handlers = new List<string>();
        var writes = new List<string>();
        foreach (SyntaxNode node in tree.Nodes)
            if (node is FunctionDeclarationStatementNode function and not LocalFunctionDeclarationStatementNode) handlers.Add(function.Name.ToString());
        void Scan(IEnumerable<SyntaxNode> nodes)
        {
            foreach (SyntaxNode node in nodes)
            {
                switch (node)
                {
                    case AssignmentStatementNode assignment:
                        foreach (SyntaxNode left in assignment.LeftNodes)
                        {
                            if (left is TableMemberAccessExpressionNode { TableNode: TableMemberAccessExpressionNode { MemberName: "g" } } member)
                                writes.Add(member.MemberName);
                            if (left is TableIndexerAccessExpressionNode { TableNode: CallFunctionExpressionNode { FunctionNode: TableMemberAccessExpressionNode { MemberName: "table" }, ArgumentNodes: [StringLiteralNode literal] } })
                                writes.Add(literal.Text.ToString());
                        }
                        break;
                    case FunctionDeclarationStatementNode function: Scan(function.Nodes); break;
                }
            }
        }
        Scan(tree.Nodes);
        watch.Stop();
        string syntax;
        try { LuaSyntaxTree.Parse("function broken(\n  return 1\nend", "bad.lua"); syntax = "not reported"; }
        catch (LuaParseException error) { syntax = $"reported at {error.Position}: {error.Message}"; }
        bool ok = handlers.SequenceEqual(["on_award"]) && writes.SequenceEqual(["stage_turn_limit", "unit_current_levels"]);
        return Result("x-parse-check", 0, ok ? "pass" : "partial",
            $"parsed without running in {watch.Elapsed.TotalMilliseconds:F2} ms; top-level global functions [{string.Join(",", handlers)}]; written-out writes [{string.Join(",", writes)}]; a syntax error is {syntax}");
    }
}
