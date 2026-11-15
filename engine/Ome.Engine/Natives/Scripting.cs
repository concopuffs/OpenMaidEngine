using System.Runtime.CompilerServices;
using Ome.Engine.Model;
using Ome.Engine.Modding;

namespace Ome.Engine.Natives;

/// <summary>What a handler's error does (spec §5.5): the game goes on, or the run halts (<c>--lua-errors halt</c>).</summary>
public enum LuaErrorPolicy { Continue, Halt }

/// <summary>What the composition gives the scripting native: the run's runtime, the link's stack and hook plan, where
/// the linked instructions came from, the bounds, the error policy, the directory <c>lua.log</c> goes in (none keeps the
/// log in memory only), and how to read the mod ids a save's stamp names.</summary>
public sealed record ScriptingSetup
{
    public LazyModRuntime? Runtime { get; init; }
    public ModRuntimeStack Stack { get; init; } = ModRuntimeStack.Empty;
    public HookPlan Plan { get; init; } = HookPlan.Empty;
    public IScriptSites Sites { get; init; } = UnchangedScriptSites.Instance;
    public ModLimits Limits { get; init; } = ModLimits.Default;
    public LuaErrorPolicy Errors { get; init; } = LuaErrorPolicy.Continue;
    public string? DiagnosticsDirectory { get; init; }

    /// <summary><c>--lua-trace</c> (spec §12): a line per handler call in <c>diagnostics/lua-trace.jsonl</c>.</summary>
    public bool Trace { get; init; }

    /// <summary>The mod ids the stack stamp beside a loaded save names (mod loader spec §8, <c>SAVEnn.mods.toml</c>),
    /// from the texts the store read beside it; null when it has none. With it, a load whose state file is missing
    /// while the stamp names a mod that ships Lua is reported (spec §11). The composition reads the stamp; without it
    /// that report is not made.</summary>
    public Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>?>? StampedMods { get; init; }

    /// <summary>The layout of the profile's base mod, which the console's accessors read when the stack does not
    /// mount that base mod (spec §13). Asked at the console's first use; null gives the console no accessors.</summary>
    public Func<BaseModLayout?>? ConsoleLayout { get; init; }
}

/// <summary>
/// The scripting native of docs/superpowers/specs/2026-11-10-lua-scripting-design.md §14.1. It owns the session (scene
/// state: op 0x9's reset drops it), the order handlers run in, budgets and the error policy, <c>diagnostics/lua.log</c>,
/// the notices the front end reads, and the console's requests. It exists in every run of the port; it creates the
/// Lua runtime only when a mod ships Lua or the console asks for one (spec §11). It starts no script (engine layering
/// rule 9): the interpreter calls it at the places of spec §5.7 and §7.1.
/// </summary>
/// <remarks>Threads: the engine thread only, except <see cref="NoticeCount"/> and <see cref="LatestNotice"/>, which the
/// front end reads each frame, and a console request's completion, which the driver polls.</remarks>
public sealed partial class Scripting : IDisposable
{
    /// <summary>Failures in a row after which a handler is off for the session (spec §5.5).</summary>
    public const int FailuresBeforeSwitchOff = 3;
    public const string SessionStartEvent = "ome.session.start";

    private readonly ScriptMemory _memory;
    private readonly ScriptingSetup _setup;
    private readonly ScriptAccess _access;
    private readonly Lazy<BaseModLayout?> _consoleLayout;
    private IInterpreterAccess? _interpreter;
    private readonly ConditionalWeakTable<Script, TableEntry> _tables = new();

    private sealed class TableEntry(ScriptHooks? table)
    {
        public ScriptHooks? Table { get; } = table;
    }

    // Scene-lifetime state: op 0x9's reset rebuilds it (engine layering spec §5.1, rule 5), and so does each session's
    // build, which first takes the load's state (Scripting.State.cs).
    private sealed class Scene
    {
        public IModSession? Session;
        public bool ConsoleOnly;   // built for the console alone: no mod ships Lua (spec §13)
        public SessionReason Reason;
        public readonly Dictionary<int, int> FailuresInARow = new();
        public readonly HashSet<int> SwitchedOff = new();
        // Each mod's last good state block (spec §11): the loaded one until a save captures the mod's own. A block for
        // a mod not in the session, or off in it, is carried into every save unchanged.
        public readonly Dictionary<string, ModStateBlock> Blocks = new(StringComparer.Ordinal);
        public bool Restoring;        // maid.session.restoring: a full load's build until its last frame is restored
        public bool ResumedPending;   // ome.session.resumed is due at the next instruction boundary
        public int Slot = -1;         // the slot the session's load read
        // A load's state, from the save native's read until the session the load rebuilds takes it (spec §11).
        public LoadedState? Loaded;
        // A data-only load ended the session; the next instruction boundary builds the next one (spec §5.7).
        public bool BuildPending;
        // A mod's state failed to serialize under --lua-errors halt; the save's own instruction ends the run.
        public string? HaltPending;
    }

    private Scene _scene = new();
    // How many handler calls, builds and chunks are running: Lua on the stack (spec §13).
    private int _depth;
    // Whether the running call may read the frame's locals: a point hook or a console chunk, not an engine event.
    private bool _frameAccess;

    public Scripting(ScriptMemory memory, ScriptingSetup? setup = null)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _setup = setup ?? new ScriptingSetup();
        _access = new ScriptAccess(this);
        _consoleLayout = new Lazy<BaseModLayout?>(() => _setup.ConsoleLayout?.Invoke());
    }

    /// <summary>A scripting native with no mods and no runtime: a VM given it has no hooks.</summary>
    public static Scripting Without(ScriptMemory memory) => new(memory);

    public ScriptMemory Memory => _memory;
    public ScriptingSetup Setup => _setup;

    /// <summary>Whether a session is running.</summary>
    public bool HasSession => _scene.Session != null;

    /// <summary>The reason the running session was built.</summary>
    public SessionReason? Reason => _scene.Session == null ? null : _scene.Reason;

    /// <summary>Whether Lua is on the stack: a handler, a build or a console chunk is running.</summary>
    public bool LuaRunning => _depth > 0;

    /// <summary>The interpreter's side of <c>maid.vm</c>; the VM attaches itself when it is built.</summary>
    public void AttachInterpreter(IInterpreterAccess interpreter)
    {
        if (_interpreter != null && !ReferenceEquals(_interpreter, interpreter))
            throw new InvalidOperationException("A scripting native serves one interpreter.");
        _interpreter = interpreter;
    }

    /// <summary>The table a frame of <paramref name="script"/> takes when it is entered (spec §7.5), or null.
    /// <c>ome.save.writing</c>'s subscribers take their places in the list of every numbered save instruction, as a
    /// <c>before</c> hook on it would (spec §7.4, §7.7).</summary>
    public ScriptHooks? HooksFor(Script script)
    {
        IReadOnlyList<PlannedHandler> saveWriting = _setup.Plan.HandlersOf(SaveWritingEvent);
        if (_setup.Plan.Sites.Count == 0 && saveWriting.Count == 0) return null;
        if (_tables.TryGetValue(script, out TableEntry? entry)) return entry.Table;
        if (!_setup.Plan.Sites.TryGetValue(Localization.TextCatalog.NormalizeScriptName(script.Name), out var sites))
            sites = [];
        IInterpreterAccess? interpreter = _interpreter;
        ScriptHooks? table = ScriptHooks.For(script, sites,
            site => WriteLog(ModLogLevel.Warn, null,
                $"{script.Name}@0x{site.Offset:x} is no instruction of the loaded script; its hooks do not fire"),
            interpreter == null ? null : instruction => interpreter.IsNumberedSave(instruction.Opcode),
            saveWriting.Select(handler => handler with { EngineEvent = SaveWritingEvent }).ToList(),
            offset => _setup.Sites.Site(script.Name, offset));
        _tables.AddOrUpdate(script, new TableEntry(table));
        return table;
    }

    /// <summary>
    /// Build the session (spec §5.7): the old one ends, the runtime builds the new one, and <c>ome.session.start</c>
    /// fires with <paramref name="reason"/>. The interpreter calls it between frames: at the start of the run, after a
    /// return to title, and after a full load. Returns a halt reason under <c>--lua-errors halt</c>, or null.
    /// </summary>
    public string? StartSession(SessionReason reason)
    {
        LoadedState? loaded = reason == SessionReason.Load ? _scene.Loaded : null;
        EndSession();
        // Spec §5.7 step 2: a load's state, kept even when no mod ships Lua, so a removed mod's block reaches the next
        // save (spec §11).
        ModState state = TakeState(loaded);
        if (!_setup.Stack.HasLua) return null;   // gate 1: no mod ships Lua, so no runtime
        IModRuntime runtime = Runtime();
        IModSession session;
        _depth++;
        try
        {
            session = runtime.BuildSession(_setup.Stack, _access, state, reason, _setup.Limits);
        }
        finally
        {
            _depth--;
        }
        _scene.Session = session;
        _scene.Reason = reason;
        foreach (ModBuildFailure failure in session.Failures)
        {
            WriteLog(ModLogLevel.Error, failure.ModId, $"off for this session: {failure.Message}"
                                                       + (failure.Traceback is { } trace ? "\n" + trace : ""));
            Notify($"Lua: {failure.ModId} is off for this session: {FirstLine(failure.Message)}");
            if (_setup.Errors == LuaErrorPolicy.Halt) return $"lua-build-error: {failure.ModId}: {FirstLine(failure.Message)}";
        }
        return RaiseEngineEvent(SessionStartEvent, new KeyValuePair<string, CellValue>("reason", CellValue.String(reason.Name())));
    }

    /// <summary>Op 0x9's reset: the session ends with the scene, and with it the mods' state and a load still to be
    /// taken (spec §5.7). The interpreter builds the next one before the root script is entered again.</summary>
    public void ResetScene() => EndSession();

    /// <summary>End the session at shutdown, on the engine thread or once it has stopped. The composition disposes the
    /// runtime it created.</summary>
    public void Dispose()
    {
        EndSession();
        CloseTrace();
    }

    private void EndSession()
    {
        if (_depth > 0) throw new InvalidOperationException("A session cannot end while its Lua runs.");
        _scene.Session?.Dispose();
        _scene = new Scene();
    }

    /// <summary>
    /// The <c>before</c> hooks of a hooked instruction (spec §7.1): every handler due there, in order, after the
    /// boundary's services and before the opcode handler. Returns a halt reason under <c>--lua-errors halt</c>.
    /// </summary>
    public string? RunBefore(HookSite site, Instruction instruction)
    {
        IModSession? session = _scene.Session;
        if (session == null || _interpreter == null) return null;
        // Indexed, not foreach: an enumerator over the interface would be allocated at every hooked instruction.
        IReadOnlyList<PlannedHandler> handlers = site.Planned.Handlers;
        for (int i = 0; i < handlers.Count; i++)
        {
            PlannedHandler handler = handlers[i];
            if (!IsOn(session, handler.Handler)) continue;
            // An engine event's subscriber in the instruction's list (ome.save.writing) gets the event's context:
            // its fields, and no frame.
            // An event's subscriber at its point (spec §9.2) gets the event's fields over the frame's cells.
            HookContext context = handler.EngineEvent is { } name
                ? HookContext.ForEngineEvent(name,
                    new KeyValuePair<string, CellValue>("slot", _interpreter.ReadOperand(instruction.Args[1])))
                : handler.GameEvent is { } gameEvent
                    ? GameEventContext(gameEvent, site, instruction, _interpreter)
                    : HookContext.ForPoint(site, instruction, _interpreter, _memory.Encoding.CodePage);
            if (Call(session, handler.Handler, context, frameAccess: handler.EngineEvent == null) is { } halt)
                return halt;
        }
        return null;
    }

    /// <summary>An engine event's handlers (spec §7.7), in order, with no frame.</summary>
    private string? RaiseEngineEvent(string name, params KeyValuePair<string, CellValue>[] fields)
    {
        IModSession? session = _scene.Session;
        if (session == null) return null;
        foreach (PlannedHandler handler in _setup.Plan.HandlersOf(name))
        {
            if (!IsOn(session, handler.Handler)) continue;
            if (Call(session, handler.Handler, HookContext.ForEngineEvent(name, fields), frameAccess: false) is { } halt)
                return halt;
        }
        return null;
    }

    private bool IsOn(IModSession session, HandlerId handler)
        => !_scene.SwitchedOff.Contains(handler.Index) && session.IsActive(handler);

    /// <summary>Where a handler ran, for its error: the instruction of a point, the name of an event. Formed only
    /// when a call fails, so a call that succeeds formats nothing.</summary>
    private static string Where(HookContext context) => context switch
    {
        { Kind: HookKind.Point } => $"{context.Script}@0x{context.Offset:x}",
        { Kind: HookKind.GameEvent, Script: { } script } => $"{context.Name} at {script}@0x{context.Offset:x}",
        _ => context.Name ?? "",
    };

    private string? Call(IModSession session, HandlerId handler, HookContext context, bool frameAccess)
    {
        if (_depth >= _setup.Limits.NestingCap)
            return Failed(handler, new ModScriptException(ModScriptErrorKind.Depth,
                $"handlers nest {_depth} deep, the cap is {_setup.Limits.NestingCap}"), Where(context));
        bool outerFrameAccess = _frameAccess;
        _depth++;
        _frameAccess = frameAccess;
        TracedCall? traced = TraceBegin(handler, context);
        ModScriptException? failure = null;
        string? halt;
        try
        {
            session.Invoke(handler, context);
            _scene.FailuresInARow.Remove(handler.Index);
            halt = null;
        }
        catch (ModScriptException error)
        {
            failure = error;
            halt = Failed(handler, error, Where(context));
        }
        finally
        {
            if (traced != null) TraceEnd(traced, failure);
            context.Live = false;
            _frameAccess = outerFrameAccess;
            _depth--;
        }
        // A subscriber of an event this call raised halted (--lua-errors halt): the outermost call returns its reason.
        if (_depth == 0 && _nestedHalt is { } nested)
        {
            _nestedHalt = null;
            return nested;
        }
        return halt;
    }

    /// <summary>A handler's error (spec §5.5): the log gets the mod, the handler, where it ran and the traceback; the
    /// front end gets a notice; the third failure in a row switches the handler off. Under <c>--lua-errors halt</c> the
    /// instruction ends with a halt instead.</summary>
    private string? Failed(HandlerId handler, ModScriptException error, string where)
    {
        ModRuntimeHandler name = _setup.Stack[handler];
        string what = error.Kind switch
        {
            ModScriptErrorKind.Budget => "ran out of its instruction budget",
            ModScriptErrorKind.Depth => "nested too deep",
            _ => "failed",
        };
        WriteLog(ModLogLevel.Error, name.ModId, $"{name.File}:{name.Function} {what} at {where}: {error.Message}"
                                                + (error.LuaTraceback is { } trace ? "\n" + trace : ""));
        if (_setup.Errors == LuaErrorPolicy.Halt)
            return $"lua-error: {name.ModId} {name.File}:{name.Function} at {where}: {FirstLine(error.Message)}";
        int failures = _scene.FailuresInARow.GetValueOrDefault(handler.Index) + 1;
        _scene.FailuresInARow[handler.Index] = failures;
        if (failures >= FailuresBeforeSwitchOff)
        {
            _scene.SwitchedOff.Add(handler.Index);
            Notify($"Lua: {name.ModId} {name.Function} failed {failures} times in a row and is off for this session");
        }
        else Notify($"Lua: {name.ModId} {name.Function} {what}: {FirstLine(error.Message)}");
        return null;
    }

    private IModRuntime Runtime()
        => (_setup.Runtime ?? throw new InvalidOperationException(
            "The stack ships Lua, but this run has no Lua runtime.")).Get();

    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n');
        return end < 0 ? text : text[..end];
    }
}
