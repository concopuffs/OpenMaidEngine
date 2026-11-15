using Ome.Engine.Localization;
using Ome.Engine.Modding;
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
/// Links the stack's Lua (Lua scripting spec §6.2–§6.4, §7.1, §7.6), after the scripts and the text. Every file is
/// checked through the runtime's <see cref="IModRuntime.Check"/>, which runs no mod code. Every hook is checked
/// against what this engine builds, its handler against the top-level functions of its file, and a point hook is
/// bound through the patch-point registry to the base script's offset and then through the linked script's offset
/// table. One <see cref="HandlerId"/> stands for each (mod, file, function).
/// </summary>
public static class ModLuaLinker
{
    /// <summary>The engine events this engine raises; the others are built later or reserved (spec §7.7).</summary>
    public static readonly IReadOnlySet<string> BuiltEngineEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        "ome.session.start",
    };

    private static readonly IReadOnlyDictionary<string, string> LaterEngineEvents = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ome.session.resumed"] = "comes with mod state (Lua scripting milestone 2)",
        ["ome.save.writing"] = "comes with mod state (Lua scripting milestone 2)",
        ["ome.text.present"] = "is reserved for the localization filter (Lua scripting spec §7.7)",
    };

    /// <summary>The hook forms this engine builds; the reader takes all six (spec §6.3).</summary>
    private static string? NotBuilt(ModHook hook) => hook.Form switch
    {
        ModHookForm.Event => "event hooks come with base mods (Lua scripting milestone 3)",
        ModHookForm.Site => "site hooks come with the wider hooks (Lua scripting milestone 4)",
        ModHookForm.Script => "script entry and exit hooks come with the wider hooks (Lua scripting milestone 4)",
        ModHookForm.Watch => "watches come with the wider hooks (Lua scripting milestone 4)",
        _ when hook.Skip => "skip = true comes with the wider hooks (Lua scripting milestone 4)",
        _ => null,
    };

    private sealed record Pending(ModHook Hook, HandlerId Handler, int LoadOrder);

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
        var pointHooks = new List<(Pending Pending, string Script, int Offset, int Site)>();
        var engineHooks = new List<(Pending Pending, string Event)>();

        foreach (ModPackage mod in luaMods)
        {
            int loadOrder = loadOrders[mod.Id];
            int problemsBefore = report.Lines.Count(line => line.Stops);
            ModLuaSettings? settings = mod.Manifest.Lua;
            bool vm = settings?.HasVm == true;
            void Error(string? script, string message) => report.Add(ModReportClass.Error, mod.Id, script, message);

            if (settings == null) Error(null, "lua/ needs a [lua] table in the manifest");
            if (settings is { Writes.Count: > 0 })
                Error(null, "lua.writes: declared writes come with base mods (Lua scripting milestone 3)");
            if (settings is { Calls.Count: > 0 })
                Error(null, "lua.calls: script calls come with Lua scripting milestone 5");
            if (mod.Layout.Events.Count > 0)
                Error(null, "events/: event declarations come with base mods (Lua scripting milestone 3)");

            // The sources, each checked without running it.
            var requires = mod.Manifest.Requires.Select(requirement => requirement.Id).Where(loadOrders.ContainsKey).ToList();
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
                // A point first: it does not depend on the handler's file, so a broken file does not hide a wrong point.
                (string Script, int Offset, int Site)? bound = null;
                bool pointFails = false;
                if (hook.Form == ModHookForm.Point)
                {
                    if (!vm)
                        Error(null, $"{where}: a point hook hands its handler the frame and operands, which needs the vm "
                                    + "capability ([lua] capabilities = [\"vm\"])");
                    else bound = BindPoint(mod, hook, linkBase, scripts, report);
                    pointFails = bound == null;
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
                var pending = new Pending(hook, id, loadOrder);
                if (hook.Form == ModHookForm.Engine) engineHooks.Add((pending, hook.Target));
                else pointHooks.Add((pending, bound!.Value.Script, bound.Value.Offset, bound.Value.Site));
            }
            if (report.Lines.Count(line => line.Stops) == problemsBefore && sources.Count > 0)
                mods.Add(new ModRuntimeMod(mod.Id, mod.Manifest.Version.ToString(), mod.Manifest.TargetsAnyProfile, vm,
                                           requires, sources));
            // Spec §15: the report shows what each mod may change, so a player can see it.
            if (vm)
                report.Add(ModReportClass.Note, mod.Id, null,
                           "has the vm capability: its Lua reads and writes any cell, the frames' locals and the operands, "
                           + "so it can change any game state and the saves made after it");
        }

        // The merged order (spec §7.4): ascending priority, then load order; one mod's hooks keep manifest order.
        static IEnumerable<Pending> Merged(IEnumerable<Pending> due)
            => due.OrderBy(entry => entry.Hook.Priority).ThenBy(entry => entry.LoadOrder);
        PlannedHandler Planned(Pending entry) => new(entry.Handler, entry.Hook.Priority, entry.LoadOrder, entry.Hook.Skip);
        string Describe(Pending entry) => $"{handlers[entry.Handler.Index]} (priority {entry.Hook.Priority})";

        var sites = new Dictionary<string, IReadOnlyList<PlannedSite>>(StringComparer.Ordinal);
        foreach (var byScript in pointHooks.GroupBy(hook => TextCatalog.NormalizeScriptName(hook.Script), StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var planned = new List<PlannedSite>();
            foreach (var bySite in byScript.GroupBy(hook => hook.Offset).OrderBy(group => group.Key))
            {
                var due = Merged(bySite.Select(hook => hook.Pending)).ToList();
                string? point = bySite.Select(hook => hook.Pending.Hook.Target).FirstOrDefault();
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
        return new LinkedLua(new ModRuntimeStack(mods, handlers), new HookPlan(sites, engine));
    }

    /// <summary>Whether a package takes part in the Lua link: it has Lua files, a <c>[lua]</c> table or hooks.</summary>
    public static bool ShipsLua(ModPackage mod)
        => mod.Layout.Lua.Count > 0 || mod.Manifest.Lua != null || mod.Manifest.Hooks.Count > 0;

    /// <summary>A file's module name: <c>util/names.lua</c> is <c>util.names</c> (spec §5.4).</summary>
    public static string ModuleName(string path)
        => (path.EndsWith(".lua", StringComparison.Ordinal) ? path[..^4] : path).Replace('/', '.');

    /// <summary>A point hook's place (spec §7.6): the point's original offset in the base revision of its script, then
    /// that instruction's offset in the linked script. A replaced script, or an instruction a patch replaced, fails
    /// the link naming the mods.</summary>
    private static (string Script, int Offset, int Site)? BindPoint(ModPackage mod, ModHook hook, ModLinkBase linkBase,
                                                                     IReadOnlyDictionary<string, LinkedScript> scripts,
                                                                     ModLinkReport report)
    {
        string where = hook.Describe();
        if (linkBase.PatchPoints is not { } points || !points.TryGet(hook.Target, out OmsPatchPoint point))
        {
            report.Add(ModReportClass.Error, mod.Id, null, $"{where}: '{hook.Target}' is not a named patch point of {linkBase.Profile}");
            return null;
        }
        string script = point.Script.ToUpperInvariant();
        if (linkBase.ReadScript(script) is not { } bytes)
        {
            report.Add(ModReportClass.Error, mod.Id, script, $"{where}: the base catalog has no {script}");
            return null;
        }
        if (points.Resolve(hook.Target, script, OmsPatchPlan.FingerprintOf(bytes), out string? reason) is not { } site)
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
