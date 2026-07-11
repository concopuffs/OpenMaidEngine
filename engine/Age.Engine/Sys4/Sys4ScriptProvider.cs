using Age.Engine.Hosting;
using Age.Engine.Model;

namespace Age.Engine.Sys4;

/// <summary>Loads root and call-script bytecode through the native loose-first asset-store seam.</summary>
public sealed class Sys4ScriptProvider : IScriptProvider
{
    private readonly OpcodeTable _table;
    private readonly IAssetStore _store;
    private readonly Dictionary<long, Script?> _cache = new();
    private readonly Dictionary<string, Script?> _nameCache = new(StringComparer.OrdinalIgnoreCase);

    public Sys4AssetCatalog Catalog { get; }
    public IReadOnlyList<string> ScriptNames => Catalog.ScriptNames;

    public Sys4ScriptProvider(OpcodeTable table, Sys4AssetCatalog catalog, IAssetStore store)
    { _table = table; Catalog = catalog; _store = store; }

    public static Sys4ScriptProvider Load(OpcodeTable table)
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        return new Sys4ScriptProvider(table, catalog,
            new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir));
    }

    public Script? GetById(long id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;
        var entry = Catalog.ResolvePacked(id);
        Script? script = entry is { IsPlaceholder: false }
                        && entry.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
            ? Parse(entry) : null;
        _cache[id] = script;
        return script;
    }

    public Script? GetByName(string name)
    {
        string key = name;
        if (_nameCache.TryGetValue(key, out var cached)) return cached;
        var entry = Catalog.ResolveName(key);
        Script? script = entry != null && entry.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
            ? Parse(entry) : null;
        _nameCache[key] = script;
        return script;
    }

    public Script RequireByName(string name)
        => GetByName(name) ?? throw new FileNotFoundException($"script is not in SYS4INI: {name}", name);

    private Script Parse(AssetEntry entry)
        => Sys4Loader.Parse(_store.ReadAll(entry), _table, entry.Name);
}
