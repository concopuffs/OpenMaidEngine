using Ome.Engine.Localization;
using Ome.Engine.Model;

namespace Ome.Engine.Modding;

/// <summary>A handler of the stack: its index in <see cref="ModRuntimeStack.Handlers"/>.</summary>
public readonly record struct HandlerId(int Index);

/// <summary>A handler (spec §6.2): a function the mod declares at the top level of a file under <c>lua/</c>.</summary>
public sealed record ModRuntimeHandler(string ModId, string File, string Function)
{
    public override string ToString() => $"{ModId} {File}:{Function}";
}

/// <summary>A mod of the session, as the link left it (spec §6): its id and version, whether it is a library (no
/// <c>[game]</c>), whether it declared <c>vm</c>, the mods it requires, and its Lua sources.</summary>
public sealed record ModRuntimeMod(string Id, string Version, bool Library, bool Vm, IReadOnlyList<string> Requires,
                                   IReadOnlyList<ModSource> Files);

/// <summary>The mods that ship Lua, in load order, and every handler their hooks name.</summary>
public sealed record ModRuntimeStack(IReadOnlyList<ModRuntimeMod> Mods, IReadOnlyList<ModRuntimeHandler> Handlers)
{
    public static readonly ModRuntimeStack Empty = new([], []);

    public bool HasLua => Mods.Count > 0;

    public ModRuntimeHandler this[HandlerId handler] => Handlers[handler.Index];
}

/// <summary>One handler due at a place, in the merged order of spec §7.4.</summary>
public sealed record PlannedHandler(HandlerId Handler, int Priority, int LoadOrder, bool Skip = false);

/// <summary>A hooked instruction (spec §7.1, §7.5): its linked offset, its original offset, the point it is if it is
/// one, and its handlers in order.</summary>
public sealed record PlannedSite(int Offset, int Site, string? Point, IReadOnlyList<PlannedHandler> Handlers);

/// <summary>
/// The hook plan the link builds (spec §7.5): per script, the hooked instructions with their handlers, and per engine
/// event, its handlers. A frame takes its script's table (<see cref="ScriptHooks"/>) when it is entered.
/// </summary>
public sealed class HookPlan
{
    public static readonly HookPlan Empty = new(new Dictionary<string, IReadOnlyList<PlannedSite>>(),
                                                new Dictionary<string, IReadOnlyList<PlannedHandler>>());

    /// <param name="sites">By script name, either form (<c>ADDEXP</c> or <c>ADDEXP.BIN</c>).</param>
    public HookPlan(IReadOnlyDictionary<string, IReadOnlyList<PlannedSite>> sites,
                    IReadOnlyDictionary<string, IReadOnlyList<PlannedHandler>> engineEvents)
    {
        Sites = sites.ToDictionary(pair => TextCatalog.NormalizeScriptName(pair.Key), pair => pair.Value,
                                   StringComparer.Ordinal);
        EngineEvents = engineEvents;
    }

    /// <summary>By normalized script name (<c>ADDEXP</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PlannedSite>> Sites { get; }

    /// <summary>By event name (<c>ome.session.start</c>), each list in the merged order.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PlannedHandler>> EngineEvents { get; }

    public bool IsEmpty => Sites.Count == 0 && EngineEvents.Count == 0;

    public IReadOnlyList<PlannedHandler> HandlersOf(string engineEvent)
        => EngineEvents.TryGetValue(engineEvent, out var handlers) ? handlers : [];
}

/// <summary>A hooked instruction of one loaded script, as the dispatch reads it.</summary>
public sealed class HookSite(PlannedSite planned, string script)
{
    public PlannedSite Planned { get; } = planned;
    public string Script { get; } = script;
}

/// <summary>A script's table (spec §7.5): instruction index to its hooked site, or null for none. A script with no
/// hooks has no table, so the loop tests one reference per instruction.</summary>
public sealed class ScriptHooks(HookSite?[] byIndex)
{
    public HookSite?[] ByIndex { get; } = byIndex;

    /// <summary>The table of <paramref name="script"/> under <paramref name="sites"/>, or null when none of them
    /// names one of its instructions. A site whose offset is no instruction start is reported through
    /// <paramref name="unbound"/>.</summary>
    public static ScriptHooks? For(Script script, IReadOnlyList<PlannedSite> sites, Action<PlannedSite>? unbound = null)
    {
        HookSite?[]? table = null;
        foreach (PlannedSite site in sites)
        {
            if (!script.IndexByOffset.TryGetValue(site.Offset, out int index))
            {
                unbound?.Invoke(site);
                continue;
            }
            table ??= new HookSite?[script.Instructions.Count];
            table[index] = new HookSite(site, script.Name);
        }
        return table == null ? null : new ScriptHooks(table);
    }
}

/// <summary>Where a linked script's instructions came from (spec §8.1, "Positions"): the original offset of a linked
/// one, and the mods that patched the script. The composition builds it from the mounted layer; without mods every
/// offset is its own original.</summary>
public interface IScriptSites
{
    /// <summary>The original offset of the instruction at linked <paramref name="offset"/>, or null when a patch
    /// inserted it or a mod replaced the script.</summary>
    int? Site(string script, int offset);

    /// <summary>The mods that changed <paramref name="script"/>, in load order: its replacement's, then its
    /// patches'.</summary>
    IReadOnlyList<string> ChangedBy(string script);
}

/// <summary>The sites of an unmodded run: every offset is its original.</summary>
public sealed class UnchangedScriptSites : IScriptSites
{
    public static readonly UnchangedScriptSites Instance = new();
    public int? Site(string script, int offset) => offset;
    public IReadOnlyList<string> ChangedBy(string script) => [];
}
