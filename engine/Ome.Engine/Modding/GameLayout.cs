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

    /// <summary>
    /// How many rows an index may reach (the elements of a 1-D array): the first dimension, or for an open one the rows
    /// that fit before the next named global of the same bank, so an accessor never reaches past its table into
    /// another global's cells. Null for a scalar, and for an open array that no named global follows.
    /// </summary>
    public int? Rows { get; init; }

    /// <summary>The cells of one row: the last dimension of a 2-D array, 1 otherwise.</summary>
    public int RowWidth => Dimensions.Count == 2 ? Dimensions[1]!.Value : 1;
}

/// <summary>A base mod's layout and the id of the base mod it belongs to: what a mod's accessors and the console's read
/// (spec §9.3, §13).</summary>
public sealed record BaseModLayout(string ModId, GameLayout Layout);

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
    /// <summary>The document it was read from, which the link cache keeps.</summary>
    public string Json { get; }

    private GameLayout(string profile, IReadOnlyList<LayoutGlobal> globals, Dictionary<string, LayoutGlobal> byName,
                       string json)
    {
        Profile = profile;
        Globals = globals;
        _byName = byName;
        Json = json;
    }

    /// <summary>The global a name or an alias names.</summary>
    public bool TryGet(string name, out LayoutGlobal global) => _byName.TryGetValue(name, out global!);

    /// <summary>Every exported name: each global's name and its aliases.</summary>
    public IEnumerable<string> Names => _byName.Keys;

    /// <summary>
    /// The name a cell has (spec §12, the trace): a scalar's, or a table's with the cell's row, and column in a 2-D
    /// table, such as <c>unit_current_levels[2]</c>; null when no global of the bank covers it. A scalar at the address
    /// wins over a table whose rows reach it.
    /// </summary>
    public string? NameAt(CellKind kind, int address)
    {
        foreach (LayoutGlobal global in Globals)
            if (global.Kind == kind && global.IsScalar && global.Address == address) return global.Name;
        foreach (LayoutGlobal global in Globals)
        {
            if (global.Kind != kind || global.IsScalar || address < global.Address) continue;
            int offset = address - global.Address, width = global.RowWidth, row = offset / width;
            // An open table that no global follows reaches every row.
            if (global.Rows is int rows && row >= rows) continue;
            return global.Dimensions.Count == 2 ? $"{global.Name}[{row}][{offset % width}]" : $"{global.Name}[{row}]";
        }
        return null;
    }

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
            var parsed = new List<LayoutGlobal>();
            foreach (JsonElement entry in list.EnumerateArray()) parsed.Add(Global(entry));
            var globals = WithRows(parsed);
            globals.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            var byName = new Dictionary<string, LayoutGlobal>(StringComparer.Ordinal);
            foreach (LayoutGlobal global in globals)
                foreach (string name in global.Aliases.Prepend(global.Name))
                    if (!byName.TryAdd(name, global))
                        throw new FormatException($"'{name}' names both {byName[name].Name} and {global.Name}");
            return new GameLayout(profileId, globals, byName, json);
        }
    }

    /// <summary>Each array's <see cref="LayoutGlobal.Rows"/>: its first dimension, or for an open one the rows before the
    /// next global of its bank by address.</summary>
    private static List<LayoutGlobal> WithRows(List<LayoutGlobal> globals)
    {
        var result = new List<LayoutGlobal>(globals.Count);
        foreach (LayoutGlobal global in globals)
        {
            if (global.IsScalar)
            {
                result.Add(global);
                continue;
            }
            int? rows = global.Dimensions[0];
            if (rows == null)
            {
                int? next = null;
                foreach (LayoutGlobal other in globals)
                    if (other.Kind == global.Kind && other.Address > global.Address && (next == null || other.Address < next))
                        next = other.Address;
                if (next is int end) rows = (end - global.Address) / global.RowWidth;
            }
            result.Add(global with { Rows = rows });
        }
        return result;
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
