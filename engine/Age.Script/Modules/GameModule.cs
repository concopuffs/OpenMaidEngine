using System.Text.Json;
using System.Text.RegularExpressions;
using Age.Script.Syntax;

namespace Age.Script.Modules;

/// <summary>A named global cell: its bank operand type (3 int, 4 float, 5 string), address, array dimensions (0 for
/// an open leading dimension) and, for two-dimensional tables, named columns.</summary>
public sealed record GlobalSymbol(string Name, int Type, uint Address, IReadOnlyList<int> Dimensions,
                                  IReadOnlyDictionary<string, int> Columns)
{
    /// <summary>The column name of a row-table column, if it has one.</summary>
    public string? ColumnName(int column) => Columns.FirstOrDefault(pair => pair.Value == column).Key;
}

/// <summary>
/// A generated game module (spec §3.2). <c>&lt;profile&gt;.globals</c> holds every named global of the profile's
/// registry (<c>vm-map/globals*.toml</c>, generated to <c>build/games/&lt;profile&gt;/globals.json</c>); integer and
/// string globals are separate banks, so symbols are keyed by bank and address. <c>&lt;profile&gt;.scripts</c> holds
/// every catalog script by name, with the packed id <c>run</c> passes to call-script.
/// </summary>
public sealed class GameModule
{
    private static readonly Regex Identifier = new("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex TypeShape = new(@"^(int|string|float)((?:\[\d*\])*)$", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, GlobalSymbol> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Type, uint Address), GlobalSymbol> _byCell = new();
    private readonly Dictionary<string, uint> _scriptIds = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _scriptNames = new();

    public string Name { get; }

    public GameModule(string name, IEnumerable<GlobalSymbol> symbols, IEnumerable<(string Name, uint Id)>? scripts = null)
    {
        Name = name;
        foreach (GlobalSymbol symbol in symbols)
        {
            if (!_byName.TryAdd(symbol.Name, symbol))
                throw new InvalidDataException($"module {name} names '{symbol.Name}' twice");
            _byCell[(symbol.Type, symbol.Address)] = symbol;
        }
        foreach (var (script, id) in scripts ?? [])
            if (_scriptIds.TryAdd(script, id)) _scriptNames.TryAdd(id, script);
    }

    /// <summary>A script module from catalog entries (<c>RECOVER.BIN</c> → <c>RECOVER</c>, its packed id).</summary>
    public static GameModule Scripts(string profileId, IEnumerable<(string FileName, long PackedId)> scripts)
        => new($"{profileId}.scripts", [],
            scripts.Select(entry => (Path.GetFileNameWithoutExtension(entry.FileName), (uint)entry.PackedId)));

    public bool TryGetScript(string name, out uint id) => _scriptIds.TryGetValue(name, out id);

    public bool TryGetScript(uint id, out string name) => _scriptNames.TryGetValue(id, out name!);

    public IReadOnlyCollection<GlobalSymbol> Symbols => _byName.Values;

    public bool TryGet(string name, out GlobalSymbol symbol) => _byName.TryGetValue(name, out symbol!);

    public bool TryGet(int type, uint address, out GlobalSymbol symbol) => _byCell.TryGetValue((type, address), out symbol!);

    /// <summary>Loads <c>&lt;profile&gt;.globals</c> from a generated registry view. Entries without a usable name or
    /// with an unknown shape are skipped, as are names that would read as a keyword or a storage bank.</summary>
    public static GameModule LoadGlobals(string profileId, string globalsJsonPath)
    {
        using FileStream stream = File.OpenRead(globalsJsonPath);
        using JsonDocument doc = JsonDocument.Parse(stream);
        var symbols = new List<GlobalSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty cell in doc.RootElement.GetProperty("cells").EnumerateObject())
        {
            JsonElement entry = cell.Value;
            if (entry.GetProperty("name").GetString() is not { Length: > 0 } name || !Identifier.IsMatch(name)
                || Abi.IntrinsicCatalog.Keywords.Contains(name) || StorageBanks.TypeByBank.ContainsKey(name)
                || !seen.Add(name))
                continue;
            Match shape = TypeShape.Match(entry.GetProperty("type").GetString() ?? "");
            if (!shape.Success) continue;
            int type = shape.Groups[1].Value switch { "string" => 0x5, "float" => 0x4, _ => 0x3 };
            // An open dimension (int[][27]) is 0: the row count is unknown, the row width is not.
            int[] dimensions = Regex.Matches(shape.Groups[2].Value, @"\[(\d*)\]")
                .Select(m => m.Groups[1].Value.Length == 0 ? 0 : int.Parse(m.Groups[1].Value)).ToArray();
            if (dimensions.Skip(1).Contains(0)) continue;
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            if (entry.TryGetProperty("columns", out JsonElement map) && dimensions.Length == 2)
                foreach (JsonProperty column in map.EnumerateObject())
                    if (column.Value.GetString() is { } columnName && Identifier.IsMatch(columnName)
                        && int.TryParse(column.Name, out int index))
                        columns.TryAdd(columnName, index);
            uint address = Convert.ToUInt32(entry.GetProperty("address").GetString(), 16);
            symbols.Add(new GlobalSymbol(name, type, address, dimensions, columns));
        }
        return new GameModule($"{profileId}.globals", symbols);
    }
}
