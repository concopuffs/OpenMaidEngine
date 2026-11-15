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

    public LuaStackBuilder Mod(string id, string files, bool vm = true, bool library = false, params string[] requires)
        => Mod(id, Files(files), vm, library, requires);

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

    /// <summary>A base mod: its layout, and Lua files if it has any.</summary>
    public LuaStackBuilder Base(string id, GameLayout layout, string files = "")
    {
        _mods.Add(new ModRuntimeMod(id, "1.0.0", false, true, [],
            Files(files).Select(file => new ModSource(id, file.Item1, file.Item2)).ToList(), layout));
        return this;
    }

    /// <summary>A mod's <c>[lua] writes</c>, as the link leaves it: global names.</summary>
    public LuaStackBuilder Writes(string id, params string[] names)
    {
        int index = _mods.FindIndex(mod => mod.Id == id);
        _mods[index] = _mods[index] with { Writes = names };
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

    public LuaStackBuilder Point(Script script, int index, string mod, string handler, int priority = 0, string point = "test.point")
    {
        HandlerId id = Handler(mod, handler);
        int offset = script.Instructions[index].Offset;
        if (!_sites.TryGetValue(script.Name, out var sites)) _sites[script.Name] = sites = new();
        int at = sites.FindIndex(site => site.Offset == offset);
        var planned = new PlannedHandler(id, priority, LoadOrder(mod));
        if (at < 0) sites.Add(new PlannedSite(offset, offset, point, [planned]));
        else sites[at] = sites[at] with
        {
            Handlers = sites[at].Handlers.Append(planned).OrderBy(h => h.Priority).ThenBy(h => h.LoadOrder).ToList(),
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

    public (ModRuntimeStack Stack, HookPlan Plan) Build()
        => (new ModRuntimeStack(_mods, _handlers),
            new HookPlan(_sites.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlannedSite>)pair.Value),
                         _engine.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlannedHandler>)pair.Value)));
}
