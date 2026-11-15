using Lua;
using Lua.Runtime;
using Ome.Engine.Modding;

namespace Ome.Scripting.Lua;

/// <summary>
/// A mod's global environment (spec §5.2–§5.4): the sandbox's basics, its own copies of the library tables, its own
/// <c>maid</c>, a <c>print</c> that writes to the log, and a <c>require</c> that resolves only its own <c>lua/</c>
/// files and the mods it requires, each once per session. The console has one too, with no files.
/// </summary>
internal sealed class ModEnvironment
{
    public const string InitFile = "init.lua", InitModule = "init";

    private readonly LuaModSession _session;
    private readonly Dictionary<string, LuaValue> _modules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
    private readonly ReadOnlyViews _views;
    // What require gives for a base mod: its accessors for this mod beside its exports, one view per session.
    private readonly Dictionary<string, LuaValue> _baseViews = new(StringComparer.Ordinal);

    public ModEnvironment(string id, ModRuntimeMod? mod, IModAccess access, LuaModSession session)
    {
        Id = id;
        Mod = mod;
        Access = access;
        _session = session;
        _views = new ReadOnlyViews(session.Metatables);
        Globals = BuildGlobals();
    }

    public string Id { get; }
    public ModRuntimeMod? Mod { get; }
    public IModAccess Access { get; }
    public LuaTable Globals { get; }
    /// <summary>The mod's own <c>maid</c>, whatever its globals now hold: a save reads <c>maid.state</c> from it.</summary>
    public LuaTable MaidTable { get; private set; } = null!;
    /// <summary><c>maid.state</c> as the mod left it, read raw.</summary>
    public LuaValue State => MaidTable["state"];
    /// <summary>What the mod's <c>init.lua</c> returned: what <c>require</c> gives a mod that requires it.</summary>
    public LuaValue Exports { get; set; } = LuaValue.Nil;
    public bool Off { get; set; }

    /// <summary>A file's module name: <c>util/names.lua</c> is <c>util.names</c>.</summary>
    public static string ModuleOf(string file)
        => (file.EndsWith(".lua", StringComparison.Ordinal) ? file[..^4] : file).Replace('/', '.');

    private static string FileOf(string module) => module.Replace('.', '/') + ".lua";

    private LuaTable BuildGlobals()
    {
        LuaState state = _session.State;
        var globals = new LuaTable();
        foreach (string name in LuaModSession.Basics)
            if (state.Environment.TryGetValue(name, out LuaValue value)) globals[name] = value;
        foreach (string name in LuaModSession.LibraryTables)
        {
            if (!state.Environment.TryGetValue(name, out LuaValue value) || value.Type != LuaValueType.Table) continue;
            var copy = new LuaTable();
            foreach (var pair in value.Read<LuaTable>()) copy[pair.Key] = pair.Value;
            if (name == "string") copy["dump"] = LuaValue.Nil;
            if (name == "math")
            {
                copy["randomseed"] = LuaValue.Nil;   // spec §5.6: the seed is the engine's
                copy["random"] = Random();
            }
            globals[name] = copy;
        }
        globals["_G"] = globals;
        globals["require"] = _session.FnT("require", (context, token) =>
            context.Return(Require(context.GetArgument<string>(0), token)));
        globals["print"] = _session.Fn("print", context =>
        {
            Access.Log(ModLogLevel.Info, Join(context));
            return context.Return();
        });
        globals["maid"] = Maid();
        return globals;
    }

    private static string Join(LuaFunctionExecutionContext context)
    {
        var parts = new string[context.ArgumentCount];
        for (int i = 0; i < parts.Length; i++) parts[i] = LuaModSession.Show(context.GetArgument(i));
        return string.Join("\t", parts);
    }

    /// <summary>The mod's <c>maid</c> (spec §8.1, §8.4): <c>vm</c> only with the capability, <c>log</c>, <c>mod</c> and
    /// <c>session</c>; <c>names</c> only in a base mod and the console.</summary>
    private LuaTable Maid()
    {
        var maid = new LuaTable();
        if (Access.Vm is { } vm) maid["vm"] = LuaValue.FromUserData(new VmView(vm, _session));
        if (Access.OwnLayout is { } own && Access.Names(own) is { } names)
            maid["names"] = LuaValue.FromUserData(new NamesView(names, LuaValue.Nil, _views, _session));
        var log = new LuaTable();
        foreach (var (name, level) in new[] { ("info", ModLogLevel.Info), ("warn", ModLogLevel.Warn), ("error", ModLogLevel.Error) })
            log[name] = _session.Fn(name, context =>
            {
                Access.Log(level, Join(context));
                return context.Return();
            });
        maid["log"] = log;
        var mod = new LuaTable { ["id"] = Id };
        if (Mod != null) mod["version"] = Mod.Version;
        maid["mod"] = mod;
        maid["session"] = LuaValue.FromUserData(new SessionView(_session));
        maid["state"] = new LuaTable();
        MaidTable = maid;
        return maid;
    }

    /// <summary>Spec §5.7 step 2: the mod's saved state, and in <c>maid.mod.state_version</c> the version of the mod
    /// that wrote it (nil when the mod has none saved).</summary>
    public void SetState(LuaTable state, string? version)
    {
        MaidTable["state"] = state;
        if (MaidTable["mod"].TryRead(out LuaTable mod)) mod["state_version"] = version is null ? LuaValue.Nil : version;
    }

    /// <summary><c>math.random</c> over the engine's draws (spec §5.6): as many 15-bit draws as cover the range's size,
    /// joined with the first as the highest bits, then the modulo as op 0x60 takes it.</summary>
    private LuaFunction Random() => _session.Fn("random", context =>
    {
        if (context.ArgumentCount == 0)
        {
            ulong bits = 0;
            for (int k = 0; k < 4; k++) bits = (bits << 15) | (uint)Access.DrawRandom();
            return context.Return((double)(bits >> 7) / (1UL << 53));
        }
        double low = 1, high;
        if (context.ArgumentCount == 1) high = Whole(context, 0);
        else
        {
            low = Whole(context, 0);
            high = Whole(context, 1);
        }
        if (low > high) throw new LuaRuntimeException(context.State, "bad argument to 'random' (interval is empty)", 1);
        double size = high - low + 1;
        if (size > 1L << 45)
            throw new LuaRuntimeException(context.State, "bad argument to 'random' (the range may hold at most 2^45 values)", 1);
        int draws = size <= 1 << 15 ? 1 : size <= 1L << 30 ? 2 : 3;
        ulong joined = 0;
        for (int k = 0; k < draws; k++) joined = (joined << 15) | (uint)Access.DrawRandom();
        return context.Return(low + (double)(joined % (ulong)size));
    });

    private static double Whole(LuaFunctionExecutionContext context, int index)
    {
        double value = context.GetArgument<double>(index);
        if (!double.IsFinite(value) || value != Math.Floor(value) || Math.Abs(value) > 1L << 53)
            throw new LuaRuntimeException(context.State, $"bad argument #{index + 1} to 'random' (number has no integer representation)", 1);
        return value;
    }

    /// <summary>
    /// <c>require</c> (spec §5.4): an id under the mod's <c>[[requires]]</c> gives a read-only view of what that mod's
    /// <c>init.lua</c> returned, and for a base mod its accessors beside it; a module name gives the mod's own file, run
    /// once. The console's environment resolves the base mod its accessors read. Nothing else resolves.
    /// </summary>
    private LuaValue Require(string name, CancellationToken token)
    {
        if (Mod != null && Mod.Requires.Contains(name, StringComparer.Ordinal))
        {
            ModEnvironment required = _session.ModById(name)
                                      ?? throw new LuaRuntimeException(_session.State,
                                          $"{name} ships no Lua, so require(\"{name}\") has nothing to give", 1);
            if (required.Off)
                throw new LuaRuntimeException(_session.State, $"{name} is off for this session", 1);
            return required.Mod?.Layout != null && Access.Names(name) is { } names
                ? BaseView(name, names, required.Exports)
                : _views.Wrap(required.Exports);
        }
        if (Mod == null && Access.Names(name) is { } console)
            return BaseView(name, console, _session.ModById(name) is { Off: false } mounted ? mounted.Exports : LuaValue.Nil);
        if (Mod != null && Mod.Files.Any(file => file.Path == FileOf(name)))
        {
            LuaValue result = LoadModule(name, token);
            return result.Type == LuaValueType.Nil ? new LuaValue(true) : result;
        }
        throw new LuaRuntimeException(_session.State,
            $"module '{name}' not found: require gives the mod's own lua/ files and the ids it lists under [[requires]]", 1);
    }

    private LuaValue BaseView(string id, INamedAccess names, LuaValue exports)
    {
        if (!_baseViews.TryGetValue(id, out LuaValue view))
            _baseViews[id] = view = LuaValue.FromUserData(new NamesView(names, exports, _views, _session));
        return view;
    }

    /// <summary>Run a file of the mod once per session and keep its first result.</summary>
    public LuaValue LoadModule(string module, CancellationToken token)
    {
        if (_modules.TryGetValue(module, out LuaValue cached)) return cached;
        if (!_loading.Add(module))
            throw new LuaRuntimeException(_session.State, $"require loop: {module} is still loading", 1);
        try
        {
            string path = FileOf(module);
            ModSource source = Mod?.Files.FirstOrDefault(file => file.Path == path)
                               ?? throw new LuaRuntimeException(_session.State, $"{Id} has no lua/{path}", 1);
            LuaClosure closure = LuaSource.Load(_session.State, source.Text, source.ChunkName, Globals);
            LuaValue[] results = LuaModSession.Sync(_session.State.ExecuteAsync(closure, token));
            LuaValue result = results.Length > 0 ? results[0] : LuaValue.Nil;
            _modules[module] = result;
            return result;
        }
        finally
        {
            _loading.Remove(module);
        }
    }
}
