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
        IReadOnlyDictionary<string, string> reasons = Refusals(packages.Select(package =>
            new StackMember(package.Id, package.Manifest.Target, ModLuaLinker.ShipsLua(package), package.Manifest.Requires)).ToList());
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

    /// <summary>Why the <c>native</c> mode would refuse each package of a stack known by its manifests alone (the
    /// launcher's index, launcher spec §5.9), by the rule <see cref="Apply"/> applies: empty when the stack runs in
    /// <c>native</c>.</summary>
    public static IReadOnlyDictionary<string, string> NativeRefusals(IEnumerable<PackageIndexEntry> stack)
        => Refusals(stack.Select(entry => new StackMember(entry.Id, entry.Target, entry.ShipsLua, entry.Requires)).ToList());

    private sealed record StackMember(string Id, string? Target, bool ShipsLua, IReadOnlyList<ModRequirement> Requires);

    /// <summary>The refused packages of a stack, each with why: by itself, or for a requirement the mode refuses (a
    /// refused package of the stack, or an engine base mod the stack does not hold).</summary>
    private static IReadOnlyDictionary<string, string> Refusals(IReadOnlyList<StackMember> stack)
    {
        var held = stack.Select(member => member.Id).ToHashSet(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (StackMember member in stack)
            if (OwnReason(member.Target, member.ShipsLua) is { } reason)
                reasons.TryAdd(member.Id, reason);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (StackMember member in stack.Where(member => !reasons.ContainsKey(member.Id)))
                foreach (ModRequirement requirement in member.Requires)
                {
                    bool refused = reasons.ContainsKey(requirement.Id)
                                   || (!held.Contains(requirement.Id) && ShippedRefused(requirement.Id));
                    if (!refused) continue;
                    reasons[member.Id] = $"it requires {requirement.Id}, which the {Native} mode refuses";
                    changed = true;
                    break;
                }
        }
        return reasons;
    }

    /// <summary>Why the <c>native</c> mode refuses a package by itself, or null.</summary>
    private static string? OwnReason(string? target, bool shipsLua)
        => target == ModManifest.ExtendedTarget ? $"it targets {Extended}"
            : shipsLua ? "it ships Lua"
            : null;

    private static bool ShippedRefused(string id)
    {
        using ModPackage? shipped = ShippedPackages.Open(id);
        return shipped != null && OwnReason(shipped.Manifest.Target, ModLuaLinker.ShipsLua(shipped)) != null;
    }
}
