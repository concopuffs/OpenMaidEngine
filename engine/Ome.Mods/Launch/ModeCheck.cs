using Ome.Mods.Linking;
using Ome.Mods.Manifest;
using Ome.Mods.Packages;

namespace Ome.Mods.Launch;

/// <summary>
/// A profile's mode (launcher spec §5.4). <c>native</c> keeps a profile to what native AGE runs: it refuses an enabled
/// or <c>--mod</c> package that targets <c>extended</c>, that ships Lua (a Lua library has no <c>[game]</c> table and
/// so no target, which alone would let it through), or that requires a package the mode refuses. The engine's own base
/// mods count: they target <c>extended</c>, and nothing stops a <c>native</c> package from requiring one. The check runs
/// on the discovered packages before a base mod is mounted, so a refusal names the package the player enabled, never
/// the base mod it required. <c>extended</c>, and a run without a profile file, admit everything.
/// </summary>
public static class ModeCheck
{
    public const string Native = "native", Extended = "extended";

    /// <summary>Removes and disposes the packages <paramref name="mode"/> refuses, each with an error in the report.
    /// Returns whether any was refused.</summary>
    public static bool Apply(List<ModPackage> packages, string? mode, ModLinkReport report)
    {
        if (mode != Native) return false;
        var held = packages.Select(package => package.Id).ToHashSet(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ModPackage package in packages)
            if (OwnReason(package.Manifest, ModLuaLinker.ShipsLua(package)) is { } reason)
                reasons.TryAdd(package.Id, reason);

        // A requirement the mode refuses: a refused package of the stack, or an engine base mod the stack does not hold.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (ModPackage package in packages.Where(package => !reasons.ContainsKey(package.Id)))
                foreach (ModRequirement requirement in package.Manifest.Requires)
                {
                    bool refused = reasons.ContainsKey(requirement.Id)
                                   || (!held.Contains(requirement.Id) && ShippedRefused(requirement.Id));
                    if (!refused) continue;
                    reasons[package.Id] = $"it requires {requirement.Id}, which the {Native} mode refuses";
                    changed = true;
                    break;
                }
        }
        if (reasons.Count == 0) return false;
        foreach (ModPackage package in packages.Where(package => reasons.ContainsKey(package.Id)).ToList())
        {
            report.Add(ModReportClass.Error, package.Id, null,
                $"the profile's mode is {Native}, which runs only what native AGE runs, and {reasons[package.Id]}; "
                + $"switch the profile to {Extended} to use it");
            packages.Remove(package);
            package.Dispose();
        }
        return true;
    }

    /// <summary>Why the <c>native</c> mode refuses a package by itself, or null.</summary>
    private static string? OwnReason(ModManifest manifest, bool shipsLua)
        => manifest.Target == ModManifest.ExtendedTarget ? $"it targets {Extended}"
            : shipsLua ? "it ships Lua"
            : null;

    private static bool ShippedRefused(string id)
    {
        using ModPackage? shipped = ShippedPackages.Open(id);
        return shipped != null && OwnReason(shipped.Manifest, ModLuaLinker.ShipsLua(shipped)) != null;
    }
}
