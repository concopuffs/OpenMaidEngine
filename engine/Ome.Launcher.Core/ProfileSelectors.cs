using Ome.Mods.Launch;
using Ome.Mods.Packages;

namespace Ome.Launcher.Core;

/// <summary>A selector a profile holds (launcher spec §8.4 Mods, under "Advanced"). <see cref="Gone"/> when none of the
/// profile's folders holds the package and the engine does not carry it: only then does the page offer to release
/// it.</summary>
public sealed record HeldSelector(string Id, int Selector, bool Gone);

/// <summary>The selectors a profile holds, and releasing one (spec §5.9, §8.4): one action, done at once under the
/// profile's lock; nothing is stored for the next start.</summary>
public static class ProfileSelectors
{
    /// <summary>The profile's selectors, by id, each marked gone or not against <paramref name="view"/>.</summary>
    public static IReadOnlyList<HeldSelector> List(ProfileEntry profile, LibraryView view)
    {
        var present = view.Packages.Where(package => package.Entry != null).Select(package => package.Id)
            .ToHashSet(StringComparer.Ordinal);
        return SelectorRelease.Held(profile.Root).Selectors
            .Select(pair => new HeldSelector(pair.Key, pair.Value,
                                             !present.Contains(pair.Key) && !ShippedPackages.Ids.Contains(pair.Key)))
            .ToList();
    }

    /// <summary>Releases the selectors of <paramref name="ids"/> under the profile's lock. Refused, writing nothing,
    /// while the game runs the profile (<see cref="ProfileBusyException"/>).</summary>
    public static IReadOnlyList<ReleasedSelector> Release(ProfileEntry profile, IReadOnlyList<string> ids)
    {
        using RunLock held = ProfileStore.Lock(profile.Root);
        return SelectorRelease.Release(profile.Root, ids);
    }
}
