using Ome.Mods.Linking;
using Ome.Mods.Packages;

namespace Ome.Mods.Launch;

/// <summary>
/// A link for inspection (launcher spec §5.9): the discovery and the link the engine would make for a profile root,
/// writing nothing. No cache and no selector is written, and no diagnostics: a launcher that links on every switch of
/// its Mods page must not hand a permanent selector to a package the player only tried. The root's kept selectors are
/// used. <c>omp check</c>, <c>explain</c>, <c>export</c>, <c>lua-stubs</c> and <c>text</c> link through it, and so
/// does the launcher, so they share one code path. The caller disposes it, which closes the packages.
/// </summary>
public sealed class ModInspection : IDisposable
{
    private ModInspection(ModLinkReport discovery, ModLinkResult? link, IReadOnlyList<string> userOrder,
                          IReadOnlyList<ModPackage> packages)
    {
        Discovery = discovery;
        Link = link;
        UserOrder = userOrder;
        Packages = packages;
        Moves = link == null ? [] : StackResolver.Moves(link.Report.Order, userOrder, link.Report.OrderEdges);
    }

    /// <summary>The discovery's notes and errors, which the engine prints to its console at a start: the base mod's
    /// mount among them.</summary>
    public ModLinkReport Discovery { get; }

    /// <summary>The link; null when a package did not open and failures are not skipped.</summary>
    public ModLinkResult? Link { get; }

    /// <summary>The player's order the link ranked the packages by.</summary>
    public IReadOnlyList<string> UserOrder { get; }

    /// <summary>The resolved load order.</summary>
    public IReadOnlyList<string> Order => Link?.Report.Order ?? [];

    /// <summary>The packages the resolver moved from <see cref="UserOrder"/>, each with the edges that moved it.</summary>
    public IReadOnlyList<OrderMove> Moves { get; }

    /// <summary>Every package the inspection opened.</summary>
    public IReadOnlyList<ModPackage> Packages { get; }

    /// <param name="profileRoot">The data root whose <c>mods/</c> (or <paramref name="table"/>) and kept selectors are
    /// linked; null for the extra packages alone.</param>
    /// <param name="table">The profile file's <c>[mods]</c> table when the root has one (launcher spec §5.3): it names
    /// the packages in place of the root's <c>mods/</c> folder.</param>
    public static ModInspection Inspect(ModLinkBase linkBase, string? profileRoot, IReadOnlyList<string> extraPackages,
                                        bool skipFailed, ProfileMods? table = null)
    {
        var report = new ModLinkReport();
        string modsDirectory = profileRoot == null
            ? Path.Combine(Path.GetTempPath(), "omp-none-" + Guid.NewGuid().ToString("N"))
            : Path.Combine(profileRoot, ModLaunch.ModsFolder);
        ModDiscovery discovery = ModLaunch.Discover(modsDirectory, extraPackages, report, profileRoot == null ? null : table,
                                                    linkBase.Profile, linkBase.ProfileMode);
        if (discovery.FailedToOpen && !skipFailed)
            return new ModInspection(report, null, discovery.UserOrder, discovery.Packages);
        SelectorStore selectors = profileRoot == null ? new SelectorStore()
            : SelectorStore.Load(Path.Combine(modsDirectory, SelectorStore.FileName));
        ModLinkResult link = ModLinker.Link(linkBase, discovery.Packages, discovery.UserOrder, selectors, skipFailed);
        return new ModInspection(report, link, discovery.UserOrder, discovery.Packages);
    }

    /// <summary>Prints the discovery's lines as the engine does, for the <c>omp</c> commands, and gives the link and
    /// the packages to close.</summary>
    public (ModLinkResult? Result, IReadOnlyList<ModPackage> Opened) Printed(TextWriter console)
    {
        foreach (ModReportLine line in Discovery.Lines) console.WriteLine(line);
        return (Link, Packages);
    }

    public void Dispose()
    {
        foreach (ModPackage package in Packages) package.Dispose();
    }
}
