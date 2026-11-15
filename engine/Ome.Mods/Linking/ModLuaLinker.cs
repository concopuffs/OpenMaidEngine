using Ome.Engine.Localization;
using Ome.Engine.Modding;
using Ome.Engine.Natives;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;
using Ome.Script.Patch;

namespace Ome.Mods.Linking;

/// <summary>The Lua part of a link (Lua scripting spec §6, §7): the mods that ship Lua with their sources, every
/// handler their hooks name, and the hook plan bound to the linked scripts.</summary>
public sealed record LinkedLua(ModRuntimeStack Stack, HookPlan Plan)
{
    public static readonly LinkedLua None = new(ModRuntimeStack.Empty, HookPlan.Empty);
}

/// <summary>
/// Links the stack's Lua (Lua scripting spec §6.2–§6.4, §7.1, §7.6, §9.2), after the scripts and the text. Every file
/// is checked through the runtime's <see cref="IModRuntime.Check"/>, which runs no mod code. Every hook is checked
/// against what this engine builds, its handler against the top-level functions of its file, and a point hook is
/// bound through the patch points to the base script's offset and then through the linked script's offset table.
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
        ModHookForm.Site => "site hooks come with the wider hooks (Lua scripting milestone 4)",
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
        var pointHooks = new List<(Pending Pending, string Script, int Offset, int Site, string Point)>();
        var engineHooks = new List<(Pending Pending, string Event)>();
        var luaEventHooks = new List<(Pending Pending, string Event)>();
        // Spec §9.1, plan §7.1: the profile's patch points, else the copy a base mod ships.
        OmsPatchPoints? points = linkBase.PatchPoints ?? order.Select(mod => mod.BasePoints).FirstOrDefault(found => found != null);
        var (declared, brokenEvents) = Events(luaMods, order, linkBase, points, scripts, report);

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
            IReadOnlyList<string> writes = Writes(mod, order, requires, report);
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
                foreach (string written in check.WrittenNames.Distinct(StringComparer.Ordinal))
                    report.Add(ModReportClass.Note, mod.Id, null,
                        $"lua/{file.Path} writes {written}; declared writes come with base mods (Lua scripting milestone 3)");
                string module = ModuleName(file.Path);
                if (requires.Contains(module, StringComparer.Ordinal))
                    Error(null, $"lua/{file.Path} is module '{module}', which is also the id of a mod it requires, so "
                                + $"require(\"{module}\") would name both; rename the file");
                sources.Add(source);
                checks[file.Path] = check;
            }

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
                                     hook.Target));
            }
            // A base mod is in the session with no Lua of its own: the accessors of the mods that require it read its
            // layout (spec §9.1, §9.3).
            if (report.Lines.Count(line => line.Stops) == problemsBefore && (sources.Count > 0 || mod.BaseLayout != null))
                mods.Add(new ModRuntimeMod(mod.Id, mod.Manifest.Version.ToString(), mod.Manifest.TargetsAnyProfile, vm,
                                           requires, sources, mod.BaseLayout, writes));
            // Spec §15: the report shows what each mod may change, so a player can see it.
            if (vm)
                report.Add(ModReportClass.Note, mod.Id, null,
                           "has the vm capability: its Lua reads and writes any cell, the frames' locals and the operands, "
                           + "so it can change any game state and the saves made after it");
        }

        // The merged order (spec §7.4): ascending priority, then load order; one mod's hooks keep manifest order.
        static IEnumerable<Pending> Merged(IEnumerable<Pending> due)
            => due.OrderBy(entry => entry.Hook.Priority).ThenBy(entry => entry.LoadOrder);
        PlannedHandler Planned(Pending entry)
            => new(entry.Handler, entry.Hook.Priority, entry.LoadOrder, entry.Hook.Skip, GameEvent: entry.GameEvent);
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
                var due = Merged(bySite.Select(hook => hook.Pending)).ToList();
                string? point = bySite.Select(hook => hook.Point).FirstOrDefault();
                planned.Add(new PlannedSite(bySite.Key, bySite.First().Site, point, due.Select(Planned).ToList()));
                if (due.Count > 1)
                    report.Add(ModReportClass.OrderedInteraction, due.Select(entry => handlers[entry.Handler.Index].ModId),
                        byScript.First().Script, $"0x{bySite.First().Site:x} ({point}): handlers run in this order: "
                                                 + string.Join(", ", due.Select(Describe)));
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
        var events = declared.Values.Select(entry => entry.Event).OrderBy(gameEvent => gameEvent.Name, StringComparer.Ordinal).ToList();
        return new LinkedLua(new ModRuntimeStack(mods, handlers, events), new HookPlan(sites, engine, luaEvents));
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
    /// the mod requires. A name no such base mod exports is a link error, and so is a list in a mod that requires no base
    /// mod.
    /// </summary>
    private static IReadOnlyList<string> Writes(ModPackage mod, IReadOnlyList<ModPackage> order, IReadOnlyList<string> requires,
                                                ModLinkReport report)
    {
        IReadOnlyList<string> listed = mod.Manifest.Lua?.Writes ?? [];
        if (listed.Count == 0) return [];
        var bases = order.Where(package => package.BaseLayout != null && requires.Contains(package.Id, StringComparer.Ordinal))
            .ToList();
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
