using Ome.Engine.Hosting;
using Ome.Engine.Model;

namespace Ome.Engine.Sys4;

/// <summary>Loads root and call-script bytecode through the native loose-first asset-store seam.</summary>
public sealed class Sys4ScriptProvider : IScriptProvider
{
    private readonly OpcodeTable _table;
    private readonly IAssetStore _store;
    private readonly Dictionary<long, Script?> _cache = new();
    private readonly Dictionary<string, Script?> _nameCache = new(StringComparer.OrdinalIgnoreCase);

    public Sys4AssetCatalog Catalog { get; }
    public IReadOnlyList<string> ScriptNames => Catalog.ScriptNames;
    public IReadOnlyList<int> MountedAppendSelectors => Catalog.AppendPacks.Keys.ToArray();

    public Sys4ScriptProvider(OpcodeTable table, Sys4AssetCatalog catalog, IAssetStore store)
    { _table = table; Catalog = catalog; _store = store; }

    public static Sys4ScriptProvider Load(OpcodeTable table)
    {
        var catalog = Sys4AssetCatalog.Load(Paths.Sys4Ini);
        return new Sys4ScriptProvider(table, catalog,
            new Sys4AssetStore(catalog, Paths.GameDir, Paths.GameDir));
    }

    /// <summary>The script a packed id names. A failure is not kept: the loader's 再試行 asks again.</summary>
    public Script? GetById(long id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;
        var entry = Catalog.ResolvePacked(id);
        Script? script = entry is { IsPlaceholder: false }
                        && entry.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
            ? Parse(entry) : null;
        if (script != null) _cache[id] = script;
        return script;
    }

    /// <summary>
    /// What asset_open_indexed_entry@0x44f390 says for an id <see cref="GetById"/> found nothing for. A base id (high
    /// byte 0) past the file table: 「ファイル番号 n は不正な値です．」. A record with no file: 「name
    /// を開くことが出来ません．」 with ERROR CODE 2, the code of the loose open that failed first. A high byte naming no
    /// mounted catalog: 「拡張ファイル情報ファイル n は読み込まれていません．」; an index past that catalog: 「拡張ファイル
    /// ERROR を開くことが出来ません．n」. Null where the port cannot follow AGE: a high byte of 0x80 and up reads
    /// below native's table of catalogs, and a record that is not a script fails the loader's header check, whose
    /// message goes through the host.
    /// </summary>
    public AgeBoxMessage? LoadFailure(long id)
    {
        uint packed = unchecked((uint)id);
        int selector = (int)(packed >> 24);
        int index = (int)(packed & 0xffffff);
        string Number(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (selector >= 0x80) return null;
        Sys4AssetCatalog catalog = Catalog;
        if (selector != 0)
        {
            if (!Catalog.AppendPacks.TryGetValue(selector, out Sys4AssetCatalog? append))
                return AgeBoxMessage.Keyed(AgeBoxText.AppendCatalogMissing, Number(selector));
            if (index >= append.RawSlots.Count)
                return AgeBoxMessage.Keyed(AgeBoxText.AppendFileOpenFailed, "ERROR", Number(index));
            catalog = append;
        }
        else if (index >= catalog.RawSlots.Count)
            return AgeBoxMessage.Keyed(AgeBoxText.FileNumberInvalid, Number(unchecked((int)packed)));
        AssetEntry entry = catalog.RawSlots[index];
        if (entry.IsPlaceholder) return AgeBoxMessage.Keyed(AgeBoxText.FileOpenFailed, entry.Name, "2");
        return null;
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

    /// <summary><see cref="GetByName"/>, then the mounted append catalogs' records by their <c>$n$</c> names, each the
    /// script <see cref="GetById"/> gives for its packed id.</summary>
    public Script? GetByMountedName(string name)
        => GetByName(name) ?? (Catalog.ResolveMountedName(name) is { IsPlaceholder: false } entry
                               && entry.Name.EndsWith(".BIN", StringComparison.OrdinalIgnoreCase)
                                   ? GetById(entry.PackedId) : null);

    public Script RequireByName(string name)
        => GetByName(name) ?? throw new FileNotFoundException($"script is not in SYS4INI: {name}", name);

    private Script Parse(AssetEntry entry)
        => Sys4Loader.Parse(_store.ReadAll(entry), _table, entry.Name, unchecked((uint)entry.PackedId));
}
