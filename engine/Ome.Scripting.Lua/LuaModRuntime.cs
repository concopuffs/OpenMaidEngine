using Lua;
using Ome.Engine.Modding;

namespace Ome.Scripting.Lua;

/// <summary>
/// The Lua runtime over Lua-CSharp (docs/superpowers/plans/2026-11-14-lua-scripting.md §3): the link checks a file
/// with <see cref="Check"/>, which compiles and scans it and runs nothing, and the scripting native builds each
/// session with <see cref="BuildSession"/>.
/// </summary>
public sealed class LuaModRuntime : IModRuntime
{
    /// <summary>The library and this build, known without creating a runtime.</summary>
    public static string RuntimeIdentity { get; } =
        $"LuaCSharp {typeof(LuaState).Assembly.GetName().Version}; "
        + $"Ome.Scripting.Lua {typeof(LuaModRuntime).Assembly.ManifestModule.ModuleVersionId}";

    /// <summary>A lazily created runtime, as the composition hands one to the link and the scripting native.</summary>
    public static LazyModRuntime Lazy() => new(RuntimeIdentity, () => new LuaModRuntime());

    private readonly object _checkLock = new();
    private LuaState? _checkState;

    public string Identity => RuntimeIdentity;

    public ModSourceCheck Check(ModSource source)
    {
        // Syntax errors come from the compiler a session loads with (compiling runs nothing); its errors carry a line
        // and no column, as reference Lua's do. Lua-CSharp's syntax tree is not used: that parser refuses valid Lua
        // (plan §4.2), so the handlers and the writes come from LuaSourceScan, over text the compiler accepted.
        try
        {
            lock (_checkLock)
            {
                _checkState ??= LuaState.Create();
                LuaSource.Load(_checkState, source.Text, source.ChunkName, new LuaTable());
            }
        }
        catch (LuaParseException error)
        {
            string message = Bare(error.Message, source.ChunkName, out int line);
            return new ModSourceCheck([new ModSourceError(line > 0 ? line : error.Position.Line, 0, message)],
                                      new HashSet<string>(), []);
        }
        catch (Exception error) when (error.GetType().Name == "LuaCompileException")
        {
            string message = Bare(error.Message, source.ChunkName, out int line);
            return new ModSourceCheck([new ModSourceError(line, 0, message)], new HashSet<string>(), []);
        }
        var (functions, writes) = LuaSourceScan.Scan(source.Text);
        return new ModSourceCheck([], functions, writes);
    }

    /// <summary>The library's message without what the report gives itself: the library's name and the
    /// <c>[string "CHUNK"]:LINE:</c> (the compiler's) or <c>CHUNK:LINE:</c> it puts in front, whose line it hands back
    /// (0 when there is none).</summary>
    private static string Bare(string message, string chunkName, out int line)
    {
        line = 0;
        message = LuaModSession.WithoutLibraryName(message);
        foreach (string prefix in new[] { $"[string \"{chunkName}\"]:", chunkName + ":" })
        {
            if (!message.StartsWith(prefix, StringComparison.Ordinal)) continue;
            int digits = prefix.Length, end = digits;
            while (end < message.Length && char.IsAsciiDigit(message[end])) end++;
            if (end == digits || end + 1 >= message.Length || message[end] != ':' || message[end + 1] != ' ') return message;
            line = int.Parse(message.AsSpan(digits, end - digits), System.Globalization.CultureInfo.InvariantCulture);
            return message[(end + 2)..];
        }
        return message;
    }

    public IModSession BuildSession(ModRuntimeStack stack, IScriptAccess access, ModState state, SessionReason reason,
                                    ModLimits limits)
        => LuaModSession.Build(stack, access, state, reason, limits);

    public void Dispose()
    {
        lock (_checkLock)
        {
            _checkState?.Dispose();
            _checkState = null;
        }
    }
}
