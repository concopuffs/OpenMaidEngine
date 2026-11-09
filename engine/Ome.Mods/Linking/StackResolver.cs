using Ome.Mods.Manifest;
using Ome.Mods.Packages;

namespace Ome.Mods.Linking;

/// <summary>
/// Resolves a stack (spec §6.1 step 3, §6.3): profile and revision checks, dependencies and their version ranges,
/// declared conflicts, then the load order. Dependencies and <c>[replace]</c> <c>over</c> entries come before the mods
/// that name them; <c>[order]</c> hints follow; remaining ties go by the user's load-order list, then by mod id.
/// </summary>
public static class StackResolver
{
    public static IReadOnlyList<ModPackage> Resolve(IReadOnlyList<ModPackage> mods, ModLinkBase linkBase,
                                                    IReadOnlyList<string> userOrder, ModLinkReport report)
    {
        var byId = new Dictionary<string, ModPackage>(StringComparer.Ordinal);
        foreach (ModPackage mod in mods)
        {
            if (byId.TryGetValue(mod.Id, out ModPackage? other))
            {
                report.Add(ModReportClass.Error, mod.Id, null,
                    $"two packages share the id {mod.Id}: {other.Location} and {mod.Location}");
                continue;
            }
            byId[mod.Id] = mod;
        }

        foreach (ModPackage mod in byId.Values)
        {
            ModManifest manifest = mod.Manifest;
            if (manifest.Profile != linkBase.Profile)
                report.Add(ModReportClass.Error, mod.Id, null,
                    $"targets profile {manifest.Profile}, but the game is {linkBase.Profile}");
            if (manifest.Revisions.Count > 0
                && !manifest.Revisions.Contains(linkBase.CatalogFingerprint, StringComparer.OrdinalIgnoreCase))
                report.Add(ModReportClass.Error, mod.Id, null,
                    $"supports SYS4INI.BIN {string.Join(", ", manifest.Revisions)}; the installed catalog is "
                    + linkBase.CatalogFingerprint);
            foreach (ModRequirement requirement in manifest.Requires)
            {
                if (!byId.TryGetValue(requirement.Id, out ModPackage? dependency))
                    report.Add(ModReportClass.Error, mod.Id, null, $"requires {requirement.Id}, which is not installed or enabled");
                else if (!requirement.Version.Contains(dependency.Manifest.Version))
                    report.Add(ModReportClass.Error, [mod.Id, dependency.Id], null,
                        $"requires {requirement.Id} {requirement.Version}, but {dependency.Manifest.Version} is installed");
            }
            foreach (ModConflict conflict in manifest.Conflicts.Where(conflict => byId.ContainsKey(conflict.Id)))
                report.Add(ModReportClass.HardConflict, [mod.Id, conflict.Id], null,
                    $"declares a conflict with {conflict.Id}" + (conflict.Reason is { } reason ? $": {reason}" : ""));
        }

        // Edges run from the mod that loads first to the mod that loads after it.
        var edges = byId.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        void Edge(string first, string then)
        {
            if (byId.ContainsKey(first) && byId.ContainsKey(then) && first != then) edges[first].Add(then);
        }
        foreach (ModPackage mod in byId.Values)
        {
            foreach (ModRequirement requirement in mod.Manifest.Requires) Edge(requirement.Id, mod.Id);
            foreach (ModReplacementOrder replacement in mod.Manifest.ReplacementOrders) Edge(replacement.Over, mod.Id);
            foreach (string after in mod.Manifest.After) Edge(after, mod.Id);
            foreach (string before in mod.Manifest.Before) Edge(mod.Id, before);
        }

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int k = 0; k < userOrder.Count; k++) rank.TryAdd(userOrder[k], k);
        int Rank(string id) => rank.TryGetValue(id, out int value) ? value : int.MaxValue;
        var ready = new SortedSet<(int Rank, string Id)>(Comparer<(int Rank, string Id)>.Create((a, b) =>
            a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : string.CompareOrdinal(a.Id, b.Id)));
        var incoming = byId.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (string target in edges.Values.SelectMany(targets => targets)) incoming[target]++;
        foreach (var (id, count) in incoming.Where(pair => pair.Value == 0)) ready.Add((Rank(id), id));

        var order = new List<ModPackage>();
        while (ready.Count > 0)
        {
            var next = ready.Min;
            ready.Remove(next);
            order.Add(byId[next.Id]);
            foreach (string target in edges[next.Id])
                if (--incoming[target] == 0) ready.Add((Rank(target), target));
        }

        if (order.Count < byId.Count)
        {
            var remaining = byId.Keys.Where(id => incoming[id] > 0).ToHashSet(StringComparer.Ordinal);
            List<string> cycle = FindCycle(remaining, edges);
            report.Add(ModReportClass.Error, cycle, null,
                $"load-order cycle: {string.Join(" -> ", cycle.Append(cycle[0]))} (requirements, [order] hints and "
                + "[replace] over entries)");
            order.AddRange(remaining.Order(StringComparer.Ordinal).Select(id => byId[id]));
        }
        report.Order = order.Select(mod => mod.Id).ToList();
        return order;
    }

    private static List<string> FindCycle(HashSet<string> remaining, Dictionary<string, HashSet<string>> edges)
    {
        // Every remaining node still has a remaining predecessor, so walking backward must revisit one; that loop,
        // reversed, is a cycle in load order.
        string current = remaining.Order(StringComparer.Ordinal).First();
        var path = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        while (!seen.ContainsKey(current))
        {
            seen[current] = path.Count;
            path.Add(current);
            string node = current;
            current = remaining.Where(candidate => edges[candidate].Contains(node)).Order(StringComparer.Ordinal).First();
        }
        List<string> cycle = path.Skip(seen[current]).ToList();
        cycle.Reverse();
        // Start at the smallest id, so the report does not depend on where the walk began.
        int first = cycle.IndexOf(cycle.Min(StringComparer.Ordinal)!);
        return cycle.Skip(first).Concat(cycle.Take(first)).ToList();
    }
}
