using System.Text;
using Age.Mods.Linking;
using Tomlyn;
using Tomlyn.Model;

namespace Age.Mods.Launch;

/// <summary>
/// The stack recorded beside a save (spec §8, <c>SAVEnn.mods.toml</c>): each mod's id, version and package hash in load
/// order, the selector allocation, and the link cache key. <see cref="Compare"/> lists what differs from the current
/// stack; the loader warns with it and never blocks.
/// </summary>
public sealed record ModSaveStamp(IReadOnlyList<ModStackEntry> Mods, IReadOnlyDictionary<string, int> Selectors, string Key)
{
    public const string Suffix = ".mods.toml";

    public static ModSaveStamp Of(MountedModLayer layer) => new(layer.Manifest.Order, layer.Manifest.Selectors, layer.Key);

    public string ToToml()
    {
        var text = new StringBuilder();
        text.Append("# The mod stack this save was made with (Open Maid Engine mod loader spec §8).\n")
            .Append("stamp = 1\nkey = \"").Append(Key).Append("\"\n");
        foreach (ModStackEntry mod in Mods)
            text.Append("\n[[mods]]\nid = \"").Append(mod.Id).Append("\"\nversion = \"").Append(mod.Version)
                .Append("\"\nhash = \"").Append(mod.Hash).Append("\"\n");
        text.Append("\n[selectors]\n");
        foreach (var (id, selector) in Selectors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.Append('"').Append(id).Append("\" = ").Append(selector).Append('\n');
        return text.ToString();
    }

    /// <summary>The stamp of a sidecar, or null when it cannot be read (reported by the caller as unreadable).</summary>
    public static ModSaveStamp? TryParse(string text)
    {
        try
        {
            TomlTable table = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
            var mods = new List<ModStackEntry>();
            if (table.TryGetValue("mods", out object? list) && list is TomlTableArray entries)
                foreach (TomlTable entry in entries)
                    mods.Add(new ModStackEntry((string)entry["id"], (string)entry["version"], (string)entry["hash"]));
            var selectors = new Dictionary<string, int>(StringComparer.Ordinal);
            if (table.TryGetValue("selectors", out object? raw) && raw is TomlTable selectorTable)
                foreach (var (id, value) in selectorTable) selectors[id] = checked((int)(long)value);
            return new ModSaveStamp(mods, selectors, table.TryGetValue("key", out object? key) ? (string)key : "");
        }
        catch (Exception error) when (error is TomlException or InvalidCastException or KeyNotFoundException
                                          or OverflowException)
        {
            return null;
        }
    }

    /// <summary>What changed from <paramref name="saved"/> (null: a save without a stamp) to <paramref name="current"/>
    /// (null: no mods now). Empty when they match.</summary>
    public static IReadOnlyList<string> Compare(ModSaveStamp? saved, ModSaveStamp? current)
    {
        var savedMods = saved?.Mods ?? [];
        var currentMods = current?.Mods ?? [];
        var differences = new List<string>();
        var before = savedMods.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        var now = currentMods.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        foreach (ModStackEntry mod in currentMods.Where(mod => !before.ContainsKey(mod.Id)))
            differences.Add($"added {mod.Id} {mod.Version}");
        foreach (ModStackEntry mod in savedMods.Where(mod => !now.ContainsKey(mod.Id)))
            differences.Add($"removed {mod.Id} {mod.Version}");
        foreach (ModStackEntry mod in currentMods.Where(mod => before.ContainsKey(mod.Id)))
        {
            ModStackEntry old = before[mod.Id];
            if (old.Version != mod.Version) differences.Add($"{mod.Id} {old.Version} -> {mod.Version}");
            else if (old.Hash != mod.Hash) differences.Add($"{mod.Id} {mod.Version} changed (package hash differs)");
        }
        var common = currentMods.Select(mod => mod.Id).Where(before.ContainsKey).ToList();
        var commonBefore = savedMods.Select(mod => mod.Id).Where(now.ContainsKey).ToList();
        if (!common.SequenceEqual(commonBefore))
            differences.Add($"reordered: {string.Join(" -> ", commonBefore)} became {string.Join(" -> ", common)}");
        var selectorsBefore = saved?.Selectors ?? new Dictionary<string, int>();
        var selectorsNow = current?.Selectors ?? new Dictionary<string, int>();
        foreach (var (id, selector) in selectorsBefore)
            if (selectorsNow.TryGetValue(id, out int moved) && moved != selector)
                differences.Add($"{id}'s selector {selector} -> {moved}: saves inside its scripts will not resolve");
        return differences;
    }
}
