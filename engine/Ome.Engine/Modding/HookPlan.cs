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
/// <c>[game]</c>), whether it declared <c>vm</c>, the mods it requires, and its Lua sources. A base mod also has its
/// <paramref name="Layout"/> (spec §9.1); <paramref name="Writes"/> are the global names its <c>[lua] writes</c> lists,
/// each an exported name's global (spec §9.3); <paramref name="Calls"/> the scripts its <c>[lua] calls</c> lists, as
/// catalog names without <c>.BIN</c> (spec §10), and <paramref name="OwnScripts"/> the names of its own new scripts among
/// them, each to its catalog name (<c>$selector$NAME</c>).</summary>
public sealed record ModRuntimeMod(string Id, string Version, bool Library, bool Vm, IReadOnlyList<string> Requires,
                                   IReadOnlyList<ModSource> Files, GameLayout? Layout = null,
                                   IReadOnlyList<string>? Writes = null, bool LuaDev = false,
                                   IReadOnlyList<string>? Calls = null,
                                   IReadOnlyDictionary<string, string>? OwnScripts = null)
{
    // LuaDev: an unpacked mod run under --lua-dev (spec §9.3, §13): a write to a name its list lacks goes through and
    // is logged once, where it would be an error.
    /// <summary>The write list, empty when the mod lists none.</summary>
    public IReadOnlyList<string> WriteList => Writes ?? [];

    /// <summary>The scripts the mod may call, empty when it lists none.</summary>
    public IReadOnlyList<string> CallList => Calls ?? [];
}

/// <summary>The mods that ship Lua, in load order, every handler their hooks name, and the events they declare.</summary>
public sealed record ModRuntimeStack(IReadOnlyList<ModRuntimeMod> Mods, IReadOnlyList<ModRuntimeHandler> Handlers,
                                     IReadOnlyList<GameEvent>? Events = null)
{
    public static readonly ModRuntimeStack Empty = new([], []);

    public bool HasLua => Mods.Count > 0;

    public ModRuntimeHandler this[HandlerId handler] => Handlers[handler.Index];

    /// <summary>The declared events, empty when none.</summary>
    public IReadOnlyList<GameEvent> EventList => Events ?? [];

    public GameEvent? Event(string name)
    {
        foreach (GameEvent gameEvent in EventList)
            if (gameEvent.Name == name) return gameEvent;
        return null;
    }
}

/// <summary>One handler due at a place, in the merged order of spec §7.4. <paramref name="EngineEvent"/> names the
/// engine event the handler subscribes to when the event's subscribers take their places in an instruction's list
/// (<c>ome.save.writing</c> before each numbered save, spec §7.7); <paramref name="GameEvent"/> names the game event at
/// the instruction's point that it subscribes to (spec §9.2). Such a handler is given the event's context.
/// <paramref name="Site"/> marks a site hook's handler (spec §6.3), which its context and the trace name as one.</summary>
public sealed record PlannedHandler(HandlerId Handler, int Priority, int LoadOrder, bool Skip = false,
                                    string? EngineEvent = null, string? GameEvent = null, bool Site = false);

/// <summary>A hooked instruction (spec §7.1, §7.5): its linked offset, its original offset, the point it is if it is
/// one, its handlers before it in order, and its <c>after</c> hooks in order.</summary>
public sealed record PlannedSite(int Offset, int Site, string? Point, IReadOnlyList<PlannedHandler> Handlers,
                                 IReadOnlyList<PlannedHandler>? After = null)
{
    /// <summary>The <c>after</c> hooks, empty when it has none.</summary>
    public IReadOnlyList<PlannedHandler> AfterList => After ?? [];
}

/// <summary>A script's entry and exit hooks (spec §7.2), each list in the merged order.</summary>
public sealed record PlannedScript(IReadOnlyList<PlannedHandler> Enter, IReadOnlyList<PlannedHandler> Exit);

/// <summary>
/// A watch (spec §7.3): <paramref name="Count"/> cells from <paramref name="Address"/> in the bank of
/// <paramref name="Kind"/> (an integer or a string bank), how the manifest names it (<c>exp</c>, <c>exp[*]</c>,
/// <c>G[0x5000]</c>), and its handlers in the merged order. A watch on a name keeps the global's name and shape
/// (<paramref name="Dimensions"/>, <paramref name="RowWidth"/>), so a handler is given a cell's row and column.
/// </summary>
public sealed record PlannedWatch(string Watch, CellKind Kind, int Address, int Count, IReadOnlyList<PlannedHandler> Handlers,
                                  string? Global = null, int Dimensions = 0, int RowWidth = 1)
{
    public bool Covers(CellKind kind, int address) => kind == Kind && address >= Address && address - Address < Count;

    /// <summary>The cells, as the report and the trace write them: <c>G[0x5000]</c> or <c>G[0x5000..0x5063]</c>.</summary>
    public string Cells => (Kind == CellKind.String ? "GS" : "G")
                           + (Count == 1 ? $"[0x{Address:x}]" : $"[0x{Address:x}..0x{Address + Count - 1:x}]");
}

/// <summary>
/// The hook plan the link builds (spec §7.5): per script, the hooked instructions with their handlers and its entry
/// and exit hooks, and per engine event, its handlers. A frame takes its script's table (<see cref="ScriptHooks"/>)
/// when it is entered.
/// </summary>
public sealed class HookPlan
{
    public static readonly HookPlan Empty = new(new Dictionary<string, IReadOnlyList<PlannedSite>>(),
                                                new Dictionary<string, IReadOnlyList<PlannedHandler>>());

    /// <param name="sites">By script name, either form (<c>ADDEXP</c> or <c>ADDEXP.BIN</c>).</param>
    /// <param name="luaEvents">The subscribers of each game event raised from Lua (spec §8.2), by event name.</param>
    /// <param name="scripts">Each script's entry and exit hooks, by script name in either form.</param>
    public HookPlan(IReadOnlyDictionary<string, IReadOnlyList<PlannedSite>> sites,
                    IReadOnlyDictionary<string, IReadOnlyList<PlannedHandler>> engineEvents,
                    IReadOnlyDictionary<string, IReadOnlyList<PlannedHandler>>? luaEvents = null,
                    IReadOnlyDictionary<string, PlannedScript>? scripts = null, IReadOnlyList<PlannedWatch>? watches = null)
    {
        Sites = sites.ToDictionary(pair => TextCatalog.NormalizeScriptName(pair.Key), pair => pair.Value,
                                   StringComparer.Ordinal);
        EngineEvents = engineEvents;
        LuaEvents = luaEvents ?? new Dictionary<string, IReadOnlyList<PlannedHandler>>();
        Scripts = (scripts ?? new Dictionary<string, PlannedScript>())
            .ToDictionary(pair => TextCatalog.NormalizeScriptName(pair.Key), pair => pair.Value, StringComparer.Ordinal);
        Watches = watches ?? [];
    }

    /// <summary>The watches (spec §7.3), each once for the cells it covers.</summary>
    public IReadOnlyList<PlannedWatch> Watches { get; }

    /// <summary>By normalized script name (<c>ADDEXP</c>), its entry and exit hooks.</summary>
    public IReadOnlyDictionary<string, PlannedScript> Scripts { get; }

    /// <summary>By game event name, the subscribers of an event raised from Lua, each list in the merged order.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PlannedHandler>> LuaEvents { get; }

    public IReadOnlyList<PlannedHandler> SubscribersOf(string luaEvent)
        => LuaEvents.TryGetValue(luaEvent, out var handlers) ? handlers : [];

    /// <summary>By normalized script name (<c>ADDEXP</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PlannedSite>> Sites { get; }

    /// <summary>By event name (<c>ome.session.start</c>), each list in the merged order.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PlannedHandler>> EngineEvents { get; }

    public bool IsEmpty => Sites.Count == 0 && EngineEvents.Count == 0 && LuaEvents.Count == 0 && Scripts.Count == 0
                           && Watches.Count == 0;

    public IReadOnlyList<PlannedHandler> HandlersOf(string engineEvent)
        => EngineEvents.TryGetValue(engineEvent, out var handlers) ? handlers : [];
}

/// <summary>A hooked instruction of one loaded script, as the dispatch reads it.</summary>
public sealed class HookSite(PlannedSite planned, string script)
{
    public PlannedSite Planned { get; } = planned;
    public string Script { get; } = script;

    /// <summary>Whether it has <c>after</c> hooks, which the loop runs when the opcode handler returns.</summary>
    public bool HasAfter { get; } = planned.AfterList.Count > 0;

    /// <summary>Whether a handler before it declared <c>skip = true</c> (spec §7.1).</summary>
    public bool MaySkip { get; } = planned.Handlers.Any(handler => handler.Skip);
}

/// <summary>A script's table (spec §7.5): instruction index to its hooked site, or null for none, and its entry and
/// exit hooks (spec §7.2). A script with no hooks has no table, so the loop tests one reference per instruction.</summary>
public sealed class ScriptHooks(HookSite?[] byIndex, PlannedScript? script = null)
{
    public HookSite?[] ByIndex { get; } = byIndex;

    /// <summary>The handlers before the first instruction of a frame that starts at the script's entry.</summary>
    public IReadOnlyList<PlannedHandler> Enter { get; } = script?.Enter ?? [];

    /// <summary>The handlers when a frame of the script returns.</summary>
    public IReadOnlyList<PlannedHandler> Exit { get; } = script?.Exit ?? [];

    /// <summary>The table of <paramref name="script"/> under <paramref name="sites"/>, or null when none of them
    /// names one of its instructions. A site whose offset is no instruction start is reported through
    /// <paramref name="unbound"/>. An instruction that <paramref name="takesEvent"/> accepts also gets
    /// <paramref name="eventHandlers"/>, merged into its list by priority and then load order (spec §7.4); its
    /// original offset comes from <paramref name="site"/>. <paramref name="entryAndExit"/> are the script's entry and
    /// exit hooks.</summary>
    public static ScriptHooks? For(Script script, IReadOnlyList<PlannedSite> sites, Action<PlannedSite>? unbound = null,
                                   Func<Instruction, bool>? takesEvent = null,
                                   IReadOnlyList<PlannedHandler>? eventHandlers = null, Func<int, int?>? site = null,
                                   PlannedScript? entryAndExit = null)
    {
        HookSite?[]? table = null;
        if (entryAndExit is { Enter.Count: > 0 } or { Exit.Count: > 0 }) table = new HookSite?[script.Instructions.Count];
        foreach (PlannedSite planned in sites)
        {
            if (!script.IndexByOffset.TryGetValue(planned.Offset, out int index))
            {
                unbound?.Invoke(planned);
                continue;
            }
            table ??= new HookSite?[script.Instructions.Count];
            table[index] = new HookSite(planned, script.Name);
        }
        if (takesEvent != null && eventHandlers is { Count: > 0 })
            for (int index = 0; index < script.Instructions.Count; index++)
            {
                Instruction instruction = script.Instructions[index];
                if (!takesEvent(instruction)) continue;
                table ??= new HookSite?[script.Instructions.Count];
                PlannedSite merged = table[index]?.Planned
                                     ?? new PlannedSite(instruction.Offset, site?.Invoke(instruction.Offset) ?? -1, null, []);
                merged = merged with
                {
                    Handlers = merged.Handlers.Concat(eventHandlers)
                        .OrderBy(handler => handler.Priority).ThenBy(handler => handler.LoadOrder).ToList(),
                };
                table[index] = new HookSite(merged, script.Name);
            }
        return table == null ? null : new ScriptHooks(table, entryAndExit);
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
