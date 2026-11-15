using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ome.Engine.Modding;

/// <summary>
/// A named global of a base mod's layout (Lua scripting spec §9.1): its bank, its first cell, and its shape. A scalar
/// has no dimensions; an array has one or two, of which only the first may be open (null: the registry does not know
/// the row count). <see cref="Columns"/> name the last dimension's indices: a row's columns in a 2-D array, the
/// elements of a 1-D one. <see cref="Aliases"/> are names the global had before a rename, still exported.
/// </summary>
public sealed record LayoutGlobal(string Name, CellKind Kind, int Address, IReadOnlyList<int?> Dimensions,
                                  IReadOnlyDictionary<string, int> Columns, IReadOnlyList<string> Aliases)
{
    public bool IsScalar => Dimensions.Count == 0;
}

/// <summary>
/// A base mod's layout (Lua scripting spec §9.1): every registry global's name, bank, address and shape, as
/// <c>tools/base_mod_build.py</c> writes it to <c>layout/globals.json</c>. The engine builds a mod's accessors from
/// it. A name and an alias are one name: both find the global.
/// </summary>
public sealed partial class GameLayout
{
    public const int Format = 1;

    private readonly Dictionary<string, LayoutGlobal> _byName;

    public string Profile { get; }
    /// <summary>The globals, by name in ordinal order.</summary>
    public IReadOnlyList<LayoutGlobal> Globals { get; }

    private GameLayout(string profile, IReadOnlyList<LayoutGlobal> globals, Dictionary<string, LayoutGlobal> byName)
    {
        Profile = profile;
        Globals = globals;
        _byName = byName;
    }

    /// <summary>The global a name or an alias names.</summary>
    public bool TryGet(string name, out LayoutGlobal global) => _byName.TryGetValue(name, out global!);

    /// <summary>Every exported name: each global's name and its aliases.</summary>
    public IEnumerable<string> Names => _byName.Keys;

    [GeneratedRegex("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    /// <summary>A column's name: a Lua name (the registries' column names may hold capitals, as asset names do).</summary>
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnName();

    /// <summary>Reads <c>layout/globals.json</c>. A document that is not a layout of this format fails with a
    /// <see cref="FormatException"/> that says what is wrong.</summary>
    public static GameLayout Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            throw new FormatException($"not JSON: {error.Message}");
        }
        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("not a JSON object");
            if (!root.TryGetProperty("layout", out JsonElement format) || !format.TryGetInt32(out int version))
                throw new FormatException("it has no \"layout\" format number");
            if (version != Format) throw new FormatException($"its format is {version}; this engine reads {Format}");
            if (!root.TryGetProperty("profile", out JsonElement profile) || profile.ValueKind != JsonValueKind.String
                || profile.GetString() is not { Length: > 0 } profileId)
                throw new FormatException("it has no \"profile\"");
            if (!root.TryGetProperty("globals", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
                throw new FormatException("it has no \"globals\" list");
            var globals = new List<LayoutGlobal>();
            var byName = new Dictionary<string, LayoutGlobal>(StringComparer.Ordinal);
            foreach (JsonElement entry in list.EnumerateArray())
            {
                LayoutGlobal global = Global(entry);
                foreach (string name in global.Aliases.Prepend(global.Name))
                    if (!byName.TryAdd(name, global))
                        throw new FormatException($"'{name}' names both {byName[name].Name} and {global.Name}");
                globals.Add(global);
            }
            globals.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return new GameLayout(profileId, globals, byName);
        }
    }

    private static LayoutGlobal Global(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("name", out JsonElement nameElement)
            || nameElement.ValueKind != JsonValueKind.String || nameElement.GetString() is not { } name
            || !Identifier().IsMatch(name))
            throw new FormatException("a global has no lowercase identifier as its \"name\"");
        string where = $"global {name}";
        CellKind kind = (entry.TryGetProperty("bank", out JsonElement bank) && bank.ValueKind == JsonValueKind.String
                ? bank.GetString() : null) switch
        {
            "G" => CellKind.Integer,
            "GF" => CellKind.Float,
            "GS" => CellKind.String,
            var other => throw new FormatException($"{where}: bank {other ?? "(none)"} is not G, GF or GS"),
        };
        string addressText = entry.TryGetProperty("address", out JsonElement address) && address.ValueKind == JsonValueKind.String
            ? address.GetString() ?? "" : "";
        if (!addressText.StartsWith("0x", StringComparison.Ordinal)
            || !int.TryParse(addressText.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                             System.Globalization.CultureInfo.InvariantCulture, out int first) || first < 0)
            throw new FormatException($"{where}: address '{addressText}' is not 0x and a cell address");
        var dimensions = new List<int?>();
        if (!entry.TryGetProperty("dims", out JsonElement dims) || dims.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{where}: it has no \"dims\" list");
        foreach (JsonElement dimension in dims.EnumerateArray())
        {
            if (dimension.ValueKind == JsonValueKind.Null && dimensions.Count == 0) dimensions.Add(null);
            else if (dimension.ValueKind == JsonValueKind.Number && dimension.TryGetInt32(out int size) && size > 0)
                dimensions.Add(size);
            else throw new FormatException($"{where}: a dimension is {dimension.GetRawText()}; it is a positive count, "
                                           + "or null for an open first dimension");
        }
        if (dimensions.Count > 2) throw new FormatException($"{where}: it has {dimensions.Count} dimensions; a layout maps two");
        long extent = dimensions.Aggregate(1L, (cells, size) => cells * (size ?? 1));
        if (first + extent - 1 > int.MaxValue) throw new FormatException($"{where}: its cells run past the last address");
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        if (entry.TryGetProperty("columns", out JsonElement map))
        {
            if (map.ValueKind != JsonValueKind.Object || dimensions.Count == 0)
                throw new FormatException($"{where}: columns are an object, on an array");
            int? last = dimensions[^1];
            foreach (JsonProperty column in map.EnumerateObject())
                if (!ColumnName().IsMatch(column.Name) || column.Value.ValueKind != JsonValueKind.Number
                    || !column.Value.TryGetInt32(out int index) || index < 0
                    || index >= (last ?? int.MaxValue))
                    throw new FormatException($"{where}: column {column.Name} = {column.Value.GetRawText()} is not a name "
                                              + "and an index of its last dimension");
                else columns[column.Name] = index;
        }
        var aliases = new List<string>();
        if (entry.TryGetProperty("aliases", out JsonElement aliasList))
        {
            if (aliasList.ValueKind != JsonValueKind.Array) throw new FormatException($"{where}: aliases are a list");
            foreach (JsonElement alias in aliasList.EnumerateArray())
                if (alias.ValueKind == JsonValueKind.String && alias.GetString() is { } text && Identifier().IsMatch(text))
                    aliases.Add(text);
                else throw new FormatException($"{where}: alias {alias.GetRawText()} is not a lowercase identifier");
        }
        return new LayoutGlobal(name, kind, first, dimensions, columns, aliases);
    }
}
