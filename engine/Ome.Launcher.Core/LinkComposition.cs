using Ome.Engine.Modding;
using Ome.Engine.Profiles;
using Ome.Engine.Sys4;
using Ome.Engine.Vm;
using Ome.Mods.Launch;
using Ome.Mods.Linking;
using Ome.Mods.Tools;
using Ome.Scripting.Lua;

namespace Ome.Launcher.Core;

/// <summary>
/// The launcher's composition (engine layering spec §7.6): the one place in it that puts the engine's link together
/// and so reads an engine build's ABI, as <c>Ome.Cli</c> does (<c>EngineBuildTests</c> allows this file alone).
/// </summary>
internal static class LinkComposition
{
    /// <summary>The link as <c>Ome.Cli</c>'s <c>omp check --data-root</c> makes it: the run resolved from the profile
    /// file, the catalog, the asset store with the profile's overlays, and the engine's link base with a Lua runtime
    /// (spec §5.9). Gate 8 holds the two to the same text.</summary>
    public static LinkCheck Link(string root, GameProfileRegistry registry, string? machineFolder)
    {
        ResolvedRun run = RunResolver.Resolve([RunResolver.DataRootOption, root], root, registry, machineFolder);
        IReadOnlyList<string> arguments = run.Arguments;
        string gameRoot = Path.GetFullPath(RunResolver.LastValue(arguments, RunResolver.GameRootOption)
                                           ?? throw new RunResolutionException($"{root} names no install"));
        string sys4Ini = Path.Combine(gameRoot, "SYS4INI.BIN");
        SelectedGameProfile game = GameProfileSelection.Resolve(arguments, GameCatalogIdentity.ReadSys4IniHeader(sys4Ini),
                                                                registry);
        EngineBuild build = EngineBuilds.ForProfile(game.Profile);
        Sys4AssetCatalog catalog = Sys4AssetCatalog.Load(sys4Ini);
        AssetLaunchOptions assets = AssetLaunchOptions.Resolve(arguments, gameRoot);
        var store = new Sys4AssetStore(catalog, gameRoot, assets.LooseRoots.ToArray());
        using LazyModRuntime lua = LuaModRuntime.Lazy();
        ModLinkBase linkBase = ModLinkBase.ForGame(game.Profile.Id, build.AbiId, sys4Ini, catalog, store, out _)
            with { Lua = lua, ProfileMode = run.ProfileFile?.ModeName };
        using ModInspection inspection = ModInspection.Inspect(linkBase, run.DataRoot, [],
                                                               arguments.Contains(ModLaunchOptions.SkipFailedOption), run.Mods);
        ModLinkReport? report = inspection.Link?.Report;
        var dropped = (report?.Dropped ?? []).Concat(inspection.Discovery.FailedMods).Distinct()
            .Order(StringComparer.Ordinal).ToList();
        return new LinkCheck(OmpCommands.CheckReport(inspection), inspection.Discovery, report, inspection.Order,
                             inspection.Moves, dropped);
    }
}
