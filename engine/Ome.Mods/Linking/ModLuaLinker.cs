using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Engine.Sys4;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;
using Ome.Script.Patch;

namespace Ome.Mods.Linking;

/// <summary>The Lua part of a link (Lua scripting spec §6, §7): the mods that ship Lua with their sources, every
/// handler their hooks name, the hook plan bound to the linked scripts, and the writes the source names that a mod's
/// list lacks (spec §9.3), which <c>omp build</c> refuses.</summary>
public sealed record LinkedLua(ModRuntimeStack Stack, HookPlan Plan, IReadOnlyList<ModUndeclaredWrite>? UndeclaredWrites = null)
{
    public static readonly LinkedLua None = new(ModRuntimeStack.Empty, HookPlan.Empty);

    public IReadOnlyList<ModUndeclaredWrite> Undeclared => UndeclaredWrites ?? [];
}

/// <summary>A write the source spells out (<c>game.g.NAME = …</c>, <c>game.table("NAME")[…] = …</c>) to a name a base
/// mod the mod requires exports, which the mod's <c>[lua] writes</c> lacks: <paramref name="Name"/> is the global's.</summary>
public sealed record ModUndeclaredWrite(string Mod, string File, string Name);

/// <summary>
/// Links the stack's Lua (Lua scripting spec §6.2–§6.4, §7.1, §7.6, §9.2), after the scripts and the text. Every file
/// is checked through the runtime's <see cref="IModRuntime.Check"/>, which runs no mod code. Every hook is checked
/// against what this engine builds, its handler against the top-level functions of its file, and a point hook is
/// bound through the patch points to the base script's offset and then through the linked script's offset table; a
/// site hook names that offset and its revision itself.
/// The events under <c>events/</c> are read first, so an event hook is checked against them, and an event at a point
/// is bound as a point hook is. One <see cref="HandlerId"/> stands for each (mod, file, function).
/// </summary>
public static class ModLuaLinker
{
    /// <summary>The engine events this engine raises; the others are built later or reserved (spec §7.7).</summary>
    public static readonly IReadOnlySet<string> BuiltEngineEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        Scripting.SessionStartEvent, Scripting.SessionResumedEvent, Scripting.SaveWritingEvent,
    };

    private static readonly IReadOnlyDictionary<string, string> LaterEngineEvents = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ome.text.present"] = "is reserved for the localization filter (Lua scripting spec §7.7)",
    };

    /// <summary>The hook forms this engine builds; the reader takes all six (spec §6.3).</summary>
    private static string? NotBuilt(ModHook hook) => hook.Form switch
    {
        ModHookForm.Script => "script entry and exit hooks come with the wider hooks (Lua scripting milestone 4)",
        ModHookForm.Watch => "watches come with the wider hooks (Lua scripting milestone 4)",
        _ when hook.Skip => "skip = true comes with the wider hooks (Lua scripting milestone 4)",
        _ => null,
    };

    /// <summary>A hook on its way into the plan; <paramref name="GameEvent"/> names the event at a point that it
    /// subscribes to.</summary>
    private sealed record Pending(ModHook Hook, HandlerId Handler, int LoadOrder, string? GameEvent = null);

    /// <summary>An event the link read, with its place when it is raised at a point.</summary>
    private sealed record DeclaredEvent(GameEvent Event, (string Script, int Offset, int Site)? Place);

    public static LinkedLua Link(IReadOnlyList<ModPackage> order, ModLinkBase linkBase,
                                 IReadOnlyDictionary<string, LinkedScript> scripts, ModLinkReport report)
    {
        var luaMods = order.Where(ShipsLua).ToList();
        if (luaMods.Count == 0) return LinkedLua.None;
        if (linkBase.Lua is not { } lazy)
        {
            foreach (ModPackage mod in luaMods)
                report.Add(ModReportClass.Error, mod.Id, null, "the mod ships Lua, and this build has no Lua runtime");
            return LinkedLua.None;
        }
        IModRuntime runtime = lazy.Get();
        var loadOrders = order.Select((mod, index) => (mod.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index,
                                                                                    StringComparer.Ordinal);

        var mods = new List<ModRuntimeMod>();
        var handlers = new List<ModRuntimeHandler>();
        var handlerIds = new Dictionary<ModRuntimeHandler, HandlerId>();
        // Point and site hooks, and the events at points: by instruction. A site hook has no point.
        var pointHooks = new List<(Pending Pending, string Script, int Offset, int Site, string? Point)>();
        var engineHooks = new List<(Pending Pending, string Event)>();
        var luaEventHooks = new List<(Pending Pending, string Event)>();
        // Spec §9.1, plan §7.1: the profile's patch points, else the copy a base mod ships.
        OmsPatchPoints? points = linkBase.PatchPoints ?? order.Select(mod => mod.BasePoints).FirstOrDefault(found => found != null);
        var (declared, brokenEvents) = Events(luaMods, order, linkBase, points, scripts, report);
        var undeclared = new List<ModUndeclaredWrite>();

        foreach (ModPackage mod in luaMods)
        {
            int loadOrder = loadOrders[mod.Id];
            int problemsBefore = report.Lines.Count(line => line.Stops);
            ModLuaSettings? settings = mod.Manifest.Lua;
            bool vm = settings?.HasVm == true;
            void Error(string? script, string message) => report.Add(ModReportClass.Error, mod.Id, script, message);

            if (settings == null) Error(null, "lua/ needs a [lua] table in the manifest");
            if (settings is { Calls.Count: > 0 })
                Error(null, "lua.calls: script calls come with Lua scripting milestone 5");

            // The sources, each checked without running it.
            var requires = mod.Manifest.Requires.Select(requirement => requirement.Id).Where(loadOrders.ContainsKey).ToList();
            var bases = order.Where(package => package.BaseLayout != null && requires.Contains(package.Id, StringComparer.Ordinal))
                .ToList();
            IReadOnlyList<string> writes = Writes(mod, bases, report);
            // Spec §9.3, §13: --lua-dev applies to an unpacked mod that names cells through a base mod.
            bool luaDev = false;
            if (linkBase.LuaDev && bases.Count > 0 && mod.BaseLayout == null)
            {
                luaDev = mod.Unpacked;
                report.Add(ModReportClass.Note, mod.Id, null, luaDev
                    ? "runs under --lua-dev, so it is opaque: a write to a name its [lua] writes list lacks goes through, and "
                      + "diagnostics/lua.log names it once"
                    : "is packed, so --lua-dev does not apply: its [lua] writes list is enforced");
            }
            var sources = new List<ModSource>();
            var checks = new Dictionary<string, ModSourceCheck>(StringComparer.Ordinal);
            foreach (ModLuaFile file in mod.Layout.Lua)
            {
                string text;
                try
                {
                    text = mod.ReadText(file.File);
                }
                catch (ModPackageException error)
                {
                    Error(null, error.Message);
                    continue;
                }
                var source = new ModSource(mod.Id, file.Path, text);
                ModSourceCheck check = runtime.Check(source);
                foreach (ModSourceError error in check.Errors) Error(null, $"lua/{file.Path}: {error}");
                string module = ModuleName(file.Path);
                if (requires.Contains(module, StringComparer.Ordinal))
                    Error(null, $"lua/{file.Path} is module '{module}', which is also the id of a mod it requires, so "
                                + $"require(\"{module}\") would name both; rename the file");
                sources.Add(source);
                checks[file.Path] = check;
            }

            // Spec §9.3: the writes the source spells out, against the list. A base mod's own names have no list.
            if (bases.Count > 0 && mod.BaseLayout == null) ScanWrites(mod, bases, writes, checks, luaDev, undeclared, report);

            // The hooks.
            foreach (ModHook hook in mod.Manifest.Hooks)
            {
                string where = hook.Describe();
                if (NotBuilt(hook) is { } later)
                {
                    Error(null, $"{where}: {later}; this engine does not build it yet");
                    continue;
                }
                if (hook.Form == ModHookForm.Engine && !BuiltEngineEvents.Contains(hook.Target))
                {
                    Error(null, LaterEngineEvents.TryGetValue(hook.Target, out string? why)
                        ? $"{where}: {hook.Target} {why}; this engine does not raise it yet"
                        : $"{where}: '{hook.Target}' is not an engine event (this engine raises "
                          + string.Join(", ", BuiltEngineEvents.Order(StringComparer.Ordinal)) + ")");
                    continue;
                }
                // A point or an event first: neither depends on the handler's file, so a broken file does not hide a
                // wrong target.
                (string Script, int Offset, int Site)? bound = null;
                bool pointFails = false;
                DeclaredEvent? subscribed = null;
                if (hook.Form == ModHookForm.Point)
                {
                    if (!vm)
                        Error(null, $"{where}: a point hook hands its handler the frame and operands, which needs the vm "
                                    + "capability ([lua] capabilities = [\"vm\"])");
                    else bound = BindPoint(mod, where, hook.Target, points, linkBase, scripts, report);
                    pointFails = bound == null;
                }
                else if (hook.Form == ModHookForm.Site)
                {
                    if (!vm)
                        Error(null, $"{where}: a site hook hands its handler the frame and operands, which needs the "
                                           + "vm capability ([lua] capabilities = [\"vm\"])");
                    else bound = BindSite(mod, where, hook, linkBase, scripts, report);
                    pointFails = bound == null;
                }
                else if (hook.Form == ModHookForm.Event)
                {
                    if (brokenEvents.Contains(hook.Target)) continue;   // its declaration's error is reported
                    if (!declared.TryGetValue(hook.Target, out subscribed))
                    {
                        Error(null, $"{where}: no mod of the stack declares the event '{hook.Target}'"
                                    + (declared.Count == 0 ? "" : $" (it declares {string.Join(", ", declared.Keys.Order(StringComparer.Ordinal))})"));
                        continue;
                    }
                    string owner = subscribed.Event.Owner;
                    if (owner != mod.Id && !requires.Contains(owner, StringComparer.Ordinal))
                    {
                        Error(null, $"{where}: {owner} declares {hook.Target}, and the mod does not require it "
                                    + $"([[requires]] id = \"{owner}\")");
                        continue;
                    }
                    bound = subscribed.Place;
                }
                if (!checks.TryGetValue(hook.Handler.File, out ModSourceCheck? check)) continue;   // the layout reported it
                if (!check.Parsed) continue;   // reported above
                if (!check.TopLevelFunctions.Contains(hook.Handler.Function))
                {
                    Error(null, $"{where}: lua/{hook.Handler.File} declares no top-level function {hook.Handler.Function}; "
                                + $"a handler is a global function of its file (function {hook.Handler.Function}(…))");
                    continue;
                }
                if (pointFails) continue;   // reported above
                var runtimeHandler = new ModRuntimeHandler(mod.Id, hook.Handler.File, hook.Handler.Function);
                if (!handlerIds.TryGetValue(runtimeHandler, out HandlerId id))
                {
                    handlerIds[runtimeHandler] = id = new HandlerId(handlers.Count);
                    handlers.Add(runtimeHandler);
                }
                if (subscribed is { Place: null })
                    luaEventHooks.Add((new Pending(hook, id, loadOrder), hook.Target));
                else if (subscribed is { Place: { } place })
                    pointHooks.Add((new Pending(hook, id, loadOrder, hook.Target), place.Script, place.Offset, place.Site,
                                    subscribed.Event.Point!));
                else if (hook.Form == ModHookForm.Engine) engineHooks.Add((new Pending(hook, id, loadOrder), hook.Target));
                else pointHooks.Add((new Pending(hook, id, loadOrder), bound!.Value.Script, bound.Value.Offset, bound.Value.Site,
                                     hook.Form == ModHookForm.Point ? hook.Target : null));
            }
            // A base mod is in the session with no Lua of its own: the accessors of the mods that require it read its
            // layout (spec §9.1, §9.3).
            if (report.Lines.Count(line => line.Stops) == problemsBefore && (sources.Count > 0 || mod.BaseLayout != null))
                mods.Add(new ModRuntimeMod(mod.Id, mod.Manifest.Version.ToString(), mod.Manifest.TargetsAnyProfile, vm,
                                           requires, sources, mod.BaseLayout, writes, luaDev));
            // Spec §15: the report shows what each mod may change, so a player can see it.
            if (vm)
                report.Add(ModReportClass.Note, mod.Id, null,
                           "has the vm capability, so it is opaque: its Lua reads and writes any cell, the frames' locals and the "
                           + "operands, so it can change any game state and the saves made after it, and this report cannot list "
                           + "what it changes");
        }

        // The merged order (spec §7.4): ascending priority, then load order; one mod's hooks keep manifest order.
        static IEnumerable<Pending> Merged(IEnumerable<Pending> due)
            => due.OrderBy(entry => entry.Hook.Priority).ThenBy(entry => entry.LoadOrder);
        PlannedHandler Planned(Pending entry)
            => new(entry.Handler, entry.Hook.Priority, entry.LoadOrder, entry.Hook.Skip, GameEvent: entry.GameEvent,
                   Site: entry.Hook.Form == ModHookForm.Site);
        // Spec §7.4: after the instruction, the after hooks run in one list by the same rule.
        static bool IsAfter(Pending entry) => entry.Hook is { Form: ModHookForm.Site, When: "after" };
        string Describe(Pending entry) => $"{handlers[entry.Handler.Index]}"
                                          + (entry.GameEvent is { } name ? $" on {name}" : "")
                                          + $" (priority {entry.Hook.Priority})";

        var sites = new Dictionary<string, IReadOnlyList<PlannedSite>>(StringComparer.Ordinal);
        foreach (var byScript in pointHooks.GroupBy(hook => TextCatalog.NormalizeScriptName(hook.Script), StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var planned = new List<PlannedSite>();
            foreach (var bySite in byScript.GroupBy(hook => hook.Offset).OrderBy(group => group.Key))
            {
                var due = Merged(bySite.Select(hook => hook.Pending).Where(entry => !IsAfter(entry))).ToList();
                var after = Merged(bySite.Select(hook => hook.Pending).Where(IsAfter)).ToList();
                string? point = bySite.Select(hook => hook.Point).FirstOrDefault(name => name != null);
                planned.Add(new PlannedSite(bySite.Key, bySite.First().Site, point, due.Select(Planned).ToList(),
                                            after.Count == 0 ? null : after.Select(Planned).ToList()));
                string at = $"0x{bySite.First().Site:x}" + (point is { } name ? $" ({name})" : "");
                if (due.Count > 1)
                    report.Add(ModReportClass.OrderedInteraction, due.Select(entry => handlers[entry.Handler.Index].ModId),
                        byScript.First().Script, $"{at}: handlers run in this order: " + string.Join(", ", due.Select(Describe)));
                if (after.Count > 1)
                    report.Add(ModReportClass.OrderedInteraction, after.Select(entry => handlers[entry.Handler.Index].ModId),
                        byScript.First().Script, $"{at}: after it, handlers run in this order: "
                                                 + string.Join(", ", after.Select(Describe)));
            }
            sites[byScript.Key] = planned;
        }
        var engine = new Dictionary<string, IReadOnlyList<PlannedHandler>>(StringComparer.Ordinal);
        foreach (var byEvent in engineHooks.GroupBy(hook => hook.Event, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var due = Merged(byEvent.Select(hook => hook.Pending)).ToList();
            engine[byEvent.Key] = due.Select(Planned).ToList();
            if (due.Count > 1)
                report.Add(ModReportClass.OrderedInteraction, due.Select(entry => handlers[entry.Handler.Index].ModId), null,
                    $"{byEvent.Key}: handlers run in this order: " + string.Join(", ", due.Select(Describe)));
        }
        // Spec §7.4: an event raised from Lua runs its subscribers inside the emit, in their own order.
        var luaEvents = new Dictionary<string, IReadOnlyList<PlannedHandler>>(StringComparer.Ordinal);
        foreach (var byEvent in luaEventHooks.GroupBy(hook => hook.Event, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var due = Merged(byEvent.Select(hook => hook.Pending)).ToList();
            luaEvents[byEvent.Key] = due.Select(Planned).ToList();
            if (due.Count > 1)
                report.Add(ModReportClass.OrderedInteraction, due.Select(entry => handlers[entry.Handler.Index].ModId), null,
                    $"{byEvent.Key}: subscribers run in this order: " + string.Join(", ", due.Select(Describe)));
        }
        // Spec §12: two mods that write one name are an ordered interaction, in load order.
        var writers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (ModRuntimeMod mod in mods.Where(mod => mod.Layout == null))
            foreach (string name in mod.WriteList)
            {
                if (!writers.TryGetValue(name, out List<string>? ids)) writers[name] = ids = new List<string>();
                ids.Add(mod.Id);
            }
        foreach (var (name, ids) in writers.Where(pair => pair.Value.Count > 1).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            report.Add(ModReportClass.OrderedInteraction, ids, null,
                       $"{name} is written by {string.Join(", then ", ids)} (load order); the trace shows each write (--lua-trace)");
        report.Lua = Summary(mods, handlers, sites, engine, luaEvents, declared);

        var events = declared.Values.Select(entry => entry.Event).OrderBy(gameEvent => gameEvent.Name, StringComparer.Ordinal).ToList();
        return new LinkedLua(new ModRuntimeStack(mods, handlers, events), new HookPlan(sites, engine, luaEvents), undeclared);
    }

    /// <summary>The report's Lua section (spec §12): each mod with its capabilities and the names it writes, each hook with
    /// its handlers in the order they run, and each event with its owner, its fields and its subscribers.</summary>
    private static List<string> Summary(IReadOnlyList<ModRuntimeMod> mods, IReadOnlyList<ModRuntimeHandler> handlers,
                                        IReadOnlyDictionary<string, IReadOnlyList<PlannedSite>> sites,
                                        IReadOnlyDictionary<string, IReadOnlyList<PlannedHandler>> engine,
                                        IReadOnlyDictionary<string, IReadOnlyList<PlannedHandler>> luaEvents,
                                        IReadOnlyDictionary<string, DeclaredEvent> declared)
    {
        var lines = new List<string>();
        var baseIds = mods.Where(mod => mod.Layout != null).Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        string Describe(PlannedHandler planned, bool withEvent = true)
            => $"{handlers[planned.Handler.Index]}"
               + (withEvent && planned.GameEvent is { } name ? $" on {name}" : "")
               + $" (priority {planned.Priority})";

        foreach (ModRuntimeMod mod in mods)
        {
            var parts = new List<string>();
            if (mod.Layout != null) parts.Add($"base mod of {mod.Layout.Globals.Count} globals");
            if (mod.Library) parts.Add("library");
            parts.Add(mod.Vm ? "capabilities vm (opaque)" : "capabilities none");
            var through = mod.Requires.Where(baseIds.Contains).ToList();
            if (mod.Layout != null) parts.Add("writes its own names, with no list");
            else
            {
                string writes = mod.WriteList.Count == 0 ? "writes no name" : "writes " + string.Join(", ", mod.WriteList);
                if (through.Count > 0) writes += $" through {string.Join(", ", through)}";
                if (mod.Vm) writes += ", and any cell by address";
                if (mod.LuaDev) writes += ", and under --lua-dev (opaque) any exported name its list lacks";
                parts.Add(writes);
            }
            lines.Add($"mod {mod.Id} {mod.Version}: {string.Join("; ", parts)}");
        }
        foreach (var (script, planned) in sites)
            foreach (PlannedSite site in planned)
            {
                var lists = new List<string>();
                if (site.Handlers.Count > 0) lists.Add(string.Join(", ", site.Handlers.Select(handler => Describe(handler))));
                if (site.AfterList.Count > 0)
                    lists.Add("after: " + string.Join(", ", site.AfterList.Select(handler => Describe(handler))));
                lines.Add($"hook {script} 0x{site.Site:x}" + (site.Point is { } point ? $" ({point})" : "") + ": "
                          + string.Join("; ", lists));
            }
        foreach (var (name, planned) in engine)
            lines.Add($"hook {name}: " + string.Join(", ", planned.Select(handler => Describe(handler))));
        foreach (DeclaredEvent entry in declared.Values.OrderBy(entry => entry.Event.Name, StringComparer.Ordinal))
        {
            GameEvent gameEvent = entry.Event;
            string where = gameEvent.RaisedByLua
                ? "raised from Lua"
                : $"at {gameEvent.Point}"
                  + (entry.Place is { } place ? $", {TextCatalog.NormalizeScriptName(place.Script)} 0x{place.Site:x}" : "");
            IEnumerable<PlannedHandler> subscribers = gameEvent.RaisedByLua
                ? luaEvents.GetValueOrDefault(gameEvent.Name) ?? []
                : sites.Values.SelectMany(planned => planned).SelectMany(site => site.Handlers)
                    .Where(handler => handler.GameEvent == gameEvent.Name);
            string fields = string.Join(", ", gameEvent.Fields.Select(field => $"{field.Name} {field.Type.Name()} "
                                                                               + (field.Writable ? "rw" : "r")));
            var listed = subscribers.Select(handler => Describe(handler, withEvent: false)).ToList();
            lines.Add($"event {gameEvent.Name} ({gameEvent.Owner}, {where}): fields {(fields.Length == 0 ? "none" : fields)}; "
                      + $"subscribers {(listed.Count == 0 ? "none" : string.Join(", ", listed))}");
        }
        return lines;
    }

    /// <summary>
    /// The write scan (spec §9.3): each name a file writes out through <c>.g</c> or <c>.table("…")</c> is resolved
    /// through the base mods the mod requires. One the list lacks is reported with the list as it should read, and
    /// recorded for <c>omp build</c>; one no base mod exports is reported. A write through a computed name, or through
    /// a local that stands for <c>game.g</c>, is checked when it runs.
    /// </summary>
    private static void ScanWrites(ModPackage mod, IReadOnlyList<ModPackage> bases, IReadOnlyList<string> writes,
                                   IReadOnlyDictionary<string, ModSourceCheck> checks, bool luaDev,
                                   List<ModUndeclaredWrite> undeclared, ModLinkReport report)
    {
        var missing = new List<ModUndeclaredWrite>();
        foreach (var (file, check) in checks.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            foreach (string written in check.WrittenNames.Distinct(StringComparer.Ordinal))
            {
                LayoutGlobal? global = null;
                foreach (ModPackage package in bases)
                    if (package.BaseLayout!.TryGet(written, out LayoutGlobal found))
                    {
                        global = found;
                        break;
                    }
                if (global == null)
                {
                    report.Add(ModReportClass.Note, mod.Id, null,
                        $"lua/{file} writes {written} by name, which {string.Join(" or ", bases.Select(package => package.Id))} "
                        + "does not export; that write fails when it runs");
                    continue;
                }
                if (!writes.Contains(global.Name, StringComparer.Ordinal) && missing.All(entry => entry.Name != global.Name))
                    missing.Add(new ModUndeclaredWrite(mod.Id, file, global.Name));
            }
        if (missing.Count == 0) return;
        undeclared.AddRange(missing);
        string what = string.Join(", ", missing.Select(entry => $"{entry.Name} (lua/{entry.File})"));
        string should = "writes = [" + string.Join(", ", writes.Concat(missing.Select(entry => entry.Name)).Select(name => $"\"{name}\""))
                        + "]";
        report.Add(ModReportClass.Note, mod.Id, null, luaDev
            ? $"the source writes names its [lua] writes list lacks: {what}; --lua-dev lets them through. The list should read {should}"
            : $"the source writes names its [lua] writes list lacks: {what}; each write fails when it runs, and omp build "
              + $"refuses the mod. The list should read {should}");
    }

    /// <summary>
    /// The events the stack declares (spec §9.2), each once: its fields over the layout of its mod, or of the first base
    /// mod its mod requires, and an event at a point bound as a point hook is. An event declared twice is a hard
    /// conflict. The second set names the events whose declaration failed, so their subscribers report nothing more.
    /// </summary>
    private static (Dictionary<string, DeclaredEvent>, HashSet<string>) Events(
        IReadOnlyList<ModPackage> luaMods, IReadOnlyList<ModPackage> order, ModLinkBase linkBase, OmsPatchPoints? points,
        IReadOnlyDictionary<string, LinkedScript> scripts, ModLinkReport report)
    {
        var declared = new Dictionary<string, DeclaredEvent>(StringComparer.Ordinal);
        var broken = new HashSet<string>(StringComparer.Ordinal);
        foreach (ModPackage mod in luaMods)
        {
            if (mod.Layout.Events.Count == 0) continue;
            void Error(string message) => report.Add(ModReportClass.Error, mod.Id, null, message);
            GameLayout? layout = mod.BaseLayout ?? order
                .Where(package => package.BaseLayout != null
                                  && mod.Manifest.Requires.Any(requirement => requirement.Id == package.Id))
                .Select(package => package.BaseLayout).FirstOrDefault();
            foreach (ModEventFile file in mod.Layout.Events)
            {
                IReadOnlyList<ModEventDeclaration> declarations;
                try
                {
                    declarations = ManifestReader.ParseEvents(mod.ReadText(file.File), $"events/{file.Name}");
                }
                catch (Exception error) when (error is ModManifestException or ModPackageException)
                {
                    Error(error.Message);
                    continue;
                }
                foreach (ModEventDeclaration declaration in declarations)
                {
                    string where = $"events/{file.Name}: {declaration.Name}";
                    if (declared.TryGetValue(declaration.Name, out DeclaredEvent? first))
                    {
                        report.Add(ModReportClass.HardConflict, [first.Event.Owner, mod.Id], null,
                            $"{first.Event.Owner} and {mod.Id} both declare the event {declaration.Name}; an event has one "
                            + "declaring mod");
                        broken.Add(declaration.Name);
                        continue;
                    }
                    var fields = new List<GameEventField>();
                    bool failed = false;
                    foreach (ModEventFieldDeclaration field in declaration.Fields)
                        try
                        {
                            fields.Add(GameEvent.BuildField(field.Name, field.Type, field.Access, field.At,
                                                            !declaration.RaisedByLua, layout));
                        }
                        catch (FormatException error)
                        {
                            Error($"{where}: {error.Message}");
                            failed = true;
                        }
                    (string Script, int Offset, int Site)? place = null;
                    if (declaration.Point is { } point)
                    {
                        if (mod.Manifest.Lua?.HasVm != true)
                        {
                            Error($"{where}: an event at a point reads the frame's cells, which needs the vm capability "
                                  + "([lua] capabilities = [\"vm\"])");
                            failed = true;
                        }
                        else if (BindPoint(mod, where, point, points, linkBase, scripts, report) is { } bound) place = bound;
                        else failed = true;
                    }
                    if (failed)
                    {
                        broken.Add(declaration.Name);
                        continue;
                    }
                    declared[declaration.Name] = new DeclaredEvent(
                        new GameEvent(declaration.Name, mod.Id, declaration.Point, declaration.Description, fields), place);
                }
            }
        }
        foreach (string name in broken) declared.Remove(name);
        return (declared, broken);
    }

    /// <summary>
    /// A mod's <c>[lua] writes</c> (spec §9.3), each entry as the global it names: an exported name or alias of a base mod
    /// the mod requires (<paramref name="bases"/>). A name no such base mod exports is a link error, and so is a list in a
    /// mod that requires no base mod.
    /// </summary>
    private static IReadOnlyList<string> Writes(ModPackage mod, IReadOnlyList<ModPackage> bases, ModLinkReport report)
    {
        IReadOnlyList<string> listed = mod.Manifest.Lua?.Writes ?? [];
        if (listed.Count == 0) return [];
        if (bases.Count == 0)
        {
            report.Add(ModReportClass.Error, mod.Id, null,
                "lua.writes names cells of a base mod, and the mod requires none ([[requires]] id = \"<profile>-base\")");
            return [];
        }
        var writes = new List<string>();
        foreach (string name in listed)
        {
            LayoutGlobal? global = null;
            foreach (ModPackage package in bases)
                if (package.BaseLayout!.TryGet(name, out LayoutGlobal found))
                {
                    global = found;
                    break;
                }
            if (global == null)
                report.Add(ModReportClass.Error, mod.Id, null,
                    $"lua.writes: '{name}' is not a name {string.Join(" or ", bases.Select(package => package.Id))} exports");
            else if (!writes.Contains(global.Name, StringComparer.Ordinal)) writes.Add(global.Name);
        }
        return writes;
    }

    /// <summary>Whether a package takes part in the Lua link: it has Lua files, a <c>[lua]</c> table or hooks.</summary>
    public static bool ShipsLua(ModPackage mod)
        => mod.Layout.Lua.Count > 0 || mod.Manifest.Lua != null || mod.Manifest.Hooks.Count > 0 || mod.Layout.Events.Count > 0;

    /// <summary>A file's module name: <c>util/names.lua</c> is <c>util.names</c> (spec §5.4).</summary>
    public static string ModuleName(string path)
        => (path.EndsWith(".lua", StringComparison.Ordinal) ? path[..^4] : path).Replace('/', '.');

    /// <summary>A point's place (spec §7.6), for a point hook or an event at a point: the point's original offset in
    /// the base revision of its script, then that instruction's offset in the linked script. A replaced script, or an
    /// instruction a patch replaced, fails the link naming the mods.</summary>
    private static (string Script, int Offset, int Site)? BindPoint(ModPackage mod, string where, string name,
                                                                     OmsPatchPoints? points, ModLinkBase linkBase,
                                                                     IReadOnlyDictionary<string, LinkedScript> scripts,
                                                                     ModLinkReport report)
    {
        if (points == null || !points.TryGet(name, out OmsPatchPoint point))
        {
            report.Add(ModReportClass.Error, mod.Id, null, $"{where}: '{name}' is not a named patch point of {linkBase.Profile}");
            return null;
        }
        string script = point.Script.ToUpperInvariant();
        if (linkBase.ReadScript(script) is not { } bytes)
        {
            report.Add(ModReportClass.Error, mod.Id, script, $"{where}: the base catalog has no {script}");
            return null;
        }
        if (points.Resolve(name, script, OmsPatchPlan.FingerprintOf(bytes), out string? reason) is not { } site)
        {
            report.Add(ModReportClass.Error, mod.Id, script, $"{where}: {reason}");
            return null;
        }
        return BindOriginal(mod, where, script, site, scripts, report);
    }

    /// <summary>A site hook's place (spec §6.3, §7.6): the instruction at original offset <c>site</c> of the revision
    /// its fingerprint names, then that instruction's offset in the linked script. The base catalog's script must be
    /// that revision, and the offset the start of one of its instructions. An <c>after</c> hook on an instruction that
    /// waits, calls or only ends gets a note (spec §7.1).</summary>
    private static (string Script, int Offset, int Site)? BindSite(ModPackage mod, string where, ModHook hook,
                                                                    ModLinkBase linkBase,
                                                                    IReadOnlyDictionary<string, LinkedScript> scripts,
                                                                    ModLinkReport report)
    {
        string script = hook.Target.ToUpperInvariant();
        int site = hook.Site!.Value;
        void Error(string message) => report.Add(ModReportClass.Error, mod.Id, script, $"{where}: {message}");
        if (linkBase.ReadScript(script) is not { } bytes)
        {
            Error($"the base catalog has no {script}");
            return null;
        }
        string fingerprint = OmsPatchPlan.FingerprintOf(bytes);
        if (!fingerprint.Equals(hook.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            Error($"the hook was written for {script} {hook.Fingerprint}, but the base catalog's {script} is {fingerprint}");
            return null;
        }
        Ome.Engine.Model.Script code;   // Script alone is the Ome.Script namespace here
        try
        {
            code = Sys4Loader.Parse(bytes, linkBase.Table, script);
        }
        catch (Exception error) when (error is InvalidDataException or Sys4OpcodeDecodeException
                                          or IndexOutOfRangeException or ArgumentException)
        {
            Error($"{script} does not decode as a script: {error.Message}");
            return null;
        }
        if (!code.IndexByOffset.TryGetValue(site, out int index))
        {
            Error($"0x{site:x} is not the start of an instruction of {script}");
            return null;
        }
        if (BindOriginal(mod, where, script, site, scripts, report) is not { } bound) return null;
        if (hook.When == "after") NoteAfter(mod, where, script, code.Instructions[index], linkBase.Table, report);
        return bound;
    }

    /// <summary>Spec §7.1: an <c>after</c> hook fires once its instruction has finished. One that waits, or calls a
    /// script that may wait, can be saved across, and a load of that save rebuilds the session, so the hook does not
    /// fire for it; one that only ends its frame, the scene or the run never finishes.</summary>
    private static void NoteAfter(ModPackage mod, string where, string script, Instruction instruction, OpcodeTable table,
                                  ModLinkReport report)
    {
        if (!table.TryGetDefinition(instruction.Opcode, out OpcodeDefinition definition) || definition.Waits is not { } waits)
            return;
        string what = $"{where}: the instruction at 0x{instruction.Offset:x} (op 0x{instruction.Opcode:x} {definition.CanonicalLabel})";
        if ((definition.Leaves & (OpcodeLeaves.Falls | OpcodeLeaves.Jumps)) == 0)
        {
            report.Add(ModReportClass.Note, mod.Id, script, $"{what} ends its frame, the scene or the run, so the after hook "
                                                            + "never fires; a script exit hook sees a frame's return");
            return;
        }
        var how = new List<string>();
        if (waits != OpcodeWaits.Never) how.Add(waits == OpcodeWaits.Always ? "waits" : "may wait");
        if (definition.Leaves.HasFlag(OpcodeLeaves.Calls)) how.Add("calls a script");
        if (how.Count > 0)
            report.Add(ModReportClass.Note, mod.Id, script, $"{what} {string.Join(" and ", how)}, so the after hook fires "
                                                            + "only once it has finished; a save made before then rebuilds the "
                                                            + "session when it is loaded, and the hook does not fire for that load");
    }

    /// <summary>An original instruction's offset in the linked script (spec §7.6): its own when no mod changed the
    /// script, else through the patches' offset table. A replaced script, or an instruction a patch replaced, fails
    /// the link naming the mods.</summary>
    private static (string Script, int Offset, int Site)? BindOriginal(ModPackage mod, string where, string script, int site,
                                                                        IReadOnlyDictionary<string, LinkedScript> scripts,
                                                                        ModLinkReport report)
    {
        if (!scripts.TryGetValue(script, out LinkedScript? linked)) return (script, site, site);
        if (linked.ReplacedBy is { } replacer)
        {
            report.Add(replacer == mod.Id ? ModReportClass.Error : ModReportClass.HardConflict, [mod.Id, replacer], script,
                $"{where}: {replacer} replaces {script}, and a hook names the original's instruction at 0x{site:x}; a "
                + "replaced script is that mod's code, which a hook does not reach");
            return null;
        }
        if (linked.Offsets?.Map(site) is not { } offset)
        {
            var patchers = linked.PatchedBy.Where(id => id != mod.Id).ToList();
            report.Add(patchers.Count > 0 ? ModReportClass.HardConflict : ModReportClass.Error, [mod.Id, .. linked.PatchedBy],
                script, $"{where}: a patch of {string.Join(", ", linked.PatchedBy)} replaced the instruction at 0x{site:x} "
                        + $"({string.Join(", ", linked.Plans)})");
            return null;
        }
        return (script, offset, site);
    }
}
