using Ome.Engine.Model;
using Ome.Engine.Modding;

/// <summary>A stack and its hook plan, as the link would leave them, for the Lua tests (<see cref="LuaScriptingTests"/>,
/// <see cref="LuaStateTests"/>). Files are <c>-- file: name.lua</c> sections of one text.</summary>
internal sealed class LuaStackBuilder
{
    private readonly List<ModRuntimeMod> _mods = new();
    private readonly List<ModRuntimeHandler> _handlers = new();
    private readonly Dictionary<string, List<PlannedSite>> _sites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PlannedHandler>> _engine = new(StringComparer.Ordinal);
    private readonly List<GameEvent> _events = new();
    private readonly Dictionary<string, List<PlannedHandler>> _luaEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (List<PlannedHandler> Enter, List<PlannedHandler> Exit)> _scripts = new(StringComparer.Ordinal);
    private readonly List<PlannedWatch> _watches = new();

    public LuaStackBuilder Mod(string id, string files, bool vm = true, bool library = false, params string[] requires)
        => Mod(id, Files(files), vm, library, requires);

    /// <summary>A mod's sources from <c>-- file: name.lua</c> sections of one text, as a reload hands them over.</summary>
    public static IReadOnlyList<ModSource> Sources(string id, string files)
        => Files(files).Select(file => new ModSource(id, file.Item1, file.Item2)).ToList();

    /// <summary>Files as <c>-- file: name.lua</c> sections of one text.</summary>
    private static IReadOnlyList<(string, string)> Files(string text)
    {
        var files = new List<(string, string)>();
        string? name = null;
        var body = new System.Text.StringBuilder();
        foreach (string line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("-- file: ", StringComparison.Ordinal))
            {
                if (name != null) files.Add((name, body.ToString()));
                name = line.Trim()["-- file: ".Length..];
                body.Clear();
            }
            else body.Append(line).Append('\n');
        }
        if (name != null) files.Add((name, body.ToString()));
        return files;
    }

    private LuaStackBuilder Mod(string id, IReadOnlyList<(string Path, string Text)> files, bool vm, bool library, string[] requires)
    {
        _mods.Add(new ModRuntimeMod(id, "1.0.0", library, vm, requires,
            files.Select(file => new ModSource(id, file.Path, file.Text)).ToList()));
        return this;
    }

    /// <summary>A base mod: its layout, Lua files if it has any, and the mods it requires.</summary>
    public LuaStackBuilder Base(string id, GameLayout layout, string files = "", params string[] requires)
    {
        _mods.Add(new ModRuntimeMod(id, "1.0.0", false, true, requires,
            Files(files).Select(file => new ModSource(id, file.Item1, file.Item2)).ToList(), layout));
        return this;
    }

    /// <summary>An unpacked mod run under <c>--lua-dev</c>, as the link marks it.</summary>
    public LuaStackBuilder LuaDev(string id)
    {
        int index = _mods.FindIndex(mod => mod.Id == id);
        _mods[index] = _mods[index] with { LuaDev = true };
        return this;
    }

    /// <summary>A mod's <c>[lua] writes</c>, as the link leaves it: global names.</summary>
    public LuaStackBuilder Writes(string id, params string[] names)
    {
        int index = _mods.FindIndex(mod => mod.Id == id);
        _mods[index] = _mods[index] with { Writes = names };
        return this;
    }

    /// <summary>A mod's <c>[lua] calls</c>, as the link leaves it: catalog names without <c>.BIN</c>.</summary>
    public LuaStackBuilder Calls(string id, params string[] scripts)
    {
        int index = _mods.FindIndex(mod => mod.Id == id);
        _mods[index] = _mods[index] with { Calls = scripts };
        return this;
    }

    /// <summary>A mod's own new script on its <c>[lua] calls</c>, as the link leaves it: <paramref name="name"/> to its
    /// catalog name.</summary>
    public LuaStackBuilder OwnScript(string id, string name, string catalogName)
    {
        int index = _mods.FindIndex(mod => mod.Id == id);
        ModRuntimeMod mod = _mods[index];
        var own = new Dictionary<string, string>(mod.OwnScripts ?? new Dictionary<string, string>()) { [name] = catalogName };
        _mods[index] = mod with { Calls = [.. mod.CallList, catalogName], OwnScripts = own };
        return this;
    }

    private HandlerId Handler(string mod, string handler)
    {
        // One id per (mod, file, function), as the link gives it.
        int colon = handler.IndexOf(':');
        var entry = new ModRuntimeHandler(mod, handler[..colon], handler[(colon + 1)..]);
        int found = _handlers.IndexOf(entry);
        if (found >= 0) return new HandlerId(found);
        _handlers.Add(entry);
        return new HandlerId(_handlers.Count - 1);
    }

    private int LoadOrder(string mod) => _mods.FindIndex(entry => entry.Id == mod);

    /// <summary>A handler at an instruction: a point hook, or with <paramref name="gameEvent"/> a subscriber of that
    /// event at the instruction's point.</summary>
    public LuaStackBuilder Point(Script script, int index, string mod, string handler, int priority = 0, string point = "test.point",
                                 string? gameEvent = null, bool skip = false)
    {
        HandlerId id = Handler(mod, handler);
        int offset = script.Instructions[index].Offset;
        if (!_sites.TryGetValue(script.Name, out var sites)) _sites[script.Name] = sites = new();
        int at = sites.FindIndex(site => site.Offset == offset);
        var planned = new PlannedHandler(id, priority, LoadOrder(mod), skip, GameEvent: gameEvent);
        if (at < 0) sites.Add(new PlannedSite(offset, offset, point, [planned]));
        else sites[at] = sites[at] with
        {
            Handlers = sites[at].Handlers.Append(planned).OrderBy(h => h.Priority).ThenBy(h => h.LoadOrder).ToList(),
        };
        return this;
    }

    /// <summary>A site hook at an instruction (spec §6.3): in its before list with the points, or with
    /// <paramref name="when"/> <c>after</c> in its after list; each list in the merged order.</summary>
    public LuaStackBuilder Site(Script script, int index, string mod, string handler, string when = "before", int priority = 0,
                                bool skip = false)
    {
        HandlerId id = Handler(mod, handler);
        int offset = script.Instructions[index].Offset;
        if (!_sites.TryGetValue(script.Name, out var sites)) _sites[script.Name] = sites = new();
        int at = sites.FindIndex(site => site.Offset == offset);
        PlannedSite site = at < 0 ? new PlannedSite(offset, offset, null, []) : sites[at];
        var planned = new PlannedHandler(id, priority, LoadOrder(mod), skip, Site: true);
        static List<PlannedHandler> With(IReadOnlyList<PlannedHandler> list, PlannedHandler handler)
            => list.Append(handler).OrderBy(h => h.Priority).ThenBy(h => h.LoadOrder).ToList();
        site = when == "after" ? site with { After = With(site.AfterList, planned) } : site with { Handlers = With(site.Handlers, planned) };
        if (at < 0) sites.Add(site);
        else sites[at] = site;
        return this;
    }

    /// <summary>A script hook (spec §7.2): <paramref name="when"/> <c>enter</c> or <c>exit</c>, in the merged order.</summary>
    public LuaStackBuilder ScriptHook(string script, string when, string mod, string handler, int priority = 0)
    {
        HandlerId id = Handler(mod, handler);
        if (!_scripts.TryGetValue(script, out var lists)) _scripts[script] = lists = (new(), new());
        List<PlannedHandler> list = when == "enter" ? lists.Enter : lists.Exit;
        list.Add(new PlannedHandler(id, priority, LoadOrder(mod)));
        list.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.LoadOrder.CompareTo(b.LoadOrder));
        return this;
    }

    /// <summary>A handler of a watch (spec §7.3): <paramref name="watch"/> as the manifest names it, over
    /// <paramref name="count"/> cells from <paramref name="address"/>; a watch on a name gives its global and shape. One
    /// watch per (bank, address, count), its handlers in the merged order, as the link builds it.</summary>
    public LuaStackBuilder Watch(string watch, int address, string mod, string handler, int count = 1, int priority = 0,
                                 CellKind kind = CellKind.Integer, string? global = null, int dimensions = 0, int rowWidth = 1)
    {
        var planned = new PlannedHandler(Handler(mod, handler), priority, LoadOrder(mod));
        int at = _watches.FindIndex(entry => entry.Kind == kind && entry.Address == address && entry.Count == count);
        if (at < 0) _watches.Add(new PlannedWatch(watch, kind, address, count, [planned], global, dimensions, rowWidth));
        else _watches[at] = _watches[at] with
        {
            Handlers = _watches[at].Handlers.Append(planned).OrderBy(h => h.Priority).ThenBy(h => h.LoadOrder).ToList(),
        };
        return this;
    }

    public LuaStackBuilder Engine(string name, string mod, string handler, int priority = 0)
    {
        HandlerId id = Handler(mod, handler);
        if (!_engine.TryGetValue(name, out var list)) _engine[name] = list = new();
        list.Add(new PlannedHandler(id, priority, LoadOrder(mod)));
        return this;
    }

    /// <summary>An event the stack declares, as the link builds it.</summary>
    public LuaStackBuilder Event(GameEvent gameEvent)
    {
        _events.Add(gameEvent);
        return this;
    }

    /// <summary>A subscriber of an event raised from Lua, in the merged order.</summary>
    public LuaStackBuilder LuaEvent(string name, string mod, string handler, int priority = 0)
    {
        HandlerId id = Handler(mod, handler);
        if (!_luaEvents.TryGetValue(name, out var list)) _luaEvents[name] = list = new();
        list.Add(new PlannedHandler(id, priority, LoadOrder(mod)));
        _luaEvents[name] = list.OrderBy(h => h.Priority).ThenBy(h => h.LoadOrder).ToList();
        return this;
    }

    public (ModRuntimeStack Stack, HookPlan Plan) Build()
        => (new ModRuntimeStack(_mods, _handlers, _events),
            new HookPlan(_sites.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlannedSite>)pair.Value),
                         _engine.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlannedHandler>)pair.Value),
                         _luaEvents.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlannedHandler>)pair.Value),
                         _scripts.ToDictionary(pair => pair.Key, pair => new PlannedScript(pair.Value.Enter, pair.Value.Exit)),
                         _watches));
}
