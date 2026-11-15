namespace Ome.Engine.Modding;

// The runtime seam of docs/superpowers/specs/2026-11-10-lua-scripting-design.md §14.2. Ome.Engine defines it, the
// scripting native and the link call it, and Ome.Scripting.Lua implements it: the only project that names the Lua
// library. Nothing here runs on any thread but the engine thread, except Check, which runs no mod code.

/// <summary>A Lua source of a package: its mod, its path under <c>lua/</c> (<c>util/names.lua</c>), and its text.</summary>
public sealed record ModSource(string ModId, string Path, string Text)
{
    /// <summary>How reports and tracebacks name it: <c>mod.id/lua/util/names.lua</c>.</summary>
    public string ChunkName => $"{ModId}/lua/{Path}";
}

/// <summary>A syntax error a check found, with its position (1-based; 0 when the parser gave none).</summary>
public sealed record ModSourceError(int Line, int Column, string Message)
{
    public override string ToString()
        => Line <= 0 ? Message : Column > 0 ? $"line {Line}, column {Column}: {Message}" : $"line {Line}: {Message}";
}

/// <summary>What a check found without running the file (spec §5.5, §6.2, §9.3): its syntax errors, the functions it
/// declares at its top level as globals of the mod (the handlers it can serve), and the base mod's names it writes out
/// as <c>x.g.NAME = …</c> or <c>x.table("NAME")[…] = …</c>.</summary>
public sealed record ModSourceCheck(IReadOnlyList<ModSourceError> Errors, IReadOnlySet<string> TopLevelFunctions,
                                    IReadOnlyList<string> WrittenNames)
{
    public bool Parsed => Errors.Count == 0;
}

/// <summary>Why a session was built (spec §5.7); <c>maid.session.reason</c> and <c>ome.session.start</c>'s
/// <c>reason</c> give its name.</summary>
public enum SessionReason { Boot, Title, Load, Reload }

public static class SessionReasons
{
    public static string Name(this SessionReason reason) => reason switch
    {
        SessionReason.Boot => "boot",
        SessionReason.Title => "title",
        SessionReason.Load => "load",
        _ => "reload",
    };
}

/// <summary>The bounds of a session (spec §5.5; plan §3.2–§3.3): the instructions a handler call or a console chunk
/// may run, the instructions each mod's start-up code (its <c>init.lua</c> and the files its hooks name) may run, and
/// how deep handlers may nest.</summary>
public sealed record ModLimits
{
    public long HandlerInstructions { get; init; } = 1_000_000;
    public long StartupInstructions { get; init; } = 10_000_000;
    public int NestingCap { get; init; } = 32;
    public static readonly ModLimits Default = new();
}

/// <summary>A mod the build switched off for the session (spec §5.5): its <c>init.lua</c> or a file its hooks name
/// raised an error, a handler was not a function once its file had run, or a mod it requires is off.</summary>
public sealed record ModBuildFailure(string ModId, string Message, string? Traceback);

/// <summary>What a console chunk gave (spec §13): its results as text, or its error.</summary>
public sealed record ConsoleResult(bool Succeeded, string Text);

/// <summary>Each mod's <c>maid.state</c>, as a session is built from it and captures it. Milestone 2 gives it content
/// (spec §11); until then every session starts from <see cref="Empty"/>.</summary>
public sealed class ModState
{
    public static readonly ModState Empty = new();
}

/// <summary>How a handler call or a chunk failed, so the native can apply the error policy (spec §5.5).</summary>
public enum ModScriptErrorKind
{
    /// <summary>A Lua error: <c>error()</c>, a bad argument, a refused access, a stack overflow.</summary>
    Error,
    /// <summary>The call ran out of its instruction budget.</summary>
    Budget,
    /// <summary>Handlers nested past the cap.</summary>
    Depth,
}

/// <summary>A handler call or a chunk that ended with an error. An engine unwind (a return to title, a stop, a halt)
/// is never one: the runtime carries it out as the exception it was (plan §3.1).</summary>
public sealed class ModScriptException(ModScriptErrorKind kind, string message, string? traceback = null)
    : Exception(message)
{
    public ModScriptErrorKind Kind { get; } = kind;
    public string? LuaTraceback { get; } = traceback;
}

/// <summary>The Lua runtime (spec §14.2). One serves a whole run: the link checks files through it, and the scripting
/// native builds every session with it.</summary>
public interface IModRuntime : IDisposable
{
    /// <summary>The library and the runtime build, for the link cache key: a check can change with either.</summary>
    string Identity { get; }

    /// <summary>Compile and scan only, and run nothing: syntax errors, the top-level functions, the names written.</summary>
    ModSourceCheck Check(ModSource source);

    /// <summary>A new session: one Lua state with every mod's environment, each mod's <c>init.lua</c> run in load
    /// order and the files its hooks name loaded (spec §5.7 steps 2–3). A mod that fails is off in it
    /// (<see cref="IModSession.Failures"/>); the build itself does not throw for a mod's error.</summary>
    IModSession BuildSession(ModRuntimeStack stack, IScriptAccess access, ModState state, SessionReason reason,
                             ModLimits limits);
}

/// <summary>A built session (spec §5.7). Engine thread only.</summary>
public interface IModSession : IDisposable
{
    /// <summary>The mods the build switched off, in load order.</summary>
    IReadOnlyList<ModBuildFailure> Failures { get; }

    /// <summary>Whether the handler's mod is on and its function was found.</summary>
    bool IsActive(HandlerId handler);

    /// <summary>Call a handler with its context. Throws <see cref="ModScriptException"/> for its error; an engine
    /// exception raised inside it comes out unchanged.</summary>
    void Invoke(HandlerId handler, HookContext context);

    /// <summary>Run a console chunk (spec §13) in the console's environment, or in the environment of the mod
    /// <paramref name="modId"/>. Its error is the result's text; an engine exception comes out unchanged.</summary>
    ConsoleResult RunChunk(string? modId, string code);

    /// <summary>Each mod's state for a save (milestone 2).</summary>
    ModState CaptureState();
}

/// <summary>
/// The run's runtime, created at its first use (spec §11, gate 1): a run whose mods ship no Lua, and that never uses
/// the console, creates none. The composition builds one over the runtime's factory and hands it to the link and to
/// the scripting native.
/// </summary>
public sealed class LazyModRuntime(string identity, Func<IModRuntime> create) : IDisposable
{
    private IModRuntime? _runtime;

    /// <summary>The runtime's identity, known without creating it.</summary>
    public string Identity { get; } = identity;

    public bool Created => _runtime != null;

    public IModRuntime Get() => _runtime ??= create();

    public void Dispose()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
