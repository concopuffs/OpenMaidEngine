using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace Age.Mods.Linking;

/// <summary>
/// The selector allocation of spec §6.2, kept in <c>mods/selectors.toml</c> in the profile's data root. A mod that
/// ships new scripts or assets receives the lowest selector neither the install's own <c>*.AAI</c> catalogs nor
/// another mod use, and keeps it on every later link, whatever the load order. Only <see cref="Forget"/> releases one,
/// so removing a package and installing it again restores its saves.
/// </summary>
public sealed class SelectorStore
{
    public const string FileName = "selectors.toml";
    public const int First = 1, Last = 0x7f;

    private readonly SortedDictionary<string, int> _selectors;

    public SelectorStore() : this(new SortedDictionary<string, int>(StringComparer.Ordinal)) { }

    private SelectorStore(SortedDictionary<string, int> selectors) => _selectors = selectors;

    /// <summary>Every allocation, by mod id.</summary>
    public IReadOnlyDictionary<string, int> Selectors => _selectors;

    public SelectorStore Clone() => new(new SortedDictionary<string, int>(_selectors, StringComparer.Ordinal));

    /// <summary>The store at <paramref name="path"/>, or an empty one when the file does not exist.</summary>
    public static SelectorStore Load(string path)
        => File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8), path) : new SelectorStore();

    public static SelectorStore Parse(string text, string origin = FileName)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException error)
        {
            throw new InvalidDataException($"{origin}: {error.Message}", error);
        }
        var store = new SelectorStore();
        var owners = new Dictionary<int, string>();
        foreach (var (key, value) in table)
        {
            if (key != "selectors" || value is not TomlTable selectors)
                throw new InvalidDataException($"{origin}: unexpected key '{key}' (only a [selectors] table)");
            foreach (var (id, raw) in selectors)
            {
                if (raw is not long number || number is < First or > Last)
                    throw new InvalidDataException($"{origin}: selectors.\"{id}\" must be an integer {First}..{Last}");
                if (owners.TryGetValue((int)number, out string? other))
                    throw new InvalidDataException($"{origin}: selector {number} is allocated to both {other} and {id}");
                owners[(int)number] = id;
                store._selectors[id] = (int)number;
            }
        }
        return store;
    }

    public bool TryGet(string modId, out int selector) => _selectors.TryGetValue(modId, out selector);

    /// <summary>
    /// Gives each mod of <paramref name="modIds"/> (in that order) its kept selector, or the lowest free one. Errors
    /// go to <paramref name="report"/>: no selector free, or a kept selector the install now mounts itself.
    /// </summary>
    /// <returns>The selector of every mod that has one; mods that could not get one are missing.</returns>
    public IReadOnlyDictionary<string, int> Allocate(IEnumerable<string> modIds, IReadOnlySet<int> installSelectors,
                                                     ModLinkReport report)
    {
        var allocated = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string id in modIds)
        {
            if (_selectors.TryGetValue(id, out int kept))
            {
                if (installSelectors.Contains(kept))
                    report.Add(ModReportClass.Error, id, null,
                        $"its selector {kept} (mods/{FileName}) is now used by the install's own catalog APPEND{kept:00}.AAI; "
                        + $"--forget-mod {id} releases it, at the cost of saves made inside the mod's scripts");
                else
                    allocated[id] = kept;
                continue;
            }
            var used = _selectors.Values.ToHashSet();
            int free = Enumerable.Range(First, Last - First + 1)
                .FirstOrDefault(selector => !used.Contains(selector) && !installSelectors.Contains(selector));
            if (free == 0)
            {
                report.Add(ModReportClass.Error, id, null,
                    $"no append-catalog selector is free (1..{Last}; the install uses "
                    + $"{(installSelectors.Count == 0 ? "none" : string.Join(", ", installSelectors.Order()))}). Allocations: "
                    + string.Join(", ", _selectors.Select(pair => $"{pair.Key}={pair.Value}"))
                    + ". --forget-mod ID releases the selector of a mod that is no longer installed");
                continue;
            }
            _selectors[id] = free;
            allocated[id] = free;
        }
        return allocated;
    }

    /// <summary>Releases a mod's selector (<c>--forget-mod</c>). Saves made inside its scripts stop resolving.</summary>
    public bool Forget(string modId) => _selectors.Remove(modId);

    public string ToToml()
    {
        var text = new StringBuilder();
        text.Append("# Append-catalog selectors of installed mods (mod loader spec §6.2). A mod keeps its selector while\n")
            .Append("# it stays here; --forget-mod ID releases it.\n[selectors]\n");
        foreach (var (id, selector) in _selectors) text.Append('"').Append(id).Append("\" = ").Append(selector).Append('\n');
        return text.ToString();
    }

    /// <summary>Writes the store through a temporary file, so a failed write keeps the previous allocation.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, ToToml(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
