using Age.Mods.Packages;

namespace Age.Mods.Linking;

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

        // The key needs the order and the allocation, both cheap to compute without compiling anything.
        var probe = new ModLinkReport();
        IReadOnlyList<ModPackage> order = StackResolver.Resolve(mods, linkBase, userOrder, probe);
        SelectorStore allocation = selectors.Clone();
        IReadOnlyDictionary<string, int> allocated = allocation.Allocate(
            order.Where(mod => mod.Manifest.NeedsCatalog).Select(mod => mod.Id), linkBase.InstallSelectors, probe);
        if (!probe.Stops)
        {
            string key = ModLinkCache.ComputeKey(linkBase, order, allocated);
            if (ModLinkCache.TryOpen(cacheRoot, key) is { } cached)
            {
                allocation.Save(selectorsPath);
                var report = new ModLinkReport { Order = order.Select(mod => mod.Id).ToList() };
                report.Add(ModReportClass.Note, [], null, $"link cache hit {key[..16]}");
                return new ModStackResult(cached, report, true);
            }
        }

        ModLinkResult result = ModLinker.Link(linkBase, mods, userOrder, selectors, skipFailedMods);
        if (!result.Succeeded) return new ModStackResult(null, result.Report, false);
        var kept = result.Catalogs.ToDictionary(catalog => catalog.ModId, catalog => catalog.Selector, StringComparer.Ordinal);
        string linkedKey = ModLinkCache.ComputeKey(linkBase, result.Order, kept);
        MountedModLayer layer = ModLinkCache.Write(cacheRoot, linkedKey, linkBase, result);
        result.Selectors.Save(selectorsPath);
        return new ModStackResult(layer, result.Report, false);
    }
}
