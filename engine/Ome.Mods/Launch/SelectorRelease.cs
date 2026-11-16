using Ome.Mods.Linking;

namespace Ome.Mods.Launch;

/// <summary>One id given to <see cref="SelectorRelease.Release"/>: the selector it released, or null when it held
/// none.</summary>
public sealed record ReleasedSelector(string Id, int? Selector)
{
    public bool Held => Selector != null;
}

/// <summary>
/// Releasing a selector (mod loader spec §6.2, launcher spec §5.9) as a call: what <c>--forget-mod</c> does at a
/// start, and what the launcher's "release" does at once. Saves made inside a released package's scripts stop
/// resolving. The caller holds the data root's run lock, as the engine does at its start and the launcher takes it, so
/// a release never races a running engine.
/// </summary>
public static class SelectorRelease
{
    /// <summary>A data root's <c>mods/selectors.toml</c>, which stays there when a profile file names the packages'
    /// folders (launcher spec §5.3).</summary>
    public static string PathFor(string profileRoot)
        => Path.Combine(profileRoot, ModLaunch.ModsFolder, SelectorStore.FileName);

    /// <summary>The selectors the data root holds.</summary>
    public static SelectorStore Held(string profileRoot) => SelectorStore.Load(PathFor(profileRoot));

    /// <summary>Releases each id's selector and says, per id, which it held. Writes the file only when one was
    /// released.</summary>
    public static IReadOnlyList<ReleasedSelector> Release(string profileRoot, IReadOnlyList<string> ids)
    {
        string path = PathFor(profileRoot);
        SelectorStore store = SelectorStore.Load(path);
        var released = new List<ReleasedSelector>();
        foreach (string id in ids)
        {
            int? selector = store.TryGet(id, out int value) ? value : null;
            if (selector != null) store.Forget(id);
            released.Add(new ReleasedSelector(id, selector));
        }
        if (released.Any(entry => entry.Held)) store.Save(path);
        return released;
    }
}
