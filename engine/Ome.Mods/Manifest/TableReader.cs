using Tomlyn.Model;

namespace Ome.Mods.Manifest;

/// <summary>Typed access to one TOML table that records each problem and every key it read: the strict reading
/// of <c>manifest.toml</c> (<see cref="ManifestReader"/>) and of <c>profile.toml</c>
/// (<see cref="Ome.Mods.Launch.ProfileFile"/>), so the two report a problem alike.</summary>
internal sealed class TableReader
{
    private readonly TomlTable _table;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    public string Path { get; }
    public List<string> Problems { get; }

    public TableReader(TomlTable table, string path, List<string> problems)
    {
        _table = table;
        Path = path;
        Problems = problems;
    }

    public IEnumerable<string> Keys() => _table.Keys.ToArray();
    public bool Has(string key) => _table.ContainsKey(key);
    public void Consume(string key) => _read.Add(key);

    private object? Get(string key, bool required)
    {
        _read.Add(key);
        if (_table.TryGetValue(key, out object? value)) return value;
        if (required) Problems.Add($"{Path}{key}: required");
        return null;
    }

    public string? String(string key, bool required = false)
    {
        object? value = Get(key, required);
        if (value is null or string) return (string?)value;
        Problems.Add($"{Path}{key}: expected a string");
        return null;
    }

    public int? Integer(string key, bool required = false)
    {
        object? value = Get(key, required);
        if (value is null) return null;
        if (value is long number and >= int.MinValue and <= int.MaxValue) return (int)number;
        Problems.Add($"{Path}{key}: expected an integer");
        return null;
    }

    public bool? Boolean(string key, bool required = false)
    {
        object? value = Get(key, required);
        if (value is null or bool) return (bool?)value;
        Problems.Add($"{Path}{key}: expected true or false");
        return null;
    }

    public List<string> Strings(string key)
    {
        object? value = Get(key, false);
        if (value is null) return [];
        if (value is TomlArray array && array.All(item => item is string))
            return array.Cast<string>().ToList();
        Problems.Add($"{Path}{key}: expected an array of strings");
        return [];
    }

    /// <summary>A key that takes one string or an array of strings: null when it is absent or of another type (which
    /// is reported).</summary>
    public List<string>? StringOrStrings(string key)
    {
        object? value = Get(key, false);
        if (value is null) return null;
        if (value is string one) return [one];
        if (value is TomlArray array && array.All(item => item is string))
            return array.Cast<string>().ToList();
        Problems.Add($"{Path}{key}: expected a string or an array of strings");
        return null;
    }

    public TableReader? Table(string key, bool required = false, bool quoted = false)
    {
        object? value = Get(key, required);
        string child = quoted ? $"{Path}\"{key}\"." : $"{Path}{key}.";
        if (value is null) return null;
        if (value is TomlTable table) return new TableReader(table, child, Problems);
        Problems.Add($"{Path}{(quoted ? $"\"{key}\"" : key)}: expected a table");
        return null;
    }

    public IEnumerable<TableReader> TableArray(string key)
    {
        object? value = Get(key, false);
        if (value is null) return [];
        if (value is TomlTableArray array)
            return array.Select((table, k) => new TableReader(table, $"{Path}{key}[{k}].", Problems)).ToList();
        Problems.Add($"{Path}{key}: expected an array of tables ([[{key}]])");
        return [];
    }

    /// <summary>The tables of a <c>[[key]]</c> array, for a reader of their own.</summary>
    public IReadOnlyList<TomlTable> RawTableArray(string key)
    {
        object? value = Get(key, false);
        if (value is null) return [];
        if (value is TomlTableArray array) return array.ToList();
        Problems.Add($"{Path}{key}: expected an array of tables ([[{key}]])");
        return [];
    }

    public void RejectUnknown()
    {
        foreach (string key in _table.Keys.Where(key => !_read.Contains(key)))
            Problems.Add($"{Path}{key}: unknown key");
    }
}
