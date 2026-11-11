namespace Ome.Engine.Sys4;

/// <summary>One package's files for an asset name (localization spec §12): whether it supplies the plain asset of
/// that name, and its language assets, by language tag, as file paths.</summary>
public sealed record LanguageAssetLayer(string Mod, bool Plain, IReadOnlyDictionary<string, string> Files);

/// <summary>
/// The language assets of a mounted stack (localization spec §12), by asset name, each with the packages that supply
/// it in load order. Load order decides between packages; within one package its language asset ranks just above its
/// plain asset. So the last package that has a file for a name decides: its language asset for the first tag of the
/// selected chain it has, otherwise its plain asset, which the store below already serves. A package that has only
/// language assets for other languages leaves the decision to the packages before it.
/// </summary>
public sealed class LanguageAssetTable
{
    private readonly Dictionary<string, IReadOnlyList<LanguageAssetLayer>> _byName;

    public LanguageAssetTable(IReadOnlyDictionary<string, IReadOnlyList<LanguageAssetLayer>> byName)
        => _byName = new Dictionary<string, IReadOnlyList<LanguageAssetLayer>>(byName, StringComparer.OrdinalIgnoreCase);

    public static LanguageAssetTable Empty { get; } = new(new Dictionary<string, IReadOnlyList<LanguageAssetLayer>>());

    /// <summary>How many asset names have a language asset.</summary>
    public int Count => _byName.Count;

    /// <summary>The language tags any asset has a file for.</summary>
    public IReadOnlyList<string> Languages => _byName.Values.SelectMany(layers => layers)
        .SelectMany(layer => layer.Files.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>The file that serves <paramref name="name"/> while <paramref name="chain"/> is selected (the language,
    /// then its fallbacks), or null when the store below serves it.</summary>
    public string? Resolve(string name, IReadOnlyList<string> chain)
    {
        if (chain.Count == 0 || !_byName.TryGetValue(name, out IReadOnlyList<LanguageAssetLayer>? layers)) return null;
        for (int index = layers.Count - 1; index >= 0; index--)
        {
            LanguageAssetLayer layer = layers[index];
            foreach (string tag in chain)
                if (layer.Files.TryGetValue(tag, out string? path)) return path;
            if (layer.Plain) return null;
        }
        return null;
    }
}

/// <summary>
/// Serves a stack's language assets (localization spec §12) over another store. The selected chain can change while
/// the game runs: a switch takes effect when an asset is next read, as the spec says, so whatever a host has already
/// decoded stays until it reads the asset again.
/// </summary>
public sealed class LanguageAssetStore : IAssetStore
{
    private readonly IAssetStore _inner;
    private volatile string[] _chain = [];

    public LanguageAssetStore(IAssetStore inner, LanguageAssetTable table)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Table = table ?? throw new ArgumentNullException(nameof(table));
    }

    public LanguageAssetTable Table { get; }

    /// <summary>The selected language and its fallbacks; empty while no language is selected.</summary>
    public IReadOnlyList<string> Chain => _chain;

    public void Select(IReadOnlyList<string> chain) => _chain = chain.ToArray();

    /// <summary>The language asset that serves <paramref name="entry"/> now, or null.</summary>
    public string? Resolve(AssetEntry entry) => entry.IsPlaceholder ? null : Table.Resolve(entry.Name, _chain);

    /// <summary>Raised with the entry and the file each time a language asset is read in its place, on the reading
    /// thread, so a host can log which version a game loaded.</summary>
    public event Action<AssetEntry, string>? Served;

    private string? Serve(AssetEntry entry)
    {
        string? path = Resolve(entry);
        if (path != null) Served?.Invoke(entry, path);
        return path;
    }

    public Stream Open(AssetEntry entry)
        => Serve(entry) is { } path
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan)
            : _inner.Open(entry);

    public byte[] ReadAll(AssetEntry entry) => Serve(entry) is { } path ? File.ReadAllBytes(path) : _inner.ReadAll(entry);

    public (byte[] Bytes, bool Png) ReadImage(AssetEntry entry)
        => Serve(entry) is { } path
            ? (File.ReadAllBytes(path), path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            : _inner.ReadImage(entry);
}
