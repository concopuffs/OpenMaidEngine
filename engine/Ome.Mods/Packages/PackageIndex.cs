using Ome.Mods.Manifest;

namespace Ome.Mods.Packages;

/// <summary>One candidate as its manifest describes it, read without opening or hashing the rest of the package
/// (launcher spec §5.3; the start of §5.9's index).</summary>
/// <param name="Game">The game the package targets (<c>[game].profile</c>); null for one that targets every game.</param>
/// <param name="ShipsLua">Whether the package ships Lua: a <c>[lua]</c> table, hooks, or <c>lua/</c> or <c>events/</c>
/// files.</param>
public sealed record PackageIndexEntry(string Path, string Id, SemanticVersion Version, string? Game, string? Target,
                                       IReadOnlyList<ModRequirement> Requires, bool ShipsLua)
{
    /// <summary>Whether the package can run on <paramref name="game"/>.</summary>
    public bool IsFor(string game) => Game == null || Game == game;
}

/// <summary>Reads a candidate's manifest only, under the package reader's path and size limits.</summary>
public static class PackageIndex
{
    /// <summary>The candidate's index entry. A folder is walked and an <c>.omp</c>'s central directory read; of the
    /// content, only <c>manifest.toml</c> is. Throws what <see cref="ModPackage.Open"/> throws for an unreadable
    /// manifest.</summary>
    public static PackageIndexEntry Read(string path, PackageLimits? limits = null)
    {
        using IPackageContainer container = ModPackage.OpenContainer(path, limits);
        ModManifest manifest = ModPackage.ReadManifest(container);
        bool shipsLua = manifest.Lua != null || manifest.Hooks.Count > 0
                        || container.Entries.Any(entry => entry.Path.StartsWith(ModLayout.LuaFolder + "/", StringComparison.Ordinal)
                                                          || entry.Path.StartsWith(ModLayout.EventsFolder + "/", StringComparison.Ordinal));
        return new PackageIndexEntry(container.Location, manifest.Id, manifest.Version, manifest.Profile, manifest.Target,
                                     manifest.Requires, shipsLua);
    }
}
