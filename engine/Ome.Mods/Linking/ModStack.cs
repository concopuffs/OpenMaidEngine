using Ome.Mods.Packages;

namespace Ome.Mods.Linking;

/// <summary>The outcome of preparing a stack: the mounted layer when it linked (or came from the cache), and the
/// report either way.</summary>
public sealed record ModStackResult(MountedModLayer? Layer, ModLinkReport Report, bool FromCache)
{
    public bool Succeeded => Layer != null;
}

/// <summary>
/// The loader's transaction (spec §6.1): resolve the stack and its selectors, reuse the cache entry of the resulting
/// key or link and write one, and only then save the selector allocation. Nothing is written when the link fails.
/// </summary>
public static class ModStack
{
    /// <param name="modsDirectory">The profile's <c>mods/</c> folder: it holds <c>selectors.toml</c> and <c>.cache/</c>.</param>
    public static ModStackResult Prepare(ModLinkBase linkBase, IReadOnlyList<ModPackage> mods, IReadOnlyList<string> userOrder,
                                         string modsDirectory, bool skipFailedMods = false)
    {
        string selectorsPath = Path.Combine(modsDirectory, SelectorStore.FileName);
        string cacheRoot = Path.Combine(modsDirectory, ModLinkCache.FolderName);
        SelectorStore selectors = SelectorStore.Load(selectorsPath);

        // A link that skipped failed mods is cached under a key of the launch's inputs (the installed mods, the user's
        // order and the saved selectors), marked, so the same launch with the same flag hits it instead of linking
        // again, even when a mod fails before an order exists; a launch that does not skip failures never mounts it.
        if (skipFailedMods && ModLinkCache.TryOpen(cacheRoot, SkipFailedKey(linkBase, mods, userOrder, selectors)) is { } skipped)
        {
            var report = new ModLinkReport { Order = skipped.Manifest.Order.Select(entry => entry.Id).ToList() };
            report.Add(ModReportClass.Note, [], null,
                $"link cache hit {skipped.Key[..16]} (failed mods skipped; see the cached report)");
            return new ModStackResult(skipped, report, true);
        }

        // The key needs the order and the allocation, both cheap to compute without compiling anything.
        var probe = new ModLinkReport();
        IReadOnlyList<ModPackage> order = StackResolver.Resolve(mods, linkBase, userOrder, probe);
        SelectorStore allocation = selectors.Clone();
        IReadOnlyDictionary<string, int> allocated = allocation.Allocate(
            order.Where(mod => mod.Manifest.NeedsCatalog).Select(mod => mod.Id), linkBase.InstallSelectors, probe);
        if (!probe.Stops)
        {
            string stackKey = ModLinkCache.ComputeKey(linkBase, order, allocated);
            if (ModLinkCache.TryOpen(cacheRoot, stackKey) is { } cached)
            {
                allocation.Save(selectorsPath);
                var report = new ModLinkReport { Order = order.Select(mod => mod.Id).ToList() };
                report.Add(ModReportClass.Note, [], null, $"link cache hit {stackKey[..16]}");
                return new ModStackResult(cached, report, true);
            }
        }

        ModLinkResult result = ModLinker.Link(linkBase, mods, userOrder, selectors, skipFailedMods);
        if (!result.Succeeded) return new ModStackResult(null, result.Report, false);
        var kept = result.Catalogs.ToDictionary(catalog => catalog.ModId, catalog => catalog.Selector, StringComparer.Ordinal);
        // A dropped mod's selector is not kept, so the key uses the allocation saved below: the one the next launch loads.
        string linkedKey = skipFailedMods && result.Report.Dropped.Count > 0
            ? SkipFailedKey(linkBase, mods, userOrder, result.Selectors)
            : ModLinkCache.ComputeKey(linkBase, result.Order, kept);
        MountedModLayer layer = ModLinkCache.Write(cacheRoot, linkedKey, linkBase, result);
        result.Selectors.Save(selectorsPath);
        return new ModStackResult(layer, result.Report, false);
    }

    /// <summary>The cache key of a link that dropped its failed mods: every installed mod by id, the user's order and
    /// the saved selectors, which together decide the order, the allocation and which mods fail.</summary>
    private static string SkipFailedKey(ModLinkBase linkBase, IReadOnlyList<ModPackage> mods, IReadOnlyList<string> userOrder,
                                        SelectorStore selectors)
    {
        var installed = mods.OrderBy(mod => mod.Id, StringComparer.Ordinal).ToList();
        string text = "skip-failed-mods\n" + ModLinkCache.ComputeKey(linkBase, installed, selectors.Selectors) + "\n"
                      + string.Concat(userOrder.Select(line => "order " + line + "\n"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
