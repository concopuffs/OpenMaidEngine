namespace Ome.Engine.Localization;

/// <summary>An entry that a later one at the same scope replaced (localization spec §6.4, §9.4).</summary>
public sealed record TextCatalogOverride(TextEntry Replaced, TextEntry Winner);

/// <summary>
/// The merged, immutable entries of one language (localization spec §4, §6.4). Entries are given in load order, so
/// at equal scope and source the later one wins and the replaced one is recorded in <see cref="Overrides"/>. Exact
/// lookup tries site scope, then script scope, then global. Patterns are kept in <see cref="Patterns"/>; matching
/// them is the presenter's job.
/// </summary>
public sealed class TextCatalog
{
    private readonly Dictionary<string, TextEntry> _global;
    private readonly Dictionary<(string Script, string Key), TextEntry> _script;
    private readonly Dictionary<(string Script, int At, string Key), TextEntry> _site;

    private TextCatalog(string language, IReadOnlyList<TextEntry> entries, IReadOnlyList<TextCatalogOverride> overrides,
                        Dictionary<string, TextEntry> global, Dictionary<(string, string), TextEntry> script,
                        Dictionary<(string, int, string), TextEntry> site, IReadOnlyList<TextEntry> patterns)
    {
        Language = language;
        Entries = entries;
        Overrides = overrides;
        _global = global;
        _script = script;
        _site = site;
        Patterns = patterns;
    }

    public string Language { get; }

    /// <summary>Every entry that survived merging, in load order.</summary>
    public IReadOnlyList<TextEntry> Entries { get; }

    public IReadOnlyList<TextCatalogOverride> Overrides { get; }

    /// <summary>The pattern entries, in load order.</summary>
    public IReadOnlyList<TextEntry> Patterns { get; }

    public int Count => Entries.Count;

    /// <summary>A script name as catalogs write it: upper case, without a <c>.BIN</c> extension, so
    /// <c>SC0100.BIN</c>, <c>sc0100</c> and <c>SC0100</c> are one script and <c>$1$SC2600.bin</c> is
    /// <c>$1$SC2600</c>.</summary>
    public static string NormalizeScriptName(string name)
    {
        string upper = name.Trim().ToUpperInvariant();
        return upper.EndsWith(".BIN", StringComparison.Ordinal) ? upper[..^4] : upper;
    }

    public static TextCatalog Build(string language, IEnumerable<TextEntry> entriesInLoadOrder)
    {
        var global = new Dictionary<string, TextEntry>(StringComparer.Ordinal);
        var script = new Dictionary<(string, string), TextEntry>();
        var site = new Dictionary<(string, int, string), TextEntry>();
        var patterns = new Dictionary<(string?, int?, string), TextEntry>();
        var order = new List<TextEntry>();
        var removed = new HashSet<TextEntry>(ReferenceEqualityComparer.Instance);
        var overrides = new List<TextCatalogOverride>();

        void Put<TKey>(Dictionary<TKey, TextEntry> map, TKey key, TextEntry entry) where TKey : notnull
        {
            if (map.TryGetValue(key, out TextEntry? replaced))
            {
                overrides.Add(new TextCatalogOverride(replaced, entry));
                removed.Add(replaced);
            }
            map[key] = entry;
            order.Add(entry);
        }

        foreach (TextEntry raw in entriesInLoadOrder)
        {
            TextEntry entry = raw.Script is { } name ? raw with { Script = NormalizeScriptName(name) } : raw;
            if (entry.At != null && entry.Script == null)
                throw new ArgumentException($"{entry.Origin}: an entry with 'at' needs a script");
            if (entry.IsPattern) Put(patterns, (entry.Script, entry.At, entry.Source), entry);
            else if (entry.At is { } at) Put(site, (entry.Script!, at, entry.Key), entry);
            else if (entry.Script is { } owner) Put(script, (owner, entry.Key), entry);
            else Put(global, entry.Key, entry);
            if (order.Count - removed.Count > TextCatalogLimits.MaxEntriesPerLanguage)
                throw new ArgumentException(
                    $"language '{language}' has more than {TextCatalogLimits.MaxEntriesPerLanguage} entries");
        }
        TextEntry[] kept = order.Where(entry => !removed.Contains(entry)).ToArray();
        return new TextCatalog(language, kept, overrides, global, script, site,
            kept.Where(entry => entry.IsPattern).ToArray());
    }

    /// <summary>The exact entry for <paramref name="source"/> drawn by <paramref name="script"/> at base offset
    /// <paramref name="at"/>: site scope first, then script, then global (spec §6.4 steps 1–3).</summary>
    public bool TryResolveExact(string source, string? script, int? at, out TextEntry entry)
    {
        string? owner = script == null ? null : NormalizeScriptName(script);
        if (owner != null && at is { } offset && _site.TryGetValue((owner, offset, source), out entry!)) return true;
        if (owner != null && _script.TryGetValue((owner, source), out entry!)) return true;
        return _global.TryGetValue(source, out entry!);
    }
}
