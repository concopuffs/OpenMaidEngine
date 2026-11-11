using Ome.Mods.Manifest;
using Ome.Mods.Packages;
using Ome.Script.Compile;
using Ome.Script.Patch;
using Ome.Script.Syntax;

namespace Ome.Mods.Linking;

/// <summary>A catalog script the stack changed: its linked bytes, the mod whose replacement it starts from (null for
/// the base), and the mods whose patches it carries, in load order.</summary>
public sealed record LinkedScript(string Name, byte[] Bytes, string? ReplacedBy, IReadOnlyList<string> PatchedBy)
{
    /// <summary>The plans applied, in order, as <c>mod.id (patches/stem)</c>.</summary>
    public IReadOnlyList<string> Plans { get; init; } = [];

    /// <summary>Where the base instructions went, for a patched script that no mod replaces (localization spec
    /// §6.1); null otherwise.</summary>
    public ScriptOffsetMap? Offsets { get; init; }
}

/// <summary>An <c>assets/</c> file that overrides the base asset of its basename; the last mod in load order wins.</summary>
public sealed record LinkedAsset(string Name, string ModId, byte[] Bytes);

/// <summary>The outcome of a link: the resolved order, the changed scripts, the mods' append catalogs, the asset
/// overrides, the text (catalogs, fonts and languages), the selector allocation after the link, and the report. When
/// the report stops the link, the content holds what linked cleanly but must not be mounted, and the allocation must
/// not be saved.</summary>
public sealed record ModLinkResult(
    IReadOnlyList<ModPackage> Order,
    IReadOnlyDictionary<string, LinkedScript> Scripts,
    ModLinkReport Report)
{
    public bool Succeeded => !Report.Stops;
    public IReadOnlyList<LinkedCatalog> Catalogs { get; init; } = [];
    public IReadOnlyDictionary<string, LinkedAsset> Assets { get; init; } = new Dictionary<string, LinkedAsset>();
    /// <summary>The language assets (localization spec §12), by catalog name: every package with a file for the name,
    /// in load order.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<LinkedLanguageAssetLayer>> LanguageAssets { get; init; }
        = new Dictionary<string, IReadOnlyList<LinkedLanguageAssetLayer>>();
    public LinkedText Text { get; init; } = LinkedText.None;
    public SelectorStore Selectors { get; init; } = new();
}

/// <summary>
/// Links a stack against a base (spec §6.1 steps 3–8): resolves the order, picks each script's base or highest
/// replacement, applies every plan in load order (stored plans when their fingerprint matches, otherwise the
/// fragment compiled now), and reports interactions, overrides and failures.
/// </summary>
public static class ModLinker
{
    /// <param name="selectors">The kept allocation (<c>mods/selectors.toml</c>). It is not changed: the result carries
    /// the allocation after the link, for the caller to save when the link succeeds. Null starts from none.</param>
    /// <param name="skipFailedMods">Drop every mod a stopping line names and link the rest again (spec §7).</param>
    public static ModLinkResult Link(ModLinkBase linkBase, IReadOnlyList<ModPackage> mods, IReadOnlyList<string> userOrder,
                                     SelectorStore? selectors = null, bool skipFailedMods = false)
    {
        var dropped = new List<string>();
        var remaining = mods.ToList();
        while (true)
        {
            ModLinkResult result = LinkOnce(linkBase, remaining, userOrder, (selectors ?? new SelectorStore()).Clone());
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
                                          IReadOnlyList<string> userOrder, SelectorStore selectors)
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
        // Selectors and record layouts (spec §6.2), so every reference resolves before anything is compiled.
        IReadOnlyDictionary<string, int> allocated = selectors.Allocate(
            order.Where(mod => mod.Manifest.NeedsCatalog).Select(mod => mod.Id), linkBase.InstallSelectors, report);
        var layouts = order.Where(mod => allocated.ContainsKey(mod.Id))
            .ToDictionary(mod => mod.Id, ModCatalogBuilder.Layout, StringComparer.Ordinal);
        uint ResolveResource(string reference)
        {
            int colon = reference.LastIndexOf(':');
            string modId = colon < 0 ? "" : reference[..colon], name = reference[(colon + 1)..];
            if (!layouts.TryGetValue(modId, out var layout))
                throw new InvalidDataException($"'{reference}' names a mod that has no append catalog in this stack");
            int record = layout.ToList().FindIndex(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (record < 0) throw new InvalidDataException($"'{reference}': {modId} has no script or asset {name}");
            return (uint)(allocated[modId] << 24) | (uint)record;
        }
        var catalogs = new List<LinkedCatalog>();
        foreach (ModPackage mod in order.Where(mod => allocated.ContainsKey(mod.Id)))
            if (ModCatalogBuilder.Build(mod, allocated[mod.Id], ScopeOf(mod, byId), linkBase, ResolveResource, report)
                is { } catalog)
                catalogs.Add(catalog);

        // Asset overrides: an unlisted assets/ file names a base asset; later mods win (spec §4.4).
        var assets = new Dictionary<string, LinkedAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (ModPackage mod in order)
            foreach (ModAssetFile file in mod.Layout.Assets)
            {
                bool inBase = linkBase.HasAsset(file.Name);
                if (file.ListedAsNew)
                {
                    if (inBase)
                        report.Add(ModReportClass.Error, mod.Id, file.Name,
                            "[assets].names lists it as a new asset, but the base has an asset of that name; drop it "
                            + "from the names to override the base asset");
                    continue;
                }
                if (!inBase)
                {
                    report.Add(ModReportClass.Error, mod.Id, file.Name,
                        "assets/ holds it, but the base has no asset of that name; a new asset must be listed in "
                        + "[assets].names");
                    continue;
                }
                if (file.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase))
                {
                    report.Add(ModReportClass.Error, mod.Id, file.Name, "a whole-script replacement goes in replace/, not assets/");
                    continue;
                }
                if (assets.TryGetValue(file.Name, out LinkedAsset? earlier))
                    report.Add(ModReportClass.Override, [earlier.ModId, mod.Id], file.Name,
                        $"{mod.Id}'s asset overrides {earlier.ModId}'s (later in load order)");
                assets[file.Name] = new LinkedAsset(file.Name, mod.Id, mod.ReadAll(file.File));
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
            if (ApplyAll(script, list, Chosen(script)!, linkBase, ResolveResource, report) is { } patched)
            {
                string? replacedBy = replacements.TryGetValue(script, out var winner) ? winner.Mod.Id : null;
                scripts[script] = new LinkedScript(script, patched.Bytes, replacedBy,
                    list.Select(use => use.Mod.Id).Distinct().ToList())
                {
                    Plans = list.Select(use => use.Label).ToList(),
                    Offsets = replacedBy == null ? ScriptOffsetMap.From(patched) : null,
                };
            }
        LinkedText text = ModTextLinker.Link(order, linkBase, scripts, report);
        var languageAssets = ModLanguageAssetLinker.Link(order, linkBase, allocated, report);
        return new ModLinkResult(order, scripts, report)
        {
            Catalogs = catalogs, Assets = assets, LanguageAssets = languageAssets, Text = text, Selectors = selectors,
        };
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
            catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException
                                              or ModPackageException)
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
            catch (Exception error) when (error is OmsSyntaxException or ModPackageException)
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

    private static OmsPatchResult? ApplyAll(string script, List<PatchUse> uses, byte[] baseBytes, ModLinkBase linkBase,
                                            Func<string, uint> resolveResource, ModLinkReport report)
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
                plans.Add(use.Plan.ResolveResources(resolveResource));
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
            return result;
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
