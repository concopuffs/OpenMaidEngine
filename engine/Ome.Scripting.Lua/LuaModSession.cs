using System.Runtime.ExceptionServices;
using Lua;
using Lua.Runtime;
using Lua.Standard;
using Ome.Engine.Modding;

namespace Ome.Scripting.Lua;

/// <summary>
/// One session (spec §5.2, §5.7): one Lua state, and for each mod its own environment, library copies, <c>maid</c>
/// table and module cache. Every call into Lua follows plan §3.1: it carries the token of the session's source, an
/// engine exception inside a callback cancels the token and comes out as itself, and a budget stop cancels it too.
/// </summary>
internal sealed partial class LuaModSession : IModSession
{
    /// <summary>The basic functions the sandbox keeps (spec §5.3).</summary>
    internal static readonly string[] Basics =
    [
        "assert", "error", "pcall", "xpcall", "select", "type", "tostring", "tonumber", "pairs", "ipairs", "next",
        "rawget", "rawset", "rawequal", "setmetatable", "getmetatable", "unpack", "_VERSION",
    ];

    /// <summary>The library tables each mod gets its own copy of (spec §5.2).</summary>
    internal static readonly string[] LibraryTables = ["string", "table", "math", "bit32", "coroutine"];

    /// <summary>How often the count hook runs (plan §3.2).</summary>
    internal const int HookInterval = 1000;

    private readonly LuaState _state;
    private readonly ModRuntimeStack _stack;
    private readonly IScriptAccess _access;
    private readonly ModLimits _limits;
    private readonly SessionReason _reason;
    private readonly ModState _saved;
    private readonly Dictionary<string, ModEnvironment> _mods = new(StringComparer.Ordinal);
    private readonly LuaValue[] _handlers;
    private readonly List<ModBuildFailure> _failures = new();
    private ModEnvironment? _console;

    // The call in progress (plan §3.1).
    private CancellationTokenSource _source = new();
    private int _callDepth;
    private long _used, _budget;
    private bool _budgetStop;
    private ExceptionDispatchInfo? _kept;

    private LuaModSession(ModRuntimeStack stack, IScriptAccess access, ModState saved, SessionReason reason,
                          ModLimits limits)
    {
        _stack = stack;
        _access = access;
        _saved = saved;
        _reason = reason;
        _limits = limits;
        _handlers = new LuaValue[stack.Handlers.Count];
        _state = LuaState.Create();
        _state.OpenBasicLibrary();
        _state.OpenStringLibrary();
        _state.OpenTableLibrary();
        _state.OpenMathLibrary();
        _state.OpenBitwiseLibrary();
        _state.OpenCoroutineLibrary();
        LockStringMetatable();
        Metatables = new Metatables(this);
        _state.SetHook(new LuaFunction("budget", (context, _) =>
        {
            _used += HookInterval;
            if (_used > _budget && !_budgetStop)
            {
                _budgetStop = true;
                _source.Cancel();
            }
            return new ValueTask<int>(context.Return());
        }), "", HookInterval);
    }

    public static LuaModSession Build(ModRuntimeStack stack, IScriptAccess access, ModState state, SessionReason reason,
                                      ModLimits limits)
    {
        var session = new LuaModSession(stack, access, state, reason, limits);
        foreach (ModRuntimeMod mod in stack.Mods) session.BuildMod(mod);
        return session;
    }

    public IReadOnlyList<ModBuildFailure> Failures => _failures;

    public bool IsActive(HandlerId handler) => _handlers[handler.Index].Type == LuaValueType.Function;

    /// <summary>Spec §5.7 steps 2 and 3 for one mod: its environment with its saved state, its <c>init.lua</c>, the
    /// files its hooks name, and each handler's function. An error switches the mod off for the session; the scripting
    /// native then carries its saved state into the next save unchanged.</summary>
    private void BuildMod(ModRuntimeMod mod)
    {
        var environment = new ModEnvironment(mod.Id, mod, _access.Mod(mod.Id), this);
        _mods[mod.Id] = environment;
        string? offRequirement = mod.Requires.FirstOrDefault(id => _mods.TryGetValue(id, out var required) && required.Off);
        if (offRequirement != null)
        {
            SwitchOff(environment, $"it requires {offRequirement}, which is off for this session", null);
            return;
        }
        ModStateBlock? saved = _saved.Of(mod.Id);
        try
        {
            environment.SetState(saved == null ? new LuaTable() : LuaStateCodec.Decode(saved.Json, _limits), saved?.Version);
        }
        catch (LuaStateException error)
        {
            SwitchOff(environment, $"its saved state cannot be read: {error.Message}", null);
            return;
        }
        try
        {
            Guarded(_limits.StartupInstructions, startup: true, token =>
            {
                if (mod.Files.Any(file => file.Path == ModEnvironment.InitFile))
                    environment.Exports = environment.LoadModule(ModEnvironment.InitModule, token);
                foreach (ModRuntimeHandler handler in _stack.Handlers.Where(handler => handler.ModId == mod.Id))
                    environment.LoadModule(ModEnvironment.ModuleOf(handler.File), token);
                return 0;
            });
        }
        catch (ModScriptException error)
        {
            SwitchOff(environment, error.Message, error.LuaTraceback);
            return;
        }
        // Spec §5.4: what require gives for a base mod holds the accessors g and table beside its exports.
        if (mod.Layout != null && environment.Exports.TryRead(out LuaTable exports)
            && NamesView.AccessorNames.FirstOrDefault(name => exports[name].Type != LuaValueType.Nil) is { } taken)
        {
            SwitchOff(environment, $"its init.lua returns a table with '{taken}', a name the engine's accessors take "
                                   + "(require gives g and table beside what init.lua returns)", null);
            return;
        }
        var found = new List<(int Index, LuaValue Function)>();
        for (int i = 0; i < _stack.Handlers.Count; i++)
        {
            ModRuntimeHandler handler = _stack.Handlers[i];
            if (handler.ModId != mod.Id) continue;
            environment.Globals.TryGetValue(handler.Function, out LuaValue function);
            if (function.Type != LuaValueType.Function)
            {
                SwitchOff(environment, $"{handler.File}:{handler.Function} is not a function once its file has run", null);
                return;
            }
            found.Add((i, function));
        }
        foreach (var (index, function) in found) _handlers[index] = function;
    }

    private void SwitchOff(ModEnvironment environment, string message, string? traceback)
    {
        environment.Off = true;
        for (int i = 0; i < _stack.Handlers.Count; i++)
            if (_stack.Handlers[i].ModId == environment.Id) _handlers[i] = LuaValue.Nil;
        _failures.Add(new ModBuildFailure(environment.Id, message, traceback));
    }

    public void Invoke(HandlerId handler, HookContext context)
    {
        LuaValue function = _handlers[handler.Index];
        if (function.Type != LuaValueType.Function)
            throw new InvalidOperationException($"{_stack[handler]} is not active in this session");
        // A static body with its state passed in, so a call allocates no closure (plan §3.2's dispatch budget).
        Guarded(_limits.HandlerInstructions, startup: false, (Session: this, Function: function, View: new HookView(context, this)),
            static (state, token) =>
            {
                LuaState lua = state.Session._state;
                int frame = lua.Stack.Count;
                lua.Push(state.Function);
                lua.Push(LuaValue.FromUserData(state.View));
                try
                {
                    return Sync(lua.CallAsync(frame, frame, token));
                }
                finally
                {
                    lua.Stack.PopUntil(frame);
                }
            });
    }

    public ConsoleResult RunChunk(string? modId, string code)
    {
        ModEnvironment environment;
        if (modId == null) environment = _console ??= new ModEnvironment(ConsoleId, null, _access.Console, this);
        else if (!_mods.TryGetValue(modId, out environment!)) return new ConsoleResult(false, $"{modId} ships no Lua");
        else if (environment.Off) return new ConsoleResult(false, $"{modId} is off for this session");
        try
        {
            LuaValue[] results = Guarded(_limits.HandlerInstructions, startup: false, token =>
            {
                var closure = LuaSource.Load(_state, code, modId == null ? ConsoleId : $"{modId} (console)", environment.Globals);
                return Sync(_state.ExecuteAsync(closure, token));
            });
            return new ConsoleResult(true, string.Join("\t", results.Select(Show)));
        }
        catch (ModScriptException error)
        {
            return new ConsoleResult(false, error.Message + (error.LuaTraceback is { } trace ? "\n" + trace : ""));
        }
    }

    public const string ConsoleId = "console";

    /// <summary>Spec §11: each mod that is on, in load order, serialized on its own. A mod whose state does not
    /// serialize is a failure, and the others are still captured.</summary>
    public ModStateCapture CaptureState()
    {
        var blocks = new Dictionary<string, ModStateBlock>(StringComparer.Ordinal);
        var failures = new List<ModStateFailure>();
        foreach (ModRuntimeMod mod in _stack.Mods)
        {
            if (!_mods.TryGetValue(mod.Id, out ModEnvironment? environment) || environment.Off) continue;
            try
            {
                blocks[mod.Id] = new ModStateBlock(mod.Version, LuaStateCodec.Encode(environment.State, _limits));
            }
            catch (LuaStateException error)
            {
                failures.Add(new ModStateFailure(mod.Id, error.Message));
            }
        }
        return new ModStateCapture(blocks, failures);
    }

    public void Dispose()
    {
        _state.Dispose();
        _source.Dispose();
    }

    // ---- calls (plan §3.1) ---------------------------------------------------------------------------------------

    /// <summary>
    /// A call into Lua. The outermost call takes a fresh token and its budget; a nested one (a callback that calls Lua
    /// again) passes on the token it received and shares the budget. An error comes out as
    /// <see cref="ModScriptException"/>, an engine exception kept by a callback as itself.
    /// <paramref name="startup"/>: a mod's start-up code, whose calls share one budget across the mod's files.
    /// </summary>
    private T Guarded<T>(long budget, bool startup, Func<CancellationToken, T> call)
        => Guarded(budget, startup, call, static (body, token) => body(token));

    /// <summary>
    /// One call from the engine into Lua, with its token and budget (plan §3.1). A call made while another runs, a
    /// subscriber of an event raised from Lua (spec §7.4), is a call of its own: it has its own token and budget, its
    /// error is its own handler's, and the outer call's token and count resume after it.
    /// </summary>
    private T Guarded<TState, T>(long budget, bool startup, TState state, Func<TState, CancellationToken, T> call)
    {
        bool nested = _callDepth > 0;
        CancellationTokenSource outerSource = _source;
        long outerUsed = _used, outerBudget = _budget;
        bool outerStop = _budgetStop;
        ExceptionDispatchInfo? outerKept = _kept;
        if (nested) _source = new CancellationTokenSource();
        else if (!_source.TryReset())
        {
            _source.Dispose();
            _source = new CancellationTokenSource();
        }
        _used = 0;
        _budget = budget;
        _budgetStop = false;
        _kept = null;
        _callDepth++;
        try
        {
            return call(state, _source.Token);
        }
        catch (OperationCanceledException)
        {
            if (_kept is { } kept)
            {
                _kept = null;
                kept.Throw();
            }
            if (_budgetStop)
                throw new ModScriptException(ModScriptErrorKind.Budget,
                    $"{(startup ? "the mod's start-up code" : "the call")} ran past its budget of {budget:N0} instructions");
            throw;
        }
        catch (LuaRuntimeException error)
        {
            throw new ModScriptException(ModScriptErrorKind.Error, ErrorText(error), error.LuaTraceback?.ToString());
        }
        catch (LuaParseException error)
        {
            throw new ModScriptException(ModScriptErrorKind.Error, WithoutLibraryName(error.Message));
        }
        catch (Exception error) when (error.GetType().Name == "LuaCompileException")
        {
            throw new ModScriptException(ModScriptErrorKind.Error, WithoutLibraryName(error.Message));
        }
        finally
        {
            _callDepth--;
            if (nested)
            {
                _source.Dispose();
                _source = outerSource;
                _used = outerUsed;
                _budget = outerBudget;
                _budgetStop = outerStop;
                _kept = outerKept;
            }
        }
    }

    /// <summary>Every call completes in place (plan §3.1 point 5): the callbacks are synchronous.</summary>
    internal static T Sync<T>(ValueTask<T> task)
    {
        if (!task.IsCompleted) throw new InvalidOperationException("a Lua call did not complete synchronously");
        return task.Result;
    }

    /// <summary>
    /// An engine callback (plan §3.1 points 2 and 4). A refused access becomes a Lua error, which <c>pcall</c> may
    /// catch; any other engine exception is kept, the call's token is cancelled, and the outermost call throws it.
    /// </summary>
    internal LuaFunction Fn(string name, Func<LuaFunctionExecutionContext, int> body)
        => new(name, (context, _) =>
        {
            try
            {
                return new ValueTask<int>(body(context));
            }
            catch (ScriptAccessException error)
            {
                throw new LuaRuntimeException(context.State, error.Message, 1);
            }
            catch (Exception error) when (error is LuaRuntimeException or OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                _kept ??= ExceptionDispatchInfo.Capture(error);
                _source.Cancel();
                return new ValueTask<int>(context.Return());
            }
        });

    /// <summary>A callback that runs Lua again, with the token of the call it runs under (plan §3.1 point 1).</summary>
    internal LuaFunction FnT(string name, Func<LuaFunctionExecutionContext, CancellationToken, int> body)
        => new(name, (context, token) =>
        {
            try
            {
                return new ValueTask<int>(body(context, token));
            }
            catch (ScriptAccessException error)
            {
                throw new LuaRuntimeException(context.State, error.Message, 1);
            }
            catch (Exception error) when (error is LuaRuntimeException or OperationCanceledException)
            {
                throw;
            }
            catch (LuaParseException error)
            {
                throw new LuaRuntimeException(context.State, error.Message, 1);
            }
            catch (Exception error) when (error.GetType().Name == "LuaCompileException")
            {
                throw new LuaRuntimeException(context.State, error.Message, 1);
            }
            catch (Exception error)
            {
                _kept ??= ExceptionDispatchInfo.Capture(error);
                _source.Cancel();
                return new ValueTask<int>(context.Return());
            }
        });

    internal LuaState State => _state;
    internal Metatables Metatables { get; }
    internal SessionReason Reason => _reason;
    internal IScriptAccess Access => _access;
    internal ModRuntimeStack Stack => _stack;

    internal ModEnvironment? ModById(string id) => _mods.GetValueOrDefault(id);

    /// <summary>Lock the string metatable (spec §5.2): <c>getmetatable("")</c> gives a mod nothing it can change, and the
    /// string methods come from the state's own <c>string</c> table, which no mod reaches.</summary>
    private void LockStringMetatable()
    {
        LuaClosure closure = _state.Load(
            "local mt = getmetatable('') if mt ~= nil then mt.__metatable = false end".AsSpan(), "lock", _state.Environment);
        Sync(_state.ExecuteAsync(closure, CancellationToken.None));
    }

    /// <summary>A Lua error as reference Lua reports it: a string or a number with the position <c>error</c> adds,
    /// which the library keeps in its message (behind its own name), not in the error value; any other value as
    /// "(error object is a T value)".</summary>
    internal static string ErrorText(LuaRuntimeException error)
        => error.ErrorObject.Type is LuaValueType.String or LuaValueType.Number or LuaValueType.Nil
            ? WithoutLibraryName(error.Message)
            : $"(error object is a {error.ErrorObject.TypeToString()} value)";

    private const string LibraryName = "Lua-CSharp: ";

    internal static string WithoutLibraryName(string message)
        => message.StartsWith(LibraryName, StringComparison.Ordinal) ? message[LibraryName.Length..] : message;

    internal static string Show(LuaValue value) => value.Type switch
    {
        LuaValueType.Nil => "nil",
        LuaValueType.Number => value.Read<double>().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
