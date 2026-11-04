using Age.Mods.Manifest;
using Age.Mods.Packages;
using Age.Script.Compile;
using Age.Script.Patch;
using Age.Script.Syntax;

namespace Age.Mods.Linking;

/// <summary>A catalog script the stack changed: its linked bytes, the mod whose replacement it starts from (null for
/// the base), and the mods whose patches it carries, in load order.</summary>
public sealed record LinkedScript(string Name, byte[] Bytes, string? ReplacedBy, IReadOnlyList<string> PatchedBy);

/// <summary>The outcome of a link: the resolved order, the changed scripts, and the report. When the report stops
/// the link, <see cref="Scripts"/> holds what linked cleanly but must not be mounted.</summary>
public sealed record ModLinkResult(
    IReadOnlyList<ModPackage> Order,
    IReadOnlyDictionary<string, LinkedScript> Scripts,
    ModLinkReport Report)
{
    public bool Succeeded => !Report.Stops;
}

/// <summary>
/// Links a stack against a base (spec §6.1 steps 3–8): resolves the order, picks each script's base or highest
/// replacement, applies every plan in load order (stored plans when their fingerprint matches, otherwise the
/// fragment compiled now), and reports interactions, overrides and failures.
/// </summary>
public static class ModLinker
{
    /// <param name="resolveResource">Binds <c>mod.id:NAME</c> references to packed ids once selectors are allocated;
    /// null fails any plan that holds one.</param>
    /// <param name="skipFailedMods">Drop every mod a stopping line names and link the rest again (spec §7).</param>
    public static ModLinkResult Link(ModLinkBase linkBase, IReadOnlyList<ModPackage> mods, IReadOnlyList<string> userOrder,
                                     Func<string, uint>? resolveResource = null, bool skipFailedMods = false)
    {
        var dropped = new List<string>();
        var remaining = mods.ToList();
        while (true)
        {
            ModLinkResult result = LinkOnce(linkBase, remaining, userOrder, resolveResource);
            if (!skipFailedMods || result.Succeeded || result.Report.FailedMods.Count == 0)
            {
                result.Report.Dropped = dropped;
                return result;
            }
            var failed = result.Report.FailedMods.ToHashSet(StringComparer.Ordinal);
            dropped.AddRange(failed.Order(StringComparer.Ordinal));
            remaining = remaining.Where(mod => !failed.Contains(mod.Id)).ToList();
        }
    }

    private sealed record PatchUse(ModPackage Mod, string Stem, OmsPatchPlan Plan)
    {
        public string Label => $"{Mod.Id} (patches/{Stem})";
    }

    private static ModLinkResult LinkOnce(ModLinkBase linkBase, IReadOnlyList<ModPackage> mods,
                                          IReadOnlyList<string> userOrder, Func<string, uint>? resolveResource)
    {
        var report = new ModLinkReport();
        IReadOnlyList<ModPackage> order = StackResolver.Resolve(mods, linkBase, userOrder, report);
        var byId = order.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        foreach (ModPackage mod in order)
            foreach (string note in mod.Layout.Notes)
                report.Add(ModReportClass.Note, mod.Id, null, note);

        var baseCache = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        byte[]? BaseScript(string name)
        {
            if (!baseCache.TryGetValue(name, out byte[]? bytes)) baseCache[name] = bytes = linkBase.ReadScript(name);
            return bytes;
        }

        // Replacements: the last in load order wins; each later one must name the one it supersedes (spec §4.2).
        var replacements = new Dictionary<string, (ModPackage Mod, byte[] Bytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (ModPackage mod in order)
            foreach (ModReplacementFile file in mod.Layout.Replacements)
            {
                string script = file.Script.ToUpperInvariant();
                if (BaseScript(script) == null)
                {
                    report.Add(ModReportClass.Error, mod.Id, script,
                        "replace/ holds a script the base catalog does not have; new scripts go in scripts/");
                    continue;
                }
                byte[] bytes = mod.ReadAll(file.File);
                if (replacements.TryGetValue(script, out var earlier))
                {
                    bool ordered = mod.Manifest.ReplacementOrders.Any(entry =>
                        entry.Script.Equals(script, StringComparison.OrdinalIgnoreCase) && entry.Over == earlier.Mod.Id);
                    if (ordered)
                        report.Add(ModReportClass.Override, [earlier.Mod.Id, mod.Id], script,
                            $"{mod.Id}'s replacement supersedes {earlier.Mod.Id}'s ([replace] over)");
                    else
                        report.Add(ModReportClass.HardConflict, [earlier.Mod.Id, mod.Id], script,
                            $"{earlier.Mod.Id} and {mod.Id} both replace it; the later one may name the earlier in "
                            + $"[replace] \"{script}\" = {{ over = \"{earlier.Mod.Id}\" }}");
                }
                replacements[script] = (mod, bytes);
            }
        byte[]? Chosen(string script) => replacements.TryGetValue(script, out var winner) ? winner.Bytes : BaseScript(script);
        string ReplacedNote(string script)
            => replacements.TryGetValue(script, out var winner) ? $" ({script} is replaced by {winner.Mod.Id})" : "";

        // Plans per script, in load order.
        var uses = new Dictionary<string, List<PatchUse>>(StringComparer.OrdinalIgnoreCase);
        foreach (ModPackage mod in order)
        {
            OmsResourceScope scope = ScopeOf(mod, byId);
            foreach (ModPatchFiles patch in mod.Layout.Patches)
                if (PlanFor(mod, patch, scope, linkBase, Chosen, ReplacedNote, report) is { } use)
                {
                    if (!uses.TryGetValue(use.Plan.Script, out var list)) uses[use.Plan.Script] = list = new();
                    list.Add(use);
                }
        }

        var scripts = new Dictionary<string, LinkedScript>(StringComparer.OrdinalIgnoreCase);
        foreach (var (script, list) in replacements.Where(pair => !uses.ContainsKey(pair.Key)))
            scripts[script] = new LinkedScript(script, list.Bytes, list.Mod.Id, []);
        foreach (var (script, list) in uses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            if (ApplyAll(script, list, Chosen(script)!, linkBase, resolveResource, report) is { } bytes)
                scripts[script] = new LinkedScript(script, bytes,
                    replacements.TryGetValue(script, out var winner) ? winner.Mod.Id : null,
                    list.Select(use => use.Mod.Id).Distinct().ToList());
        return new ModLinkResult(order, scripts, report);
    }

    /// <summary>The names a mod's code may reach: its own, and those of the mods it requires.</summary>
    internal static OmsResourceScope ScopeOf(ModPackage mod, IReadOnlyDictionary<string, ModPackage> byId)
    {
        var scope = new OmsResourceScope(mod.Id, mod.Manifest.ScriptNames, mod.Manifest.AssetNames);
        foreach (ModRequirement requirement in mod.Manifest.Requires)
            if (byId.TryGetValue(requirement.Id, out ModPackage? dependency))
                scope.WithDependency(dependency.Id, dependency.Manifest.ScriptNames, dependency.Manifest.AssetNames);
        return scope;
    }

    private static PatchUse? PlanFor(ModPackage mod, ModPatchFiles patch, OmsResourceScope scope, ModLinkBase linkBase,
                                     Func<string, byte[]?> chosen, Func<string, string> replacedNote, ModLinkReport report)
    {
        OmsPatchPlan? stored = null;
        ScriptUnit? unit = null;
        if (patch.Plan is { } planEntry)
        {
            try
            {
                stored = OmsPatchPlan.FromJson(mod.ReadText(planEntry));
            }
            catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException)
            {
                report.Add(ModReportClass.Error, mod.Id, null, $"{planEntry.Path}: not a readable transform plan: {error.Message}");
                return null;
            }
        }
        if (patch.Source is { } sourceEntry)
        {
            try
            {
                unit = Parser.ParseScript(mod.ReadText(sourceEntry), $"{mod.Id}/{sourceEntry.Path}");
            }
            catch (OmsSyntaxException error)
            {
                report.Add(ModReportClass.Error, mod.Id, null, error.Message);
                return null;
            }
            if (!unit.IsPatch)
            {
                report.Add(ModReportClass.Error, mod.Id, null,
                    $"{sourceEntry.Path} is not a patch unit (patch \"SCRIPT.BIN\" fingerprint ...)");
                return null;
            }
        }
        string target = (stored?.Script ?? unit!.Name).ToUpperInvariant();
        if (stored != null && unit != null && !stored.Script.Equals(unit.Name, StringComparison.OrdinalIgnoreCase))
        {
            report.Add(ModReportClass.Error, mod.Id, target,
                $"patches/{patch.Stem}: the stored plan targets {stored.Script} but the fragment targets {unit.Name}");
            return null;
        }
        if (chosen(target) is not { } bytes)
        {
            report.Add(ModReportClass.Error, mod.Id, target, $"patches/{patch.Stem} targets a script the base catalog does not have");
            return null;
        }
        string fingerprint = OmsPatchPlan.FingerprintOf(bytes);
        if (stored != null && stored.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase))
            return new PatchUse(mod, patch.Stem, stored);
        if (unit == null)
        {
            report.Add(ModReportClass.Error, mod.Id, target,
                $"patches/{patch.Stem}: the plan was compiled against {stored!.Fingerprint}, but {target} is {fingerprint}"
                + replacedNote(target));
            return null;
        }
        try
        {
            var warnings = new List<OmsWarning>();
            var notes = new List<string>();
            OmsPatchPlan plan = OmsPatchCompiler.Compile(unit, bytes, linkBase.Table, linkBase.Contracts, linkBase.Intrinsics,
                linkBase.Modules, linkBase.Idioms, warnings, notes, linkBase.PatchPoints, scope);
            report.Add(ModReportClass.Note, mod.Id, target, $"compiled patches/{patch.Stem}.oms at load time ("
                + (stored == null ? "no stored plan" : $"its stored plan targets {stored.Fingerprint}") + ")");
            foreach (OmsWarning warning in warnings) report.Add(ModReportClass.Note, mod.Id, target, warning.ToString());
            foreach (string note in notes) report.Add(ModReportClass.Note, mod.Id, target, note);
            return new PatchUse(mod, patch.Stem, plan);
        }
        catch (Exception error) when (error is OmsCompileException or OmsPatchException or InvalidDataException)
        {
            report.Add(ModReportClass.Error, mod.Id, target, error.Message + replacedNote(target));
            return null;
        }
    }

    private static byte[]? ApplyAll(string script, List<PatchUse> uses, byte[] baseBytes, ModLinkBase linkBase,
                                    Func<string, uint>? resolveResource, ModLinkReport report)
    {
        var plans = new List<OmsPatchPlan>();
        foreach (PatchUse use in uses)
        {
            if (use.Plan.Resources.Count == 0)
            {
                plans.Add(use.Plan);
                continue;
            }
            try
            {
                plans.Add(use.Plan.ResolveResources(reference => resolveResource?.Invoke(reference)
                    ?? throw new InvalidDataException($"'{reference}' has no packed id: the stack has no selector for it")));
            }
            catch (Exception error) when (error is InvalidDataException or KeyNotFoundException)
            {
                report.Add(ModReportClass.Error, use.Mod.Id, script, $"{use.Label}: {error.Message}");
                return null;
            }
        }
        var labels = uses.Select(use => use.Label).ToList();
        List<string> Involved(string text)
        {
            var named = uses.Where(use => text.Contains(use.Label, StringComparison.Ordinal)).Select(use => use.Mod.Id)
                .Distinct().ToList();
            return named.Count > 0 ? named : uses.Select(use => use.Mod.Id).Distinct().ToList();
        }
        try
        {
            OmsPatchResult result = OmsPatchApplier.Apply(baseBytes, script, plans, linkBase.Table, linkBase.Contracts, labels);
            foreach (string interaction in result.Interactions)
                report.Add(ModReportClass.OrderedInteraction, Involved(interaction), script, interaction);
            foreach (string note in result.Notes)
                report.Add(ModReportClass.Note, uses.Select(use => use.Mod.Id), script, note);
            return result.Bytes;
        }
        catch (OmsPatchException error)
        {
            foreach (string line in error.Message.Split(Environment.NewLine))
            {
                List<string> involved = Involved(line);
                report.Add(involved.Count > 1 ? ModReportClass.HardConflict : ModReportClass.Error, involved, script, line);
            }
            return null;
        }
    }
}
